using BD2PreviewInteractions;
using Newtonsoft.Json.Linq;

var checks = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
JObject Entry(int n, bool p, bool native) => new() {
    ["sceneId"] = $"illust_dating{n}", ["costumeId"] = 296,
    ["avatarAddress"] = "IllustInventory_1/example.png", ["title"] = $"Dating {n}",
    ["enabled"] = p, ["nativeEnabled"] = native, ["useMods"] = false,
};
var json = new JObject {
    ["schemaVersion"] = 2, ["enabled"] = true, ["pEntriesEnabled"] = true, ["nativeEntriesEnabled"] = true,
    ["scenes"] = new JArray(Entry(7,true,true), Entry(9,false,false), Entry(12,true,false), Entry(1,false,true)),
};
var loaded = LocalInteractionSettings.Parse(json.ToString(), @"F:\audio");
Check(loaded.Scenes.Count == 2 && loaded.NativeScenes.Count == 2, "Both modes must read independent per-scene switches");
Check(loaded.Scenes.ContainsKey(1900000007) && loaded.NativeScenes.ContainsKey(1900000007), "The same scene can enable both P and native objects");
Check(!loaded.Scenes.ContainsKey(1900000009) && !loaded.NativeScenes.ContainsKey(1900000009), "Both switches can be off for a scene");
Check(loaded.Scenes[1900000012].UseMods, "Legacy original-asset selections must now use enabled mods without changing entry switches");
json["pEntriesEnabled"] = false; json["nativeEntriesEnabled"] = false; json["enabled"] = false;
loaded = LocalInteractionSettings.Parse(json.ToString(), @"F:\audio");
Check(!loaded.Enabled && !loaded.PEntriesEnabled && !loaded.NativeEntriesEnabled && loaded.Scenes.Count == 2 && loaded.NativeScenes.Count == 2, "Global switches must retain per-scene preferences");
json["schemaVersion"] = 1;
loaded = LocalInteractionSettings.Parse(json.ToString(), @"F:\audio", true);
Check(loaded.Scenes.Count == 0 && loaded.NativeScenes.Count == 3, "Legacy native-six settings must not also enable their P duplicates");
loaded = LocalInteractionSettings.Parse(json.ToString(), @"F:\audio", false);
Check(loaded.Scenes.Count == 2 && loaded.NativeScenes.Count == 1, "Legacy P-only settings must preserve their P selections and normal native entries");
json["schemaVersion"] = 3;
try { LocalInteractionSettings.Parse(json.ToString(), @"F:\audio"); throw new Exception("Invalid schema accepted"); }
catch (InvalidDataException) { checks++; }
Console.WriteLine($"Passed {checks} independent dating settings and migration checks");
