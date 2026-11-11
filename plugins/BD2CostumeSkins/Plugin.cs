using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Proto.Design.common;
using Proto.Net;
using UnityEngine;

namespace BD2CostumeSkins;

[BepInPlugin("local.bd2.costume-skins", "BD2 Costume Skins", "0.1.1")]
public sealed class Plugin : BaseUnityPlugin
{
    private static Plugin? instance;
    private readonly Harmony harmony = new Harmony("local.bd2.costume-skins");
    private SkinSettings settings = new SkinSettings();
    private readonly HashSet<int> validDesigns = new HashSet<int>();
    private MethodInfo designLookup = null!;
    private static readonly FieldInfo SkinButton = AccessTools.Field(typeof(CostumeListLoopScrollItem), "_btnSkinCange");
    private static readonly FieldInfo CostumeData = typeof(CostumeListLoopScrollItem).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
        .Single(f => f.FieldType == typeof(CostumeDBInfo));
    private static readonly FieldInfo CharacterData = typeof(CharUI).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
        .Single(f => f.FieldType == typeof(CharDBInfo));
    private static readonly JsonSerializerSettings JsonOptions = new JsonSerializerSettings {
        ContractResolver = new CamelCasePropertyNamesContractResolver(), Formatting = Formatting.Indented
    };
    private string settingsPath = "";
    private string lastContents = "";
    private float nextPoll;
    private bool refreshPending;
    private float noticeUntil;
    private string notice = "";
    private bool hooksReady;

    private void Awake()
    {
        instance = this;
        settingsPath = Path.Combine(Paths.PluginPath, "BD2CostumeSkins", "settings.json");
        try {
            var candidates = typeof(CostumeDBInfo).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(m => m.ReturnType == typeof(CostumeDesignTable) && m.GetParameters().Length == 1).ToArray();
            var dbLookup = candidates.Single(m => m.GetParameters()[0].ParameterType == typeof(CostumeDBInfo));
            designLookup = candidates.Single(m => m.DeclaringType == dbLookup.DeclaringType && m.Name == dbLookup.Name
                && m.GetParameters()[0].ParameterType == typeof(int));
            if (SkinButton == null) throw new MissingFieldException("CostumeListLoopScrollItem._btnSkinCange");
            Reload();
            harmony.Patch(AccessTools.Method(typeof(CostumeDBInfo), "get_CostumeDesignId"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(DesignIdPostfix)));
            harmony.Patch(AccessTools.Method(typeof(CostumeListLoopScrollItem), "SetActiveSelected"),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(SelectedPostfix)));
            harmony.Patch(AccessTools.Method(typeof(CostumeListLoopScrollItem), "OnClickItem"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(ClickPrefix)));
            // This one viewer callback reads the serialized Id/DesignId instead of CostumeDesignId.
            var viewer = typeof(CharCostumeUI).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Single(m => m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(CutsceneViewerUI));
            harmony.Patch(viewer, transpiler: new HarmonyMethod(typeof(Plugin), nameof(ViewerTranspiler)));
            hooksReady = true;
            Logger.LogInfo("Skin hooks ready: native Skin button, illustration/background, SD model and skill viewer. Version 0.1.1");
        } catch (Exception ex) {
            harmony.UnpatchSelf(); settings = new SkinSettings();
            Logger.LogError("Skin plugin disabled; native character UI preserved: " + ex);
        }
    }

    private void OnDestroy() { harmony.UnpatchSelf(); if (instance == this) instance = null; }

    private bool DesignExists(int id)
    {
        if (validDesigns.Contains(id)) return true;
        var row = designLookup.Invoke(null, new object[] { id }) as CostumeDesignTable;
        if (row == null || row.Id != id || string.IsNullOrEmpty(row.IllustName)) return false;
        validDesigns.Add(id);
        Logger.LogInfo($"Resolved design {id:D6}: illust={row.IllustName}, cutscene={row.SkillTimelineName}, SD={row.PrefabName}");
        return true;
    }

    private static void DesignIdPostfix(CostumeDBInfo __instance, ref int __result)
    {
        var plugin = instance;
        if (plugin == null) return;
        var chosen = plugin.settings.Resolve(__instance.Id, __result);
        if (chosen == __result) return;
        try { if (plugin.DesignExists(chosen)) __result = chosen; }
        catch { /* Tables may not be loaded during login. Preserve the native design. */ }
    }

    private static void SelectedPostfix(CostumeListLoopScrollItem __instance, bool __0)
    {
        var plugin = instance;
        var db = CostumeData.GetValue(__instance) as CostumeDBInfo;
        if (plugin == null || db == null || !plugin.settings.HasSource(db.Id)) return;
        var button = SkinButton.GetValue(__instance) as GameObject;
        if (button != null) button.SetActive(__0);
    }

    private static bool ClickPrefix(CostumeListLoopScrollItem __instance, GameObject __0)
    {
        var plugin = instance;
        var db = CostumeData.GetValue(__instance) as CostumeDBInfo;
        if (plugin == null || db == null || !plugin.settings.HasSource(db.Id)) return true;
        var button = SkinButton.GetValue(__instance) as GameObject;
        if (button == null || __0 == null || (__0 != button && !__0.transform.IsChildOf(button.transform))) return true;
        try {
            // Reload before writing, so edits saved by ModManager cannot be overwritten by an old snapshot.
            plugin.Reload();
            if (!plugin.settings.HasSource(db.Id)) return false;
            var nativeDesign = db.DesignId != 0 ? db.DesignId : db.Id;
            var next = plugin.settings.Next(db.Id, plugin.settings.Resolve(db.Id, nativeDesign));
            if (!plugin.DesignExists(int.Parse(next))) throw new InvalidOperationException("游戏中找不到目标皮肤资源，请更新游戏或检查映射");
            plugin.settings.SelectedSkins[db.Id.ToString("D6")] = next;
            plugin.Save();
            plugin.refreshPending = true;
            plugin.Logger.LogInfo($"Local skin {db.Id:D6} -> {next}");
        } catch (Exception ex) { plugin.ShowError(ex); }
        return false; // Local selection never invokes the native skin-set request.
    }

    internal static IEnumerable<CodeInstruction> ViewerTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var id = AccessTools.Method(typeof(CostumeDBInfo), "get_Id");
        var design = AccessTools.Method(typeof(CostumeDBInfo), "get_DesignId");
        var appearance = AccessTools.Method(typeof(CostumeDBInfo), "get_CostumeDesignId");
        var count = 0;
        foreach (var code in instructions) {
            if (code.Calls(id) || code.Calls(design)) { code.operand = appearance; count++; }
            yield return code;
        }
        if (count != 2) throw new InvalidOperationException("Skill viewer code changed; skin hook is incompatible");
    }

    private void Reload()
    {
        if (!File.Exists(settingsPath)) { settings = SkinSettings.Defaults(); Save(); return; }
        var json = File.ReadAllText(settingsPath);
        if (json == lastContents) return;
        var loaded = JsonConvert.DeserializeObject<SkinSettings>(json) ?? throw new InvalidDataException("皮肤配置为空");
        loaded.Validate();
        settings = loaded; lastContents = json; validDesigns.Clear(); refreshPending = true;
        Logger.LogInfo($"Loaded {settings.Mappings.Count} local skin mapping(s)");
    }

    private void Save()
    {
        settings.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var json = JsonConvert.SerializeObject(settings, JsonOptions);
        var temp = settingsPath + ".plugin.tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(settingsPath)) File.Replace(temp, settingsPath, null); else File.Move(temp, settingsPath);
        lastContents = json;
    }

    private void Update()
    {
        if (!hooksReady) return;
        if (Time.unscaledTime >= nextPoll) {
            nextPoll = Time.unscaledTime + 1;
            try { Reload(); } catch (Exception ex) { if (noticeUntil < Time.unscaledTime) ShowError(ex); }
        }
        if (!refreshPending) return;
        refreshPending = false;
        foreach (var ui in FindObjectsOfType<CharUI>()) {
            if (!ui.isActiveAndEnabled || CharacterData.GetValue(ui) == null) continue;
            try { ui.RefreshCostumeUI(); ui.SetCharIllust(null); }
            catch (Exception ex) { ShowError(ex); }
        }
    }

    private void ShowError(Exception ex)
    {
        notice = "Skin: " + ex.GetBaseException().Message;
        noticeUntil = Time.unscaledTime + 6;
        Logger.LogWarning(notice);
    }

    private void OnGUI()
    {
        if (Time.unscaledTime < noticeUntil) GUI.Box(new Rect(20, 20, Mathf.Min(680, Screen.width - 40), 65), notice);
    }
}
