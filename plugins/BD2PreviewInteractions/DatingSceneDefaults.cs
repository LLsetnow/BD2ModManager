using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BD2PreviewInteractions;

internal static class DatingSceneDefaults
{
    internal static HashSet<int> NativeDefaultNumbers(string assetsDirectory)
    {
        try
        {
            var values = ReadCatalog(assetsDirectory)["dating"]?["legacyNativeSceneIds"] as JArray ?? new JArray();
            return values.Select(value => value.Value<int>()).ToHashSet();
        }
        catch { return new HashSet<int>(); }
    }

    internal static Dictionary<int, SceneSpec> Create(string assetsDirectory)
    {
        var result = new Dictionary<int, SceneSpec>();
        try
        {
            var scenes = ReadCatalog(assetsDirectory)["dating"]?["fallbackScenes"] as JArray ?? new JArray();
            foreach (var value in scenes.OfType<JObject>())
            {
                var localId = value.Value<int?>("localId") ?? 0;
                var sceneId = value.Value<string>("sceneId") ?? "";
                var costumeId = value.Value<int?>("costumeId") ?? 0;
                var avatar = value.Value<string>("avatarAddress") ?? "";
                if (localId <= 0 || costumeId <= 0 || !sceneId.StartsWith("illust_dating", StringComparison.Ordinal) ||
                    !avatar.StartsWith("IllustInventory_1/", StringComparison.Ordinal) || avatar.Contains("..")) continue;
                result[localId] = new SceneSpec(localId, sceneId, costumeId,
                    value.Value<string>("iconName") ?? "", avatar,
                    value.Value<string>("title") ?? sceneId);
            }
        }
        catch { }
        return result;
    }

    private static JObject ReadCatalog(string assetsDirectory)
    {
        if (Path.IsPathRooted(assetsDirectory))
        {
            var path = Path.Combine(assetsDirectory, "modmanager.json");
            if (File.Exists(path))
            {
                try
                {
                    var catalog = JObject.Parse(File.ReadAllText(path));
                    if (catalog.Value<int>("schemaVersion") == 1 && catalog["dating"] is JObject) return catalog;
                }
                catch (IOException) { }
                catch (Newtonsoft.Json.JsonException) { }
            }
        }
        using var stream = typeof(DatingSceneDefaults).Assembly.GetManifestResourceStream("modmanager.json")
            ?? throw new FileNotFoundException("No dating catalog is available");
        using var reader = new StreamReader(stream);
        return JObject.Parse(reader.ReadToEnd());
    }
}

internal sealed class SceneSpec
{
    internal SceneSpec(int localId, string sceneId, int costumeId, string iconName, string avatarAddress, string title, bool useMods = true)
    {
        LocalId = localId; SceneId = sceneId; CostumeId = costumeId; IconName = iconName;
        AvatarAddress = avatarAddress; Title = title; UseMods = useMods;
    }
    internal int LocalId { get; }
    internal string SceneId { get; }
    internal int CostumeId { get; }
    internal string IconName { get; }
    internal string AvatarAddress { get; }
    internal string Title { get; }
    internal bool UseMods { get; }
}
