using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Newtonsoft.Json.Linq;

namespace BD2PreviewInteractions;

internal sealed class LocalInteractionSettings
{
    private static string SharedAssetLibraryRootPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LLsetnow", "BD2AssetLibrary", "root.txt");

    internal static string FilePath => Path.Combine(Paths.PluginPath, "BD2PreviewInteractions", "settings.json");
    internal bool Enabled { get; private set; } = true;
    internal bool PEntriesEnabled { get; private set; } = true;
    internal bool NativeEntriesEnabled { get; private set; } = true;
    internal string AssetsDirectory { get; private set; } = ReadSharedAssetLibraryRoot() ?? "";
    internal string ExportDirectory => Path.Combine(AssetsDirectory, "audio", "dating");
    internal string AudioDirectory => Path.Combine(AssetsDirectory, "audio", "dating");
    internal float DragHoldSeconds { get; private set; } = 0.35f;
    internal float MarkerOpacity { get; private set; } = 0.32f;
    internal Dictionary<int, SceneSpec> Scenes { get; private set; } = new();
    internal List<string> EnabledNativeScenes { get; } = new();
    internal HashSet<string> HiddenNativeScenes { get; } = new(StringComparer.OrdinalIgnoreCase);

    internal static LocalInteractionSettings Read(bool legacyNative = true)
    {
        var result = new LocalInteractionSettings();
        if (File.Exists(FilePath)) return Parse(File.ReadAllText(FilePath), legacyNative);
        if (!legacyNative) result.Scenes = DatingSceneDefaults.Create(result.AssetsDirectory);
        return result;
    }

    private static string? ReadSharedAssetLibraryRoot()
    {
        try
        {
            var path = SharedAssetLibraryRootPath;
            var root = File.Exists(path) ? File.ReadAllText(path).Trim() : "";
            return !string.IsNullOrWhiteSpace(root) && Path.IsPathRooted(root) ? Path.GetFullPath(root) : null;
        }
        catch { return null; }
    }

    internal static LocalInteractionSettings Parse(string json, bool legacyNative = true)
    {
        var result = new LocalInteractionSettings();
        var root = JObject.Parse(json);
        var version = root.Value<int>("schemaVersion");
        if (version != 1 && version != 2) throw new InvalidDataException("Unsupported dating interaction settings");
        result.Enabled = root.Value<bool?>("enabled") ?? true;
        result.PEntriesEnabled = root.Value<bool?>("pEntriesEnabled") ?? true;
        result.NativeEntriesEnabled = root.Value<bool?>("nativeEntriesEnabled") ?? true;
        var configuredRoot = ReadSharedAssetLibraryRoot() ?? root.Value<string>("assetsDirectory") ?? result.AssetsDirectory;
        result.AssetsDirectory = Path.IsPathRooted(configuredRoot) ? Path.GetFullPath(configuredRoot) : "";
        result.DragHoldSeconds = Math.Max(0.15f, Math.Min(1.5f, root.Value<float?>("dragHoldSeconds") ?? 0.35f));
        result.MarkerOpacity = Math.Max(0.08f, Math.Min(0.6f, root.Value<float?>("markerOpacity") ?? 0.32f));
        var scenes = new Dictionary<int, SceneSpec>();
        var legacyNativeDefaults = DatingSceneDefaults.NativeDefaultNumbers(result.AssetsDirectory);
        foreach (var entry in (root["scenes"] as JArray ?? new JArray()).OfType<JObject>())
        {
            var sceneId = entry.Value<string>("sceneId") ?? "";
            if (!sceneId.StartsWith("illust_dating", StringComparison.Ordinal) ||
                !int.TryParse(sceneId.Substring("illust_dating".Length), out var number) || number < 1 || number > 9999)
                throw new InvalidDataException("Invalid local scene ID");
            var costume = entry.Value<int>("costumeId");
            var avatar = entry.Value<string>("avatarAddress") ?? "";
            if (costume < 1 || avatar.Contains("..") || !avatar.StartsWith("IllustInventory_1/", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid local scene portrait");
            var id = 1_900_000_000 + number;
            // The per-scene asset selector was removed; old useMods=false must
            // not override the enabled BrownDustX mods.
            var spec = new SceneSpec(id, sceneId, costume, "", avatar, entry.Value<string>("title") ?? sceneId);
            var p = entry.Value<bool?>("enabled") ?? version == 1;
            var useLegacyNative = version == 1 && legacyNative && legacyNativeDefaults.Contains(number);
            var native = version == 2
                ? entry.Value<bool?>("nativeEnabled") == true
                : useLegacyNative && p;
            if (useLegacyNative) p = false;
            if (p) scenes.Add(id, spec);
            if (native) result.EnabledNativeScenes.Add(sceneId);
            else if (version == 2) result.HiddenNativeScenes.Add(sceneId);
        }
        result.Scenes = scenes;
        return result;
    }
}
