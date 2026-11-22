using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BD2PreviewInteractions;

[BepInPlugin(PluginId, "BD2 Preview Interactions", "0.5.1")]
[BepInDependency("lynesth.bd2.browndustx", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    private const string PluginId = "local.bd2.previewinteractions";

    internal static Plugin? Instance { get; private set; }
    internal static LocalInteractionSettings Settings { get; private set; } = new();

    private Harmony? nativePatches;
    private Harmony? nativeRewardPatches;
    private Harmony? previewPatches;
    private Harmony? spinePatches;
    private MethodInfo? rawDataInstance;
    private MethodInfo? nativeRewardOwner;
    private MethodInfo? nativeSelectedId;
    private MethodInfo? nativeInteractionId;
    private SpineInteractionUI? nativeListOwner;
    private Dictionary<int, int>? addedNativeEntries;
    private List<int>? originalNativeIds;
    private List<int>? preparedNativeIds;
    private SpineInteractionUI? emptyNativeUi;
    private DatingPreviewPanel? previewPanel;
    private LocalSceneHost? host;
    private LocalSceneHost? pendingHost;
    private ConfigEntry<bool> applyEnabledMods = null!;
    private ConfigEntry<bool> useNativeDatingEntries = null!;
    private string notice = "";
    private float noticeUntil;

    internal bool ApplyEnabledMods => applyEnabledMods.Value;

    private void Awake()
    {
        Instance = this;
        applyEnabledMods = Config.Bind("Scenes", "ApplyEnabledDatingMods", true,
            "Apply the enabled BrownDustX mod to local previews.");
        useNativeDatingEntries = Config.Bind("Scenes", "UseNativeDatingEntries", true,
            "Schema v1 migration. Schema v2 has independent P and native entry settings.");
        ReloadSettings();

        nativePatches = Install("native", patches =>
        {
            rawDataInstance = RequireMethod(typeof(RawDataManager).BaseType!, "get_Instance");
            patches.Patch(SetUiMethod(), prefix: Hook(nameof(PrepareNativeEntries)));
        });
        if (nativePatches != null) nativeRewardPatches = Install("native-reward", InstallNativeRewardHook);
        previewPatches = Install("preview", patches =>
        {
            patches.Patch(SetUiMethod(), prefix: Hook(nameof(PreparePreview)), postfix: Hook(nameof(ShowPreviewPanel)));
            patches.Patch(RequireMethod(typeof(SpineInteractionUI), "OnDisable"), prefix: Hook(nameof(ClosePreviewPanel)));
            patches.Patch(RequireMethod(typeof(SpineInteractionUI), "SetCharSelect", typeof(int), typeof(bool), typeof(bool)),
                prefix: Hook(nameof(ReturnToNativeScene)));
            foreach (var controller in new[] { typeof(SpineInteractionRecordController), typeof(SpineInteractionReplayController), typeof(SpineInteractionHintController) })
            {
                var onClick = AccessTools.Method(controller, "OnClickUI", new[] { typeof(GameObject) });
                if (onClick?.ReturnType == typeof(bool)) patches.Patch(onClick, prefix: Hook(nameof(LocalPreviewClick)));
            }
        });
        if (previewPatches != null) spinePatches = Install("spine", OfficialSpineData.Install);
    }

    private Harmony? Install(string name, Action<Harmony> install)
    {
        var patches = new Harmony(PluginId + "." + name);
        try
        {
            install(patches);
            Logger.LogInfo($"Dating {name} hooks ready.");
            return patches;
        }
        catch (Exception error)
        {
            patches.UnpatchSelf();
            Logger.LogWarning($"Dating {name} hooks unavailable: {error}");
            return null;
        }
    }

    private static MethodInfo SetUiMethod() =>
        RequireMethod(typeof(SpineInteractionUI), "SetUI", typeof(int), typeof(List<int>));

    private static MethodInfo RequireMethod(Type type, string name, params Type[] parameters) =>
        AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.FullName, name);

    private static HarmonyMethod Hook(string name) => new HarmonyMethod(typeof(Plugin), name);

    private void InstallNativeRewardHook(Harmony patches)
    {
        var controller = typeof(SpineInteractionActionController);
        var parameters = new[] { typeof(SpineInteractionPoint) };
        // These two game builds use different names for the point reward helper.
        var claimReward = AccessTools.Method(controller, "ὥὧὭὤὭὯὤὡὭὩὡ", parameters)
            ?? AccessTools.Method(controller, "ὬὣὥὯὠὩὧὦὦὠὡ", parameters);
        if (claimReward?.ReturnType != typeof(void)) throw new MissingMethodException("Spine interaction point reward helper");
        nativeRewardOwner = RequireMethod(controller, "get_RootUI");
        nativeInteractionId = RequireMethod(controller, "get_CurInteractionGroupId");
        nativeSelectedId = RequireMethod(typeof(SpineInteractionUI), "get_CurrentSelectItemId");
        patches.Patch(claimReward, prefix: Hook(nameof(NativeRewardPrefix)));
    }

    private void ReloadSettings()
    {
        try { Settings = LocalInteractionSettings.Read(useNativeDatingEntries.Value); }
        catch (Exception error) { Logger.LogWarning($"Dating settings could not be read: {error.Message}"); }
    }

    private static bool PrepareNativeEntries(SpineInteractionUI __instance, ref int __0, ref List<int> __1)
    {
        var plugin = Instance;
        if (plugin == null) return true;

        plugin.emptyNativeUi = null;
        plugin.ReloadSettings();

        // SetUI can receive the list retained by the last open. Use the caller's
        // original list so our additions never count as owned entries.
        var incoming = __1 != null && ReferenceEquals(__1, plugin.preparedNativeIds) ? plugin.originalNativeIds : __1;
        var visible = plugin.BuildNativeList(incoming, out var added);
        plugin.preparedNativeIds = ReferenceEquals(visible, incoming) ? null : visible;
        plugin.originalNativeIds = plugin.preparedNativeIds == null ? null : incoming;
        plugin.addedNativeEntries = added;
        plugin.nativeListOwner = added is { Count: > 0 } ? __instance : null;
        if (ReferenceEquals(visible, __1)) return true;

        __1 = visible ?? new List<int>();
        plugin.Logger.LogInfo($"Native dating list: {__1.Count} visible, {added?.Count ?? 0} added from settings.");
        if (__1.Count == 0)
        {
            // SetUI selects and loads its first argument even when its list is empty.
            plugin.emptyNativeUi = __instance;
            plugin.StartCoroutine(plugin.CloseEmptyNativeUi(__instance));
            return false;
        }

        if (!__1.Contains(__0)) __0 = __1[0];
        return true;
    }

    private List<int>? BuildNativeList(List<int>? incoming, out Dictionary<int, int>? added)
    {
        added = null;
        var inject = Settings.NativeEntriesEnabled && Settings.EnabledNativeScenes.Count > 0 && nativeRewardPatches != null;
        if (!Settings.Enabled || (!inject && Settings.NativeEntriesEnabled && Settings.HiddenNativeScenes.Count == 0)) return incoming;

        try
        {
            var manager = rawDataInstance?.Invoke(null, null) as RawDataManager;
            var rows = manager?.GetLobbySettingItemTableList();
            if (manager == null || rows == null || rows.Count == 0)
            {
                Logger.LogWarning("Native dating list unchanged because the lobby table is unavailable.");
                return incoming;
            }

            var visible = NativeDatingEntries.Filter((IEnumerable<int>?)incoming ?? Array.Empty<int>(), rows,
                Settings.NativeEntriesEnabled, Settings.HiddenNativeScenes);
            if (inject)
                added = NativeDatingEntries.AppendConfigured(visible, Settings.EnabledNativeScenes, rows,
                    manager.GetSpineInteractionTable, message => Logger.LogWarning(message));
            return visible;
        }
        catch (Exception error)
        {
            Logger.LogWarning($"Native dating list unchanged: {error.GetBaseException().Message}");
            return incoming;
        }
    }

    private static bool NativeRewardPrefix(SpineInteractionActionController __instance)
    {
        var plugin = Instance;
        if (plugin?.addedNativeEntries is not { Count: > 0 } added) return true;

        try
        {
            return plugin.nativeRewardOwner?.Invoke(__instance, null) is not SpineInteractionUI ui ||
                ui != plugin.nativeListOwner || plugin.nativeSelectedId?.Invoke(ui, null) is not int rowId ||
                !added.TryGetValue(rowId, out var interactionId) ||
                plugin.nativeInteractionId?.Invoke(__instance, null) is not int currentInteractionId ||
                currentInteractionId != interactionId;
        }
        catch (Exception error)
        {
            plugin.Logger.LogWarning($"Cannot identify local-only native reward: {error.GetBaseException().Message}");
            return true;
        }
    }

    private IEnumerator CloseEmptyNativeUi(SpineInteractionUI ui)
    {
        yield return null;
        if (ui == null || emptyNativeUi != ui || previewPanel?.Owner == ui) yield break;
        emptyNativeUi = null;
        ui.CloseUI();
        Notify("没有启用的约会入口");
    }

    private static void PreparePreview()
    {
        var plugin = Instance;
        if (plugin == null) return;
        plugin.RemovePreviewPanel();
        if (plugin.nativePatches == null) plugin.ReloadSettings();
    }

    private static void ShowPreviewPanel(SpineInteractionUI __instance)
    {
        var plugin = Instance;
        if (plugin == null || !Settings.Enabled || !Settings.PEntriesEnabled) return;
        try
        {
            plugin.previewPanel = new DatingPreviewPanel(__instance, Settings.Scenes.Values,
                plugin.emptyNativeUi == __instance, plugin.OpenPreview, plugin.CloseLocal);
        }
        catch (Exception error)
        {
            plugin.Logger.LogWarning($"P preview panel unavailable: {error.GetBaseException().Message}");
        }
    }

    private static void ClosePreviewPanel(SpineInteractionUI __instance)
    {
        var plugin = Instance;
        if (plugin?.previewPanel?.Owner == __instance) plugin.RemovePreviewPanel();
        if (plugin?.emptyNativeUi == __instance) plugin.emptyNativeUi = null;
    }

    private static void ReturnToNativeScene(SpineInteractionUI __instance)
    {
        var plugin = Instance;
        if (plugin?.previewPanel?.Owner == __instance) plugin.CloseLocal();
    }

    private static bool LocalPreviewClick(ref bool __result)
    {
        if (Instance?.host?.IsOpen != true) return true;
        __result = false;
        return false;
    }

    private void OpenPreview(SceneSpec spec)
    {
        var ui = previewPanel?.Owner;
        if (ui == null || !ui.isActiveAndEnabled) return;
        var recording = AccessTools.Field(typeof(SpineInteractionUI), "_recordController")?.GetValue(ui) as SpineInteractionRecordController;
        var replaying = AccessTools.Field(typeof(SpineInteractionUI), "_replayController")?.GetValue(ui) as SpineInteractionReplayController;
        var isRecording = recording != null && AccessTools.Method(recording.GetType(), "get_IsRecording")?.Invoke(recording, null) is true;
        var isReplaying = replaying != null && AccessTools.Method(replaying.GetType(), "get_IsReplaying")?.Invoke(replaying, null) is true;
        if (isRecording || isReplaying)
        {
            Notify("请先停止官方录制或回放，再打开本地预览");
            return;
        }

        pendingHost?.Close();
        pendingHost = new LocalSceneHost(this, ui, spec, emptyNativeUi == ui);
        Notify($"正在加载本地预览：{spec.Title}");
        StartCoroutine(pendingHost.Open());
    }

    private void RemovePreviewPanel()
    {
        CloseLocal();
        previewPanel?.Destroy();
        previewPanel = null;
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

    private void LateUpdate()
    {
        host?.HideOtherVisuals();
        previewPanel?.SetPreview(host?.IsOpen == true, pendingHost?.IsActive == true);
    }

    private void OnDestroy()
    {
        RemovePreviewPanel();
        LocalAvatar.ReleaseAssets();
        spinePatches?.UnpatchSelf();
        previewPatches?.UnpatchSelf();
        nativeRewardPatches?.UnpatchSelf();
        nativePatches?.UnpatchSelf();
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
}
