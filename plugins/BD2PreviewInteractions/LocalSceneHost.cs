using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Spine.Unity;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace BD2PreviewInteractions;

internal sealed class LocalSceneHost
{
    private readonly Plugin plugin;
    private readonly SpineInteractionUI ui;
    private readonly Dictionary<GameObject, bool> previousChildren = new();
    private readonly Dictionary<Behaviour, bool> previousBehaviours = new();
    private readonly Dictionary<GameObject, bool> previousControls = new();
    private readonly List<SkeletonDataAsset> isolatedData = new();
    private RectTransform? sceneRoot;
    private GameObject? staging;
    private GameObject? prefab;
    private GameObject? overlay;
    private GameObject? hudRoot;
    private PreviewInteractionController? controller;
    private AsyncOperationHandle<GameObject> prefabHandle;
    private bool hasPrefabHandle;
    private bool closed;
    private bool committed;

    internal SceneSpec Spec { get; }
    internal bool IsOpen { get; private set; }
    internal bool IsActive => !closed;

    internal LocalSceneHost(Plugin plugin, SpineInteractionUI ui, SceneSpec spec)
    { this.plugin = plugin; this.ui = ui; Spec = spec; }

    // Flatten nested iterators so an asynchronous loader exception always restores
    // the official UI instead of escaping Unity's coroutine runner.
    internal IEnumerator Open()
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(Prepare());
        while (stack.Count > 0 && !closed)
        {
            object? current = null;
            bool moved = false;
            Exception? failure = null;
            try { moved = stack.Peek().MoveNext(); if (moved) current = stack.Peek().Current; }
            catch (Exception error) { failure = error; }
            if (failure != null) { Fail(failure); break; }
            if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (current is IEnumerator nested) stack.Push(nested);
            else yield return current;
        }
        foreach (var iterator in stack) (iterator as IDisposable)?.Dispose();
    }

    private IEnumerator Prepare()
    {
        var exportRoot = Plugin.ExportRoot;
        var audioRoot = Plugin.AudioRoot;
        var read = Task.Run(() => ExportedScene.Read(Spec.SceneId, exportRoot, audioRoot));
        while (!read.IsCompleted) yield return null;
        var data = read.GetAwaiter().GetResult();
        sceneRoot = AccessTools.Field(typeof(SpineInteractionUI), "_spinRootParent")?.GetValue(ui) as RectTransform;
        if (sceneRoot == null) throw new InvalidOperationException("无法定位游戏的互动显示区域");

        // Loading must not blank the scene or activate native prefab controllers.
        var address = $"Char/Datingillust/Illust_dating{Spec.SceneId.Substring("illust_dating".Length)}.prefab";
        prefabHandle = Addressables.LoadAssetAsync<GameObject>(address);
        hasPrefabHandle = true;
        while (!prefabHandle.IsDone) yield return null;
        if (prefabHandle.Status != AsyncOperationStatus.Succeeded || prefabHandle.Result == null)
            throw new InvalidOperationException($"无法加载互动资源：{address}");
        staging = MakeRoot("PreviewLocalScene", sceneRoot);
        prefab = UnityEngine.Object.Instantiate(prefabHandle.Result, staging.transform, false);
        prefab.name = "PreviewLocal_" + Spec.SceneId;
        LocalVisuals.DisableNativeScripts(prefab);
        var graphics = new List<SkeletonGraphic>();
        yield return BuildSkeletons(data.Scene, graphics);

        overlay = MakeRoot("PreviewLocalController", sceneRoot);
        controller = overlay.AddComponent<PreviewInteractionController>();
        var audio = overlay.AddComponent<LocalAudioPlayer>();
        audio.Initialize();
        controller.Initialize(data.Scene, graphics.ToArray(), (RectTransform)overlay.transform, prefab.transform, audio, Fail);
        controller.ValidateScene();
        var uiCanvas = ui.GetComponent<Canvas>() ?? sceneRoot.GetComponentInParent<Canvas>(true);
        if (uiCanvas == null) throw new InvalidOperationException("无法定位互动界面的 Canvas");
        hudRoot = MakeRoot("PreviewLocalHud", uiCanvas.transform);
        hudRoot.AddComponent<LocalInteractionHud>().Initialize(ui, controller, uiCanvas);
        if (!plugin.CommitLocal(this)) { Close(); yield break; }
        committed = true;
        SuspendNativeInteraction();
        HideOtherVisuals();
        staging.SetActive(true);
        overlay.SetActive(true);
        hudRoot.SetActive(true);
        controller.Begin();
        IsOpen = true;
        plugin.RefreshAvatarSelection(ui);
        plugin.Notify($"已打开本地互动：{Spec.Title}");
    }

    private static GameObject MakeRoot(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(LocalSceneTag));
        obj.layer = parent.gameObject.layer;
        obj.SetActive(false);
        obj.transform.SetParent(parent, false);
        var rect = (RectTransform)obj.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return obj;
    }

    internal void HideOtherVisuals()
    {
        if (closed || !committed || sceneRoot == null) return;
        // Disabling the native gauge controller does not hide its graphics.
        // Keep saved native controls hidden even if a native animation/callback
        // tries to reactivate them while a local scene is open.
        foreach (var control in previousControls.Keys)
            if (control != null && control.activeSelf) control.SetActive(false);
        foreach (Transform child in sceneRoot)
        {
            var visual = child.gameObject;
            if (visual.GetComponent<LocalSceneTag>() != null) continue;
            if (!previousChildren.ContainsKey(visual)) previousChildren[visual] = visual.activeSelf;
            if (visual.activeSelf) visual.SetActive(false);
        }
    }

    private IEnumerator BuildSkeletons(JObject scene, List<SkeletonGraphic> result)
    {
        var existing = prefab!.GetComponentsInChildren<SkeletonGraphic>(true);
        if (existing.Length == 0) throw new InvalidDataException("预制体中没有 Spine 画面");
        var names = (scene["groups"] as JArray ?? new JArray()).OfType<JObject>()
            .Select(g => g.Value<string>("skeleton")).Where(n => !string.IsNullOrWhiteSpace(n)).Cast<string>().Distinct().ToList();
        if (names.Count == 0) names.Add(scene.Value<string>("skeleton") ?? Spec.SceneId);
        foreach (var name in names)
        {
            if (name != Spec.SceneId && !name.StartsWith(Spec.SceneId + "_", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"骨骼不属于当前约会：{name}");
            var graphic = existing.FirstOrDefault(g => g.SkeletonDataAsset != null &&
                g.SkeletonDataAsset.name.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase));
            if (graphic == null)
            {
                var clone = UnityEngine.Object.Instantiate(existing[0].gameObject, existing[0].transform.parent, false);
                LocalVisuals.DisableNativeScripts(clone);
                graphic = clone.GetComponent<SkeletonGraphic>();
                if (graphic == null) throw new InvalidDataException("无法创建 Spine 画面");
            }
            var original = OfficialSpineData.Resolve(name);
            var isolated = SkeletonDataAsset.CreateRuntimeInstance(original.Raw, original.Atlases, false, original.Scale);
            isolatedData.Add(isolated);
            isolated.defaultMix = original.DefaultMix;
            if (plugin.ApplyEnabledMods && Spec.UseMods) yield return OfficialSpineData.PrepareEnabledMod(name);
            var modded = plugin.ApplyEnabledMods && Spec.UseMods && OfficialSpineData.ApplyEnabledMod(isolated, name);
            isolated.name = "PreviewLocalOriginal" + name;
            var parse = ManagedSkeletonLoader.Read(isolated.skeletonJSON, isolated.atlasAssets, isolated.scale);
            while (!parse.IsCompleted) yield return null;
            var parsed = parse.GetAwaiter().GetResult();
            ManagedSkeletonLoader.SetData(isolated, parsed);
            if (modded)
            {
                var groupIds = (scene["groups"] as JArray ?? new JArray()).OfType<JObject>()
                    .Where(g => (g.Value<string>("skeleton") ?? names[0]) == name).Select(g => g.Value<int>("id")).ToArray();
                var points = (scene["points"] as JArray ?? new JArray()).OfType<JObject>()
                    .Where(p => groupIds.Contains(p.Value<int>("group")));
                var bones = points.SelectMany(p => new[] { p.Value<string>("bone"), (p["zone"] as JObject)?.Value<string>("bone") }
                    .Concat((p["destinations"] as JArray ?? new JArray()).OfType<JObject>().Select(d => d.Value<string>("bone"))))
                    .Where(b => !string.IsNullOrEmpty(b)).Cast<string>().Distinct().Where(b => parsed.FindBone(b) == null).ToArray();
                if (bones.Length > 0)
                {
                    var baseline = ManagedSkeletonLoader.Read(original.Raw, original.Atlases, original.Scale);
                    while (!baseline.IsCompleted) yield return null;
                    var restored = original.RestoreInteractionBones(isolated, bones, baseline.GetAwaiter().GetResult());
                    plugin.LogDiagnostic($"Restored {restored} input helper bones: {name}");
                }
            }
            graphic.skeletonDataAsset = isolated;
            graphic.Initialize(true);
            if (graphic.Skeleton == null) throw new InvalidDataException($"骨骼初始化失败：{name}");
            result.Add(graphic);
            plugin.LogDiagnostic($"Local resource {name}: {(modded ? "enabled BrownDustX mod" : "official")}");
            yield return null;
        }
        foreach (var graphic in existing) if (graphic != null && !result.Contains(graphic)) graphic.gameObject.SetActive(false);
    }

    private void SuspendNativeInteraction()
    {
        foreach (var name in new[] { "_spineInteractionController", "_spineInteractionGaugeController", "_dragZoomController", "_recordController", "_replayController", "_hintController" })
        {
            if (AccessTools.Field(typeof(SpineInteractionUI), name)?.GetValue(ui) is not Behaviour component) continue;
            previousBehaviours[component] = component.enabled;
            component.enabled = false;
            if (name == "_spineInteractionGaugeController")
            {
                var gauge = AccessTools.Field(component.GetType(), "_gaugeUI")?.GetValue(component) as Component;
                SaveAndHide(gauge?.gameObject);
            }
            if (name == "_hintController")
            {
                var hint = AccessTools.Field(component.GetType(), "_hintSettingButton")?.GetValue(component) as Component;
                SaveAndHide(hint?.gameObject);
            }
        }
        foreach (var name in new[] { "_resetButton", "_interactionToolbarParent", "_closeAchievementGauge", "_openAchievementGauge", "_objSubtitle", "_objDescription", "_tooltipButton", "_tooltipCloseButton", "_goRewardReceiveLabel" })
        {
            var value = AccessTools.Field(typeof(SpineInteractionUI), name)?.GetValue(ui);
            SaveAndHide(value is GameObject go ? go : (value as Component)?.gameObject);
        }
        foreach (var field in typeof(SpineInteractionUI).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic))
        {
            var value = field.GetValue(ui);
            if (value is IEnumerable list && field.FieldType.FullName?.Contains("FMOD.Studio.EventInstance") == true)
                foreach (var item in list) StopNativeSound(item);
            else StopNativeSound(value);
        }
    }

    private void SaveAndHide(GameObject? control)
    {
        if (control == null) return;
        if (!previousControls.ContainsKey(control)) previousControls[control] = control.activeSelf;
        control.SetActive(false);
    }

    private static void StopNativeSound(object? value)
    {
        if (value == null || value.GetType().FullName != "FMOD.Studio.EventInstance") return;
        var method = value.GetType().GetMethod("stop");
        var argument = method?.GetParameters().FirstOrDefault()?.ParameterType;
        if (method == null || argument == null || !argument.IsEnum) return;
        try { method.Invoke(value, new[] { Enum.Parse(argument, "IMMEDIATE") }); } catch { }
    }

    internal void Restart()
    {
        try { controller?.Restart(); }
        catch (Exception error) { Fail(error); }
    }

    private void Fail(Exception error)
    {
        if (closed) return;
        plugin.LogDiagnostic($"Local scene failed: {error}");
        plugin.Notify($"{Spec.Title}：{error.GetBaseException().Message}，已恢复官方界面");
        Close();
    }

    internal void Close()
    {
        if (closed) return;
        closed = true;
        IsOpen = false;
        if (committed) plugin.ClearHeaderAvatars(ui);
        if (controller != null) controller.Shutdown();
        foreach (var obj in new[] { hudRoot, overlay, staging })
            if (obj != null) { obj.SetActive(false); UnityEngine.Object.Destroy(obj); }
        foreach (var pair in previousChildren) if (pair.Key != null) pair.Key.SetActive(pair.Value);
        foreach (var pair in previousControls) if (pair.Key != null) pair.Key.SetActive(pair.Value);
        foreach (var pair in previousBehaviours) if (pair.Key != null) pair.Key.enabled = pair.Value;
        previousChildren.Clear(); previousControls.Clear(); previousBehaviours.Clear();
        plugin.StartCoroutine(ReleaseResources());
    }

    private IEnumerator ReleaseResources()
    {
        // Unity destroys the visuals at the end of this frame, before releasing
        // addressable textures/materials referenced by their final render pass.
        yield return null;
        foreach (var asset in isolatedData) if (asset != null) UnityEngine.Object.Destroy(asset);
        isolatedData.Clear();
        if (!hasPrefabHandle) yield break;
        while (!prefabHandle.IsDone) yield return null;
        if (prefabHandle.IsValid()) Addressables.Release(prefabHandle);
        hasPrefabHandle = false;
    }
}

internal sealed class ExportedScene
{
    internal JObject Scene { get; private set; } = null!;

    internal static ExportedScene Read(string sceneId, string exportRoot, string audioRoot)
    {
        var path = Path.Combine(exportRoot, sceneId + ".json");
        if (!File.Exists(path)) throw new FileNotFoundException("缺少导出 JSON，请先在 Preview 导出", path);
        var envelope = JObject.Parse(File.ReadAllText(path));
        if (envelope.Value<int>("schemaVersion") != 1 || envelope.Value<string>("sceneId") != sceneId)
            throw new InvalidDataException("导出格式或场景编号错误");
        if (envelope.Value<long>("expiresAtMs") <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            throw new InvalidDataException("导出数据已过期，请重新导出");
        var scene = envelope["scene"] as JObject ?? throw new InvalidDataException("缺少场景配置");
        if (scene["groups"] is not JArray || scene["points"] is not JArray) throw new InvalidDataException("场景配置不完整");
        var events = scene["audio"]?["events"] as JObject;
        if (events == null || events.Count == 0) throw new InvalidDataException("场景缺少音频映射");
        foreach (var entry in events.Properties())
        {
            var hash = entry.Value.Value<string>("file") ?? "";
            if (hash.Length != 64 || hash.Any(ch => !Uri.IsHexDigit(ch))) throw new InvalidDataException("音频哈希无效");
            if (!File.Exists(Path.Combine(audioRoot, hash + ".ogg"))) throw new FileNotFoundException($"缺少音频：{hash}");
        }
        // Audio is verified and decoded only when its event is first played.
        return new ExportedScene { Scene = scene };
    }
}
