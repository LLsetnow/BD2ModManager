using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using Newtonsoft.Json.Linq;

namespace BD2PreviewInteractions;

internal sealed class LocalInteractionSettings
{
    internal static string FilePath => Path.Combine(Paths.PluginPath, "BD2PreviewInteractions", "settings.json");
    internal bool Enabled { get; private set; } = true;
    internal string ExportDirectory { get; private set; } = @"F:\Neowiz\mods\dating-interaction-configs";
    internal string AudioDirectory { get; private set; } = @"F:\Neowiz\Brown-Dust-2-Asset-master\audio\dating";
    internal string AssetsDirectory { get; private set; } = @"F:\Neowiz\Brown-Dust-2-Asset-master";
    internal float DragHoldSeconds { get; private set; } = 0.35f;
    internal float MarkerOpacity { get; private set; } = 0.32f;
    internal Dictionary<int, SceneSpec> Scenes { get; private set; } = Plugin.DefaultScenes();

    internal static LocalInteractionSettings Read(string fallbackAudio)
    {
        var result = new LocalInteractionSettings { AudioDirectory = fallbackAudio };
        if (!File.Exists(FilePath)) return result;
        var root = JObject.Parse(File.ReadAllText(FilePath));
        if (root.Value<int>("schemaVersion") != 1) throw new InvalidDataException("Unsupported local interaction settings");
        result.Enabled = root.Value<bool?>("enabled") ?? true;
        result.ExportDirectory = Path.GetFullPath(root.Value<string>("exportDirectory") ?? result.ExportDirectory);
        result.AudioDirectory = Path.GetFullPath(root.Value<string>("audioDirectory") ?? result.AudioDirectory);
        result.AssetsDirectory = Path.GetFullPath(root.Value<string>("assetsDirectory") ?? result.AssetsDirectory);
        result.DragHoldSeconds = Math.Max(0.15f, Math.Min(1.5f, root.Value<float?>("dragHoldSeconds") ?? 0.35f));
        result.MarkerOpacity = Math.Max(0.08f, Math.Min(0.6f, root.Value<float?>("markerOpacity") ?? 0.32f));
        var scenes = new Dictionary<int, SceneSpec>();
        foreach (var entry in (root["scenes"] as JArray ?? new JArray()).OfType<JObject>())
        {
            if (entry.Value<bool?>("enabled") == false) continue;
            var sceneId = entry.Value<string>("sceneId") ?? "";
            if (!sceneId.StartsWith("illust_dating", StringComparison.Ordinal) ||
                !int.TryParse(sceneId.Substring("illust_dating".Length), out var number) || number < 1 || number > 9999)
                throw new InvalidDataException("Invalid local scene ID");
            var costume = entry.Value<int>("costumeId");
            var avatar = entry.Value<string>("avatarAddress") ?? "";
            if (costume < 1 || avatar.Contains("..") || !avatar.StartsWith("IllustInventory_1/", StringComparison.Ordinal))
                throw new InvalidDataException("Invalid local scene portrait");
            var id = 1_900_000_000 + number;
            scenes.Add(id, new SceneSpec(id, sceneId, costume, "", avatar, entry.Value<string>("title") ?? sceneId,
                entry.Value<bool?>("useMods") ?? true));
        }
        result.Scenes = scenes;
        return result;
    }
}
