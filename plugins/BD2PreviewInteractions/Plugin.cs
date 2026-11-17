using System;
using System.Collections;
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

[BepInPlugin("local.bd2.previewinteractions", "BD2 Preview Interactions", "0.4.2")]
[BepInDependency("lynesth.bd2.browndustx", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    internal const int Local18 = 1_900_000_018;
    internal static Dictionary<int, SceneSpec> Specs { get; private set; } = DefaultScenes();
    internal static LocalInteractionSettings Settings { get; private set; } = new();
    internal static Dictionary<int, SceneSpec> DefaultScenes() => DatingSceneDefaults.Create();

    internal static Plugin? Instance { get; private set; }
    internal static string ExportRoot => Settings.ExportDirectory;

    private readonly Dictionary<int, LobbySettingItemTable> localRows = new();
    private readonly HashSet<int> localRewardNotices = new();
    private readonly NativeEntryProbe nativeProbe = new();
    private readonly NativeDatingEntries nativeDating = new();
    private List<LobbySettingItemTable>? officialDatingRows;
    private readonly HashSet<string> loggedOfficialLists = new(StringComparer.Ordinal);
    private Harmony? harmony;
    private MethodInfo? rowGetter;
    private LocalSceneHost? host;
    private LocalSceneHost? pendingHost;
    private string notice = "";
    private float noticeUntil;
    private ConfigEntry<bool> applyEnabledMods = null!;
    private ConfigEntry<int> controlsTop = null!;
    private ConfigEntry<bool> nativeProbeEnabled = null!;
    private ConfigEntry<int> nativeProbeSourceId = null!;
    private ConfigEntry<bool> useNativeDatingEntries = null!;
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
        nativeProbeEnabled = Config.Bind("Diagnostics", "NativeEntryProbeEnabled", false,
            "Duplicate one displayed official row under a temporary ID and use native UI/interaction code. Re-enter dating UI after changing.");
        nativeProbeSourceId = Config.Bind("Diagnostics", "NativeEntryProbeSourceId", 32,
            "Official row ID to duplicate for the native entry probe. Default 32 is Illust_dating1.");
        useNativeDatingEntries = Config.Bind("Scenes", "UseNativeDatingEntries", true,
            "Legacy schema-v1 migration only. Schema v2 uses independent P/native switches from settings.json.");
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
            harmony.Patch(AccessTools.Method(typeof(SpineInteractionActionController), "SetSpineInteraction",
                    new[] { typeof(int), typeof(SpineAnimationObject), typeof(SpineInteractionGroup), typeof(bool) }),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(NativeControllerPostfix)));
            // This helper only checks/claims a point reward. Pose changes run in
            // its callers and do not depend on a reward response.
            var claimReward = AccessTools.Method(typeof(SpineInteractionActionController), "ὬὣὥὯὠὩὧὦὦὠὡ",
                new[] { typeof(SpineInteractionPoint) });
            if (claimReward == null || claimReward.ReturnType != typeof(void))
                throw new MissingMethodException("Spine interaction point reward helper");
            harmony.Patch(claimReward, prefix: new HarmonyMethod(typeof(Plugin), nameof(NativeRewardPrefix)));
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
            Logger.LogInfo($"Dating entry modes: P={Settings.PEntriesEnabled}, native={Settings.NativeEntriesEnabled}; independently configured in settings.json");
            if (nativeProbeEnabled.Value)
                Logger.LogInfo($"Native entry probe enabled: sourceId={nativeProbeSourceId.Value}, probeId={NativeEntryProbe.EntryId}");
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
            Settings = LocalInteractionSettings.Read(useNativeDatingEntries.Value);
            Specs = Settings.Enabled && Settings.PEntriesEnabled ? Settings.Scenes : new Dictionary<int, SceneSpec>();
            localRows.Clear();
        }
        catch (Exception error) { Logger.LogError($"Local interaction settings could not be loaded: {error.Message}"); }
    }

    private LobbySettingItemTable? GetOriginalRow(int id)
    {
        try { return rowGetter?.Invoke(null, new object[] { id }) as LobbySettingItemTable; }
        catch { return null; }
    }

    private void LogOfficialList(List<int> ids)
    {
        var signature = string.Join(",", ids);
        if (!loggedOfficialLists.Add(signature)) return;

        Logger.LogInfo($"Official dating list captured ({ids.Count} entries), row IDs: [{signature}]");
        for (var index = 0; index < ids.Count; index++)
        {
            var id = ids[index];
            var row = GetOriginalRow(id);
            if (row == null)
            {
                Logger.LogInfo($"Official dating list entry {index}: id={id}, lookup=missing");
                continue;
            }
            Logger.LogInfo($"Official dating list entry {index}: id={id}, rowId={row.Id}, costumeId={row.CostumeId}, resource={row.InteractionResourceName}");
        }
    }

    private void PrepareRows(List<int> existingIds)
    {
        var originals = officialDatingRows?.Where(row => row.Type == 2 && row.InteractionId > 0 && !string.IsNullOrEmpty(row.InteractionResourceName)).ToList()
            ?? existingIds.Select(GetOriginalRow).Where(row => row != null).Select(row => row!).ToList();
        foreach (var spec in Specs.Values.ToArray())
        {
            if (existingIds.Contains(spec.LocalId) && !localRows.ContainsKey(spec.LocalId))
            {
                Logger.LogError($"Local avatar ID conflicts with game data: {spec.LocalId}");
                localRows.Remove(spec.LocalId);
                Specs.Remove(spec.LocalId);
                continue;
            }
            var source = originals.FirstOrDefault(row => string.Equals(row.InteractionResourceName, spec.SceneId, StringComparison.OrdinalIgnoreCase))
                ?? originals.FirstOrDefault(row => row.CostumeId == spec.CostumeId) ?? originals.FirstOrDefault();
            if (source == null) continue;
            var row = source.Clone();
            row.Id = spec.LocalId;
            row.CostumeId = spec.CostumeId;
            row.InteractionResourceName = "Illust_dating" + spec.SceneId.Substring("illust_dating".Length);
            // Preserve the game's valid row metadata. Local cells draw their own portrait.
            localRows[spec.LocalId] = row;
        }
    }

    private static bool SetUiPrefix(SpineInteractionUI __instance, ref int __0, ref List<int> __1)
    {
        var plugin = Instance;
        if (plugin == null || __1 == null) return true;
        plugin.CloseLocal();
        plugin.localRewardNotices.Clear();
        var previousCostume = Specs.TryGetValue(__0, out var selected) ? selected.CostumeId : 0;
        var previousResource = selected == null ? "" : "Illust_dating" + selected.SceneId.Substring("illust_dating".Length);
        if (plugin.nativeDating.Entries.TryGetValue(__0, out var previousNative))
            previousCostume = previousNative.Row.CostumeId;
        var previouslyInjected = new HashSet<int>(plugin.localRows.Keys);
        previouslyInjected.UnionWith(plugin.nativeDating.AddedIds);
        var previousProbeSource = plugin.nativeProbe.SourceId;
        if (plugin.nativeProbe.Row != null) previouslyInjected.Add(NativeEntryProbe.EntryId);
        __1 = plugin.nativeDating.CopyOfficialIds(__1)
            .Where(id => !plugin.localRows.ContainsKey(id) && id != NativeEntryProbe.EntryId).ToList();
        var officialInput = new List<int>(__1);
        plugin.LogOfficialList(__1);
        plugin.ReloadCatalog();
        plugin.PrepareNativeDating(__1);
        plugin.PrepareRows(__1);
        plugin.PrepareNativeProbe(__1);
        foreach (var spec in Specs.Values)
        {
            if (!plugin.localRows.ContainsKey(spec.LocalId) || __1.Contains(spec.LocalId)) continue;
            var originalIndex = __1.FindIndex(id => string.Equals(plugin.GetOriginalRow(id)?.InteractionResourceName, spec.SceneId, StringComparison.OrdinalIgnoreCase));
            if (originalIndex < 0) originalIndex = __1.FindIndex(id => plugin.GetOriginalRow(id)?.CostumeId == spec.CostumeId);
            __1.Insert(originalIndex >= 0 ? originalIndex + 1 : __1.Count, spec.LocalId);
        }
        // A local selection can be remembered by the native UI. When it is removed,
        // choose its original character (or another official entry) before SetUI runs.
        if (!__1.Contains(__0)) {
            var probeSource = previousProbeSource != 0 ? previousProbeSource : plugin.nativeProbeSourceId.Value;
            var original = __0 == NativeEntryProbe.EntryId && __1.Contains(probeSource) ? probeSource : 0;
            if (original == 0 && previousResource.Length != 0)
                original = __1.FirstOrDefault(id => string.Equals(plugin.GetOriginalRow(id)?.InteractionResourceName, previousResource, StringComparison.OrdinalIgnoreCase));
            if (original == 0)
                original = __1.FirstOrDefault(id => previousCostume != 0 && plugin.GetOriginalRow(id)?.CostumeId == previousCostume);
            __0 = original != 0 ? original : __1.FirstOrDefault();
        }
        plugin.nativeDating.RememberList(__1, officialInput);
        if (__1.Count == 0)
        {
            plugin.Notify("没有启用的约会入口，请在 Manager 的约会互动中开启 P 或官方原生入口");
            plugin.StartCoroutine(plugin.CloseEmptyDatingUi(__instance));
            return false;
        }
        return true;
    }

    private IEnumerator CloseEmptyDatingUi(SpineInteractionUI ui)
    {
        yield return null;
        if (ui != null) ui.CloseUI();
    }

    private void PrepareNativeDating(List<int> ids)
    {
        nativeDating.Clear();
        officialDatingRows = null;
        if (!Settings.Enabled) return;
        try
        {
            var manager = AccessTools.Method(typeof(RawDataManager).BaseType, "get_Instance")?.Invoke(null, null) as RawDataManager;
            var rows = manager?.GetLobbySettingItemTableList();
            if (rows == null || manager == null) throw new InvalidOperationException("Official lobby table is not loaded");
            officialDatingRows = rows;
            IEnumerable<string> nativeScenes = Settings.NativeScenes.Values.Select(spec => spec.SceneId);
            if (Settings.UseAllNativeDefaults)
                nativeScenes = rows.Where(row => row.Type == 2 && row.InteractionId > 0).Select(row => row.InteractionResourceName.ToLowerInvariant());
            else if (Settings.SchemaVersion == 1)
                nativeScenes = nativeScenes.Concat(rows.Where(row => ids.Contains(row.Id)
                    && row.InteractionResourceName.StartsWith("Illust_dating", StringComparison.OrdinalIgnoreCase))
                    .Select(row => row.InteractionResourceName.ToLowerInvariant()));
            if (!Settings.NativeEntriesEnabled) nativeScenes = Array.Empty<string>();
            foreach (var failure in nativeDating.Rebuild(ids, nativeScenes, rows, id => manager.GetSpineInteractionTable(id), hideDisabled: true))
                Logger.LogWarning($"Native dating entry skipped: {failure}");
            foreach (var entry in nativeDating.Entries.Values)
            {
                Logger.LogInfo($"Native dating entry {(nativeDating.AddedIds.Contains(entry.Row.Id) ? "added" : "already present")}: sceneId={entry.SceneId}, rowId={entry.Row.Id}, interactionId={entry.Row.InteractionId}, costumeId={entry.Row.CostumeId}, unlock={entry.Interaction.UnlockDatinggroupId}/{entry.Interaction.UnlockDatingId}");
            }
            Logger.LogInfo($"Native dating list ready: added={nativeDating.AddedIds.Count}, hidden={nativeDating.HiddenIds.Count}, active={nativeDating.Entries.Count}, nativeEntries={ids.Count}, P={Specs.Count}");
        }
        catch (Exception error) { Logger.LogWarning($"Native dating entries unavailable: {error.GetBaseException().Message}"); }
    }

    private void PrepareNativeProbe(List<int> ids)
    {
        nativeProbe.Clear();
        if (!Settings.Enabled || !nativeProbeEnabled.Value) return;
        try
        {
            // This build keeps get_* method names but obfuscates property names.
            var manager = AccessTools.Method(typeof(RawDataManager).BaseType, "get_Instance")?.Invoke(null, null) as RawDataManager;
            var rows = manager?.GetLobbySettingItemTableList();
            if (rows == null) throw new InvalidOperationException("Official lobby table is not loaded");
            if (!nativeProbe.Prepare(nativeProbeSourceId.Value, ids, rows, Specs.Keys.ToArray(), out var reason))
            {
                Logger.LogWarning($"Native entry probe skipped: {reason}");
                return;
            }
            var row = nativeProbe.Row!;
            ids.Insert(ids.IndexOf(nativeProbe.SourceId) + 1, NativeEntryProbe.EntryId);
            Logger.LogInfo($"Native entry probe injected: sourceId={nativeProbe.SourceId}, probeId={row.Id}, costumeId={row.CostumeId}, interactionId={row.InteractionId}, resource={row.InteractionResourceName}; native cell and selection enabled");
            Logger.LogInfo($"Native entry probe source row: {rows.First(source => source.Id == nativeProbe.SourceId)}");
        }
        catch (Exception error)
        {
            // A diagnostic failure must not prevent the normal dating UI from opening.
            ids.Remove(NativeEntryProbe.EntryId);
            nativeProbe.Clear();
            Logger.LogWarning($"Native entry probe skipped: {error.GetBaseException().Message}");
        }
    }

    private static bool RowPrefix(int __0, ref LobbySettingItemTable __result)
    {
        var plugin = Instance;
        if (plugin?.nativeProbe.Row is { } probe && __0 == NativeEntryProbe.EntryId)
        {
            __result = probe;
            return false;
        }
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
            if (__0 == NativeEntryProbe.EntryId && plugin.nativeProbe.Row != null)
                plugin.Logger.LogInfo($"Native entry probe selected: probeId={__0}, sourceId={plugin.nativeProbe.SourceId}; passing through original SetCharSelect");
            if (plugin.nativeDating.Entries.TryGetValue(__0, out var entry))
                plugin.Logger.LogInfo($"Native dating entry selected: sceneId={entry.SceneId}, rowId={__0}, interactionId={entry.Row.InteractionId}; passing through original SetCharSelect");
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

    private static bool NativeRewardPrefix(SpineInteractionActionController __instance)
    {
        var plugin = Instance;
        if (plugin == null || !Settings.Enabled) return true;
        try
        {
            if (AccessTools.Method(typeof(SpineInteractionActionController), "get_CurInteractionGroupId")
                    ?.Invoke(__instance, null) is not int interactionId ||
                !plugin.nativeDating.IsLocalOnlyInteraction(interactionId)) return true;
            if (plugin.localRewardNotices.Add(interactionId))
                plugin.Logger.LogInfo($"Local-only native dating: skipped point reward request for unowned interactionId={interactionId}; native pose/input continue, no reward or account change");
            return false;
        }
        catch (Exception error)
        {
            plugin.Logger.LogWarning($"Cannot identify local-only native reward: {error.GetBaseException().Message}");
            return true;
        }
    }

    private static void NativeControllerPostfix(SpineInteractionActionController __instance, int __0)
    {
        var plugin = Instance;
        if (plugin == null || (plugin.nativeProbe.Row == null && plugin.nativeDating.Entries.Count == 0)) return;
        try
        {
            var ui = AccessTools.Method(typeof(SpineInteractionActionController), "get_RootUI")?.Invoke(__instance, null) as SpineInteractionUI;
            if (ui == null || AccessTools.Method(typeof(SpineInteractionUI), "get_CurrentSelectItemId")?.Invoke(ui, null) is not int selected) return;
            var isProbe = selected == NativeEntryProbe.EntryId && plugin.nativeProbe.Row != null;
            var isNative = plugin.nativeDating.Entries.TryGetValue(selected, out var entry);
            if (!isProbe && !isNative) return;
            var points = AccessTools.Method(typeof(SpineInteractionActionController), "get_InteractionPoints")?.Invoke(__instance, null)
                as Dictionary<int, List<SpineInteractionPoint>>;
            var groups = AccessTools.Method(typeof(SpineInteractionActionController), "get_DefaultSpineMotionByGroup")?.Invoke(__instance, null)
                as List<SpineInteractionGroupData>;
            var currentGroup = AccessTools.Method(typeof(SpineInteractionActionController), "get_CurInteractionPointGroupId")?.Invoke(__instance, null);
            var label = isProbe ? "Native entry probe" : $"Native dating entry {entry!.SceneId} rowId={selected}";
            plugin.Logger.LogInfo($"{label} controller initialized: interactionId={__0}, groups={groups?.Count ?? 0}, points={points?.Values.Sum(group => group?.Count ?? 0) ?? 0}, currentGroup={currentGroup}; original SetSpineInteraction returned");
        }
        catch (Exception error) { plugin.Logger.LogWarning($"Native entry probe diagnostic failed: {error.Message}"); }
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
