using BD2PreviewInteractions;
using Google.Protobuf;
using Proto.Design.common;
using System.Reflection;

var checks = 0;
void Check(bool result, string message)
{
    checks++;
    if (!result) throw new Exception(message);
}

// Deliberately not inferred from the dating number. Exercise the real game
// protobuf type, its mutable repeated fields, and a future/unknown field.
var source = new LobbySettingItemTable
{
    Id = 32, CostumeId = 3303, InteractionId = 4321, InteractionResourceName = "Illust_dating1",
    ItemNameTextId = 123, PackId = 12, PrestigeSkinId = 45, ProvideType = 2, Type = 9,
};
source.IconSpriteName.Add(new[] { "official_thumb", "official_thumb_variant" });
source.ItemPath.Add("official_item_path");
source.WallPaperBGLoadType.Add(3);
source.WallpaperBGFullPath.Add("official_background_full");
source.WallpaperPath.Add("official_background");
source = LobbySettingItemTable.Parser.ParseFrom(source.ToByteArray().Concat(new byte[] { 0xa0, 0x1f, 0x7b }).ToArray());
var before = source.ToByteArray();
var probe = new NativeEntryProbe();
Check(probe.Prepare(32, new[] { 32, 53 }, new[] { source }, new[] { 1_900_000_018 }, out _), "Displayed official source should be accepted");
Check(probe.Row!.Id == NativeEntryProbe.EntryId && probe.SourceId == 32, "Probe must have a distinct row identity and remember its source");
var normalized = probe.Row.Clone();
normalized.Id = source.Id;
Check(normalized.ToByteArray().SequenceEqual(before), "Every official field, including unknown fields, must survive cloning unchanged");
Check(probe.Row.InteractionId == 4321, "Native interaction ID must not be derived from the dating or synthetic row ID");
probe.Row.IconSpriteName[0] = "changed_probe_thumbnail";
probe.Row.ItemPath.Add("probe_only_path");
Check(source.ToByteArray().SequenceEqual(before), "Changing a cloned repeated field must not mutate the official source");

var collision = source.Clone();
collision.Id = NativeEntryProbe.EntryId;
Check(!probe.Prepare(32, new[] { 32 }, new[] { source, collision }, Array.Empty<int>(), out _)
    && probe.Row == null, "Even an undisplayed official row must prevent a synthetic ID collision");
Check(!probe.Prepare(32, new[] { 32 }, new[] { source }, new[] { NativeEntryProbe.EntryId }, out _), "Local entry IDs must not collide with the probe");
Check(!probe.Prepare(32, new[] { 53 }, new[] { source }, Array.Empty<int>(), out _), "Do not clone a source absent from the current official list");
Check(!probe.Prepare(53, new[] { 53 }, new[] { source }, Array.Empty<int>(), out _), "Do not synthesize missing official records");
var invalid = source.Clone();
invalid.InteractionId = 0;
Check(!probe.Prepare(32, new[] { 32 }, new[] { invalid }, Array.Empty<int>(), out _), "Reject a source without a native interaction binding");

// The game obfuscates property names separately from accessor method names.
// Verify the actual binary's native entry points without invoking Unity.
const BindingFlags instanceMethods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
Check(typeof(RawDataManager).BaseType!.GetMethod("get_Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) != null,
    "RawDataManager singleton accessor must exist on its actual base type");
foreach (var name in new[] { "get_RootUI", "get_InteractionPoints", "get_DefaultSpineMotionByGroup", "get_CurInteractionPointGroupId" })
    Check(typeof(SpineInteractionActionController).GetMethod(name, instanceMethods) != null, $"Native diagnostic accessor {name} must exist");
Check(typeof(SpineInteractionActionController).GetMethod("SetSpineInteraction", instanceMethods, null,
    new[] { typeof(int), typeof(SpineAnimationObject), typeof(SpineInteractionGroup), typeof(bool) }, null) != null,
    "Native controller hook must match the game's current method signature");

Console.WriteLine($"Passed {checks} native-entry probe checks against the game's protobuf types");
