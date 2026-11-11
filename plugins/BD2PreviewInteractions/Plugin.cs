using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Proto.Design.common;
using UnityEngine;
using UnityEngine.UI;

namespace BD2PreviewInteractions;

[BepInPlugin("local.bd2.previewinteractions", "BD2 Preview Interactions", "0.2.8")]
[BepInDependency("lynesth.bd2.browndustx", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    internal const int Local18 = 1_900_000_018;
    internal static Dictionary<int, SceneSpec> Specs { get; private set; } = DefaultScenes();
    internal static LocalInteractionSettings Settings { get; private set; } = new();
    internal static Dictionary<int, SceneSpec> DefaultScenes() => new()
    {
        [Local18] = new(Local18, "illust_dating18", 396, "illust_loop_char000396", "IllustInventory_1/illust_inven_char000396_202.png", "莎赫拉查德 · 从容的王牌"),
        [1_900_000_021] = new(1_900_000_021, "illust_dating21", 65193, "illust_loop_char065193", "IllustInventory_1/illust_inven_char065193_224.png", "尤里 · 永远的吉祥物"),
        [1_900_000_019] = new(1_900_000_019, "illust_dating19", 67104, "illust_loop_char067104", "IllustInventory_1/illust_inven_char067104_206.png", "格兰希特 · 战地医疗兵"),
        [1_900_000_009] = new(1_900_000_009, "illust_dating9", 1197, "illust_loop_char001197", "IllustInventory_1/illust_inven_char001197_179.png", "泰瑞丝 · 奶牛比基尼"),
        [1_900_000_012] = new(1_900_000_012, "illust_dating12", 61492, "illust_loop_char061492", "IllustInventory_1/illust_inven_char061492_188.png", "杰尼斯 · 神秘面纱兔女郎"),
        [1_900_000_007] = new(1_900_000_007, "illust_dating7", 296, "illust_loop_char000296", "IllustInventory_1/illust_inven_char000296_174.png", "悠丝缇亚 · 仲夏之梦"),
    };

    internal static Plugin? Instance { get; private set; }
    internal static string ExportRoot => Settings.ExportDirectory;

    private readonly Dictionary<int, LobbySettingItemTable> localRows = new();
    private Harmony? harmony;
    private MethodInfo? rowGetter;
    private LocalSceneHost? host;
    private LocalSceneHost? pendingHost;
    private string notice = "";
    private float noticeUntil;
    private ConfigEntry<bool> applyEnabledMods = null!;
    private ConfigEntry<int> controlsTop = null!;
    private ConfigEntry<string> audioDirectory = null!;
    internal static string AudioRoot => Settings.AudioDirectory;
    internal bool ApplyEnabledMods => applyEnabledMods.Value;
    internal float ControlsTop => Mathf.Clamp(controlsTop.Value, 0, Mathf.Max(0, Screen.height - 260));

    private void LateUpdate() => host?.HideOtherVisuals();

    private void Awake()
    {
        Instance = this;
        applyEnabledMods = Config.Bind("Scenes", "ApplyEnabledDatingMods", true,
            "Apply the enabled BrownDustX mod for each local scene's illust_dating ID. False uses official assets.");
        controlsTop = Config.Bind("Interface", "ControlsTop", 160,
            "Distance in screen pixels from the top edge to the local interaction controls.");
        audioDirectory = Config.Bind("Scenes", "AudioDirectory", @"F:\Neowiz\Brown-Dust-2-Asset-master\audio\dating",
            "Directory containing the original SHA-256-named dating OGG files. JSON export does not copy audio.");
        ReloadCatalog();
        try
        {
            rowGetter = typeof(SpineInteractionUI).Assembly.GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .FirstOrDefault(method => method.ReturnType == typeof(LobbySettingItemTable)
                    && method.GetParameters().Length == 1
                    && method.GetParameters()[0].ParameterType == typeof(int)
                    && method.Name == "ὩὬὡὬὫὪὤὤὧὮὧ");
            if (rowGetter == null) throw new MissingMethodException("LobbySettingItemTable lookup");

            harmony = new Harmony("local.bd2.previewinteractions");
            OfficialSpineData.Install(harmony);
            harmony.Patch(AccessTools.Method(typeof(SpineInteractionUI), "SetUI", new[] { typeof(int), typeof(List<int>) }),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(SetUiPrefix)));
            harmony.Patch(rowGetter, prefix: new HarmonyMethod(typeof(Plugin), nameof(RowPrefix)));
            harmony.Patch(AccessTools.Method(typeof(SpineInteractionUI), "SetCharSelect", new[] { typeof(int), typeof(bool), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(SelectPrefix)));
            harmony.Patch(AccessTools.Method(typeof(SpineInteractionUI), "OnDisable"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(DisablePrefix)));
            harmony.Patch(AccessTools.Method(typeof(SpineInteractionUI), "get_CurrentSelectItemId"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(SelectedIdPostfix)));
            harmony.Patch(AccessTools.Method(typeof(SpineInteractionLoopScrollItem), "SetUI", new[] { typeof(bool), typeof(int), typeof(bool) }),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(CellPrefix)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(BadgePostfix)));
            harmony.Patch(AccessTools.Method(typeof(SpineInteractionUI), "OnClickUI", new[] { typeof(GameObject) }),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(NativeClickPrefix)));
            harmony.Patch(AccessTools.Method(typeof(SpineInteractionRecordController), "OnClickUI"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(RecordClickPrefix)));
            harmony.Patch(AccessTools.Method(typeof(SpineInteractionReplayController), "OnClickUI"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(RecordClickPrefix)));
            harmony.Patch(AccessTools.Method(typeof(SpineInteractionHintController), "OnClickUI"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(RecordClickPrefix)));
            Logger.LogInfo("Preview interaction avatar entries are ready");
        }
        catch (Exception error)
        {
            Logger.LogError($"Game UI signatures are incompatible; plugin disabled: {error}");
            harmony?.UnpatchSelf();
        }
    }

    private void OnDestroy()
    {
        CloseLocal();
        LocalAvatar.ReleaseAssets();
        harmony?.UnpatchSelf();
        Instance = null;
    }

    private void OnGUI()
    {
        if (Time.unscaledTime >= noticeUntil || string.IsNullOrEmpty(notice)) return;
        var rect = new Rect(Math.Max(12, (Screen.width - 520) / 2), Screen.height - 88, Math.Min(520, Screen.width - 24), 48);
        GUI.Box(rect, notice);
    }

    internal void Notify(string message)
    {
        Logger.LogInfo(message);
        notice = message;
        noticeUntil = Time.unscaledTime + 6f;
    }

    internal void LogDiagnostic(string message) => Logger.LogWarning(message);

    private void ReloadCatalog()
    {
        try
        {
            Settings = LocalInteractionSettings.Read(audioDirectory.Value);
            Specs = Settings.Enabled ? Settings.Scenes : new Dictionary<int, SceneSpec>();
            localRows.Clear();
        }
        catch (Exception error) { Logger.LogError($"Local interaction settings could not be loaded: {error.Message}"); }
    }

    private LobbySettingItemTable? GetOriginalRow(int id)
    {
        try { return rowGetter?.Invoke(null, new object[] { id }) as LobbySettingItemTable; }
        catch { return null; }
    }

    private void PrepareRows(List<int> existingIds)
    {
        var originals = existingIds.Select(id => (id, row: GetOriginalRow(id)))
            .Where(item => item.row != null).ToList();
        foreach (var spec in Specs.Values.ToArray())
        {
            if (existingIds.Contains(spec.LocalId) && !localRows.ContainsKey(spec.LocalId))
            {
                Logger.LogError($"Local avatar ID conflicts with game data: {spec.LocalId}");
                localRows.Remove(spec.LocalId);
                Specs.Remove(spec.LocalId);
                continue;
            }
            var source = originals.FirstOrDefault(item => item.row!.CostumeId == spec.CostumeId).row
                ?? originals.FirstOrDefault().row;
            if (source == null) continue;
            var row = source.Clone();
            row.Id = spec.LocalId;
            row.CostumeId = spec.CostumeId;
            row.InteractionResourceName = "Illust_dating" + spec.SceneId.Substring("illust_dating".Length);
            // Preserve the game's valid row metadata. Local cells draw their own portrait.
            localRows[spec.LocalId] = row;
        }
    }

    private static void SetUiPrefix(ref int __0, ref List<int> __1)
    {
        var plugin = Instance;
        if (plugin == null || __1 == null) return;
        plugin.CloseLocal();
        var previousCostume = Specs.TryGetValue(__0, out var selected) ? selected.CostumeId : 0;
        var previouslyInjected = new HashSet<int>(plugin.localRows.Keys);
        __1 = __1.Where(id => !previouslyInjected.Contains(id)).ToList();
        plugin.ReloadCatalog();
        plugin.PrepareRows(__1);
        foreach (var spec in Specs.Values)
        {
            if (!plugin.localRows.ContainsKey(spec.LocalId) || __1.Contains(spec.LocalId)) continue;
            var originalIndex = __1.FindIndex(id => plugin.GetOriginalRow(id)?.CostumeId == spec.CostumeId);
            __1.Insert(originalIndex >= 0 ? originalIndex + 1 : __1.Count, spec.LocalId);
        }
        // A local selection can be remembered by the native UI. When it is removed,
        // choose its original character (or another official entry) before SetUI runs.
        if (previouslyInjected.Contains(__0) && !__1.Contains(__0)) {
            var original = __1.FirstOrDefault(id => previousCostume != 0 && plugin.GetOriginalRow(id)?.CostumeId == previousCostume);
            __0 = original != 0 ? original : __1.FirstOrDefault();
        }
    }

    private static bool RowPrefix(int __0, ref LobbySettingItemTable __result)
    {
        var plugin = Instance;
        if (plugin == null || !plugin.localRows.TryGetValue(__0, out var row)) return true;
        __result = row;
        return false;
    }

    private static bool CellPrefix(SpineInteractionLoopScrollItem __instance, bool __0, int __1, bool __2)
    {
        var image = AccessTools.Field(typeof(SpineInteractionLoopScrollItem), "_imageCharSelect")?.GetValue(__instance) as RawImage;
        if (!Specs.ContainsKey(__1))
        {
            if (image != null) { image.enabled = true; image.color = Color.white; }
            return true;
        }
        var fields = AccessTools.GetDeclaredFields(typeof(SpineInteractionLoopScrollItem));
        fields.First(field => field.FieldType == typeof(int) && field.Name.Contains("BackingField")).SetValue(__instance, __1);
        var root = AccessTools.Field(typeof(LoopScrollItemBase), "_uiBase")?.GetValue(__instance) as SpineInteractionUI;
        fields.First(field => field.FieldType == typeof(SpineInteractionUI)).SetValue(__instance, root);
        AccessTools.Method(typeof(SpineInteractionLoopScrollItem), "SetActive", new[] { typeof(bool) })?.Invoke(__instance, new object[] { __0 });
        // Keep the native raycast surface alive so the cell's existing click
        // listener still works; only its visual is replaced by LocalAvatar.
        if (image != null) { image.enabled = true; image.texture = null; image.color = Color.clear; }
        if (AccessTools.Field(typeof(SpineInteractionLoopScrollItem), "_goSelect")?.GetValue(__instance) is GameObject selection) selection.SetActive(__2);
        if (AccessTools.Field(typeof(SpineInteractionLoopScrollItem), "_achievementGauge")?.GetValue(__instance) is Component gauge) gauge.gameObject.SetActive(false);
        return false;
    }

    private static bool NativeClickPrefix(SpineInteractionUI __instance, GameObject __0)
    {
        var current = Instance?.host;
        if (current?.IsOpen != true || __0 == null) return true;
        var reset = AccessTools.Field(typeof(SpineInteractionUI), "_resetButton")?.GetValue(__instance) as GameObject;
        if (reset == null || (__0 != reset && !__0.transform.IsChildOf(reset.transform))) return true;
        current.Restart();
        return false;
    }

    private static bool SelectPrefix(SpineInteractionUI __instance, int __0)
    {
        var plugin = Instance;
        if (plugin == null) return true;
        if (!Specs.TryGetValue(__0, out var spec))
        {
            plugin.CloseLocal();
            return true;
        }
        var record = AccessTools.Field(typeof(SpineInteractionUI), "_recordController")?.GetValue(__instance) as SpineInteractionRecordController;
        var replay = AccessTools.Field(typeof(SpineInteractionUI), "_replayController")?.GetValue(__instance) as SpineInteractionReplayController;
        var recording = record != null && AccessTools.Method(typeof(SpineInteractionRecordController), "get_IsRecording")?.Invoke(record, null) is true;
        var replaying = replay != null && AccessTools.Method(typeof(SpineInteractionReplayController), "get_IsReplaying")?.Invoke(replay, null) is true;
        if (recording || replaying)
        {
            plugin.Notify("请先停止官方录制或回放，再打开本地互动");
            return false;
        }
        plugin.pendingHost?.Close();
        plugin.pendingHost = new LocalSceneHost(plugin, __instance, spec);
        plugin.Notify($"正在加载本地互动：{spec.Title}");
        plugin.StartCoroutine(plugin.pendingHost.Open());
        return false;
    }

    private static void DisablePrefix() => Instance?.CloseLocal();

    private static bool RecordClickPrefix(ref bool __result)
    {
        if (Instance?.host?.IsActive != true && Instance?.pendingHost?.IsActive != true) return true;
        // Leave navigation available to the root UI while skipping account recording handlers.
        __result = false;
        return false;
    }

    private static void SelectedIdPostfix(ref int __result)
    {
        var host = Instance?.host;
        if (host != null && host.IsOpen) __result = host.Spec.LocalId;
    }

    private static void BadgePostfix(SpineInteractionLoopScrollItem __instance, bool __0, int __1)
    {
        var image = AccessTools.Field(typeof(SpineInteractionLoopScrollItem), "_imageCharSelect")?.GetValue(__instance) as RawImage;
        if (image != null)
        {
            var avatar = image.GetComponent<LocalAvatar>() ?? image.gameObject.AddComponent<LocalAvatar>();
            avatar.Bind(__0 && Specs.TryGetValue(__1, out var spec) ? spec : null);
        }
        var existing = __instance.transform.Find("PreviewLocalBadge");
        if (!Specs.ContainsKey(__1) || !__0)
        {
            if (existing != null) existing.gameObject.SetActive(false);
            return;
        }
        if (existing != null) { existing.gameObject.SetActive(true); return; }
        var badge = new GameObject("PreviewLocalBadge", typeof(RectTransform), typeof(Image));
        badge.layer = __instance.gameObject.layer;
        badge.transform.SetParent(__instance.transform, false);
        var rect = (RectTransform)badge.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-2, -2);
        rect.sizeDelta = new Vector2(21, 21);
        var background = badge.GetComponent<Image>();
        background.color = new Color(0.12f, 0.33f, 0.8f, 0.95f);
        background.raycastTarget = false;
        var label = new GameObject("P", typeof(RectTransform), typeof(Text));
        label.transform.SetParent(badge.transform, false);
        var textRect = (RectTransform)label.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        var text = label.GetComponent<Text>();
        text.text = "P";
        text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = 15;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
    }

    internal void RefreshAvatarSelection(SpineInteractionUI ui)
    {
        try
        {
            var list = AccessTools.Field(typeof(SpineInteractionUI), "_loopScrollRectCharSelectScroll")?.GetValue(ui);
            list?.GetType().GetMethod("RefreshCells", new[] { typeof(bool) })?.Invoke(list, new object[] { false });
            foreach (var name in new[] { "_imageCharSelectScroll", "_imageCharSelectScrollOpen" })
            {
                if (AccessTools.Field(typeof(SpineInteractionUI), name)?.GetValue(ui) is not RawImage image) continue;
                var avatar = image.GetComponent<LocalAvatar>() ?? image.gameObject.AddComponent<LocalAvatar>();
                avatar.Bind(host?.IsOpen == true ? host.Spec : null);
            }
        }
        catch (Exception error) { Logger.LogWarning($"Cannot refresh local avatar selection: {error.Message}"); }
    }

    internal void ClearHeaderAvatars(SpineInteractionUI ui)
    {
        foreach (var name in new[] { "_imageCharSelectScroll", "_imageCharSelectScrollOpen" })
            if (AccessTools.Field(typeof(SpineInteractionUI), name)?.GetValue(ui) is RawImage image)
                image.GetComponent<LocalAvatar>()?.Bind(null);
    }

    private void CloseLocal()
    {
        pendingHost?.Close();
        pendingHost = null;
        host?.Close();
        host = null;
    }

    internal bool CommitLocal(LocalSceneHost next)
    {
        if (pendingHost != next) return false;
        host?.Close();
        host = next;
        pendingHost = null;
        return true;
    }
}

internal sealed class SceneSpec
{
    internal SceneSpec(int localId, string sceneId, int costumeId, string iconName, string avatarAddress, string title, bool useMods = true)
    {
        LocalId = localId;
        SceneId = sceneId;
        CostumeId = costumeId;
        IconName = iconName;
        AvatarAddress = avatarAddress;
        Title = title;
        UseMods = useMods;
    }

    internal int LocalId { get; }
    internal string SceneId { get; }
    internal int CostumeId { get; }
    internal string IconName { get; }
    internal string AvatarAddress { get; }
    internal string Title { get; }
    internal bool UseMods { get; }
}
