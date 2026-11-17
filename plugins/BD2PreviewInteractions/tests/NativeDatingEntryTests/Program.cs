using BD2PreviewInteractions;
using Google.Protobuf;
using Proto.Design.common;
using System.Text.Json;

var checks = 0;
void Check(bool result, string message)
{
    checks++;
    if (!result) throw new Exception(message);
}

// Use the actual extracted protobufs. No Unity execution, account state or
// fabricated official records are needed for list/record lifecycle checks.
using var report = JsonDocument.Parse(File.ReadAllText(args[0]));
var exported = report.RootElement.GetProperty("entries").EnumerateArray().ToArray();
var rows = exported.Select(entry => LobbySettingItemTable.Parser.ParseFrom(Convert.FromBase64String(entry.GetProperty("lobbyProtobufBase64").GetString()!))).ToArray();
var interactions = exported.Select(entry => SpineInteractionTable.Parser.ParseFrom(Convert.FromBase64String(entry.GetProperty("interactionProtobufBase64").GetString()!))).ToDictionary(row => row.Id);
var enabled = exported.Select(entry => entry.GetProperty("sceneId").GetString()!).ToArray();
var originalBytes = rows.ToDictionary(row => row.Id, row => row.ToByteArray());
var interactionBytes = interactions.ToDictionary(pair => pair.Key, pair => pair.Value.ToByteArray());
SpineInteractionTable? Lookup(int id) => interactions.GetValueOrDefault(id);
var original = new List<int> { 326, 261, 184, 153, 138, 135, 127, 114, 105, 94, 50, 73, 59, 36, 32, 53 };
var catalog = new NativeDatingEntries();
var ids = catalog.CopyOfficialIds(original);
var failures = catalog.Rebuild(ids, enabled, rows, Lookup);
Check(failures.Count == 0, string.Join("; ", failures));
Check(ids.Count == 22 && ids.Distinct().Count() == 22 && original.Count == 16, "Six unique official entries must be added without changing the caller's original list");
Check(catalog.AddedIds.SetEquals(new[] { 77, 99, 121, 207, 225, 274 }), "Use the six verified official row IDs, excluding InteractionId=0 duplicates");
Check(interactions.Keys.All(catalog.IsLocalOnlyInteraction), "All six injected interactions must avoid unsupported server reward claims");
Check(!catalog.IsLocalOnlyInteraction(0) && !catalog.IsLocalOnlyInteraction(1) && !catalog.IsLocalOnlyInteraction(1900000007), "Unknown, owned and synthetic IDs must not suppress normal rewards");
Check(catalog.Entries[121].Row.CostumeId == 61402 && catalog.Entries[121].Row.PrestigeSkinId == 61492, "Zenith costume and prestige skin IDs must stay distinct");
Check(catalog.Entries[225].Interaction.UnlockDatinggroupId == 14 && catalog.Entries[225].Interaction.UnlockDatingId == 10, "Native dating19 chapter checks must remain intact");
foreach (var entry in catalog.Entries.Values)
{
    Check(entry.Row.ToByteArray().SequenceEqual(originalBytes[entry.Row.Id]), $"Do not mutate official lobby row {entry.Row.Id}");
    Check(entry.Interaction.ToByteArray().SequenceEqual(interactionBytes[entry.Interaction.Id]), $"Do not mutate official interaction row {entry.Interaction.Id}");
}

catalog.RememberList(ids);
var reused = catalog.CopyOfficialIds(ids);
Check(reused.SequenceEqual(original), "Reusing the decorated UI list must first strip only the previous native additions");
catalog.Rebuild(reused, enabled, rows, Lookup);
Check(reused.Count == 22 && reused.Distinct().Count() == 22, "Re-entering the native list must not duplicate entries");
catalog.RememberList(reused);
var disabled = catalog.CopyOfficialIds(reused);
catalog.Clear();
Check(disabled.SequenceEqual(original) && catalog.Entries.Count == 0, "Disabling additions must restore the original UI list");
Check(!interactions.Keys.Any(catalog.IsLocalOnlyInteraction), "Clearing native additions must clear the local-only reward policy");

catalog.Rebuild(ids = new List<int>(original), enabled, rows, Lookup);
catalog.RememberList(ids);
var newlyOwned = new List<int>(original) { 77 };
var owned = catalog.CopyOfficialIds(newlyOwned);
Check(owned.Contains(77), "A fresh account-provided list must retain a genuinely acquired row even if the plugin previously added it");
catalog.Rebuild(owned, enabled, rows, Lookup);
Check(owned.Count == 22 && catalog.AddedIds.Count == 5 && !catalog.AddedIds.Contains(77), "Do not re-add or claim ownership of an existing official entry");
Check(!catalog.IsLocalOnlyInteraction(1000296) && catalog.IsLocalOnlyInteraction(1001197), "Newly acquired scenes must recover normal rewards while other injected scenes remain local-only");
catalog.RememberList(owned);
Check(catalog.CopyOfficialIds(owned).Contains(77), "Disabling the plugin must preserve genuinely owned entries");

var subset = new List<int>(original);
catalog.Rebuild(subset, new[] { "illust_dating7", "illust_dating12" }, rows, Lookup);
Check(subset.Count == 18 && catalog.Entries.Keys.Order().SequenceEqual(new[] { 77, 121 }), "Per-scene enable settings must apply to native entries");
var missing = new List<int>(original);
failures = catalog.Rebuild(missing, new[] { "illust_dating9" }, rows, _ => null);
Check(missing.SequenceEqual(original) && catalog.Entries.Count == 0 && failures.Count == 1, "Reject a lobby row whose interaction record cannot be read");
var zeroBinding = rows.Single(row => row.Id == 121).Clone();
zeroBinding.Id = 161;
zeroBinding.InteractionId = 0;
failures = catalog.Rebuild(missing, new[] { "illust_dating12" }, rows.Where(row => row.Id != 121).Append(zeroBinding), Lookup);
Check(failures.Count == 1 && !missing.Contains(161) && catalog.Entries.Count == 0, "Do not substitute a same-resource row without a native interaction binding");
var changed = rows.Single(row => row.Id == 77).Clone();
changed.InteractionResourceName = "Illust_dating99";
failures = catalog.Rebuild(missing, new[] { "illust_dating7" }, new[] { changed }, Lookup);
Check(failures.Count == 1 && !missing.Contains(77), "Reject a game update that repurposes a verified row ID");

if (args.Length > 1)
{
    using var all = JsonDocument.Parse(File.ReadAllText(args[1]));
    var records = all.RootElement.EnumerateArray().ToArray();
    var allRows = records.Select(r => LobbySettingItemTable.Parser.ParseFrom(Convert.FromBase64String(r.GetProperty("lobbyProtobufBase64").GetString()!))).ToArray();
    var allInteractions = records.Where(r => r.GetProperty("interaction").ValueKind != JsonValueKind.Null)
        .Select(r => SpineInteractionTable.Parser.ParseFrom(Convert.FromBase64String(r.GetProperty("interactionProtobufBase64").GetString()!))).ToDictionary(r => r.Id);
    var allScenes = allRows.Select(r => r.InteractionResourceName.ToLowerInvariant()).Distinct().ToArray();
    var allIds = new List<int>(original);
    failures = catalog.Rebuild(allIds, allScenes, allRows, id => allInteractions.GetValueOrDefault(id), hideDisabled: true);
    Check(failures.Count == 0 && catalog.Entries.Count == 22 && allIds.Count == 22, "Discover native records for all 22 scenes and exclude the five unlinked variants");
    Check(original.All(id => !catalog.IsLocalOnlyInteraction(catalog.Entries[id].Row.InteractionId)), "All 16 truly owned native scenes must retain their reward path");
    catalog.RememberList(allIds);
    var hidden = catalog.CopyOfficialIds(allIds);
    catalog.Rebuild(hidden, new[] { "illust_dating7" }, allRows, id => allInteractions.GetValueOrDefault(id), hideDisabled: true);
    Check(hidden.SequenceEqual(new[] { 77 }) && catalog.HiddenIds.Count == 16, "Disabling owned native scenes must hide their list entries");
    catalog.RememberList(hidden);
    var restored = catalog.CopyOfficialIds(hidden);
    Check(restored.SequenceEqual(original), "Restore hidden official rows before applying new switches on a reused list");
    catalog.Rebuild(restored, Array.Empty<string>(), allRows, id => allInteractions.GetValueOrDefault(id), hideDisabled: true);
    Check(restored.Count == 0 && catalog.Entries.Count == 0, "All native switches off must produce an empty native list");
}
Console.WriteLine($"Passed {checks} native dating record and visibility checks");
