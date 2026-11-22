using BD2PreviewInteractions;
using Proto.Design.common;

static LobbySettingItemTable Row(int id, string resource, int type = 2, int interactionId = 0) =>
    new() { Id = id, Type = type, InteractionResourceName = resource, InteractionId = interactionId };

var rows = new[]
{
    Row(32, "Illust_dating1"),
    Row(77, "Illust_dating7", interactionId: 1000296),
    Row(99, "Illust_dating9", interactionId: 1001197),
    Row(217, "Illust_dating9"),
    Row(121, "Illust_dating12", interactionId: 1061492),
    Row(207, "Illust_dating18", interactionId: 1000396),
    Row(208, "Illust_dating18", interactionId: 1000397),
    Row(100, "Illust_lobby"),
    Row(101, "Illust_dating1", type: 1),
};
var owned = new List<int> { 77, 101, 32, 111, 100 };

void CheckFilter(IReadOnlyList<int> expected, IEnumerable<int> incoming, bool enabled, params string[] hidden)
{
    var actual = NativeDatingEntries.Filter(incoming, rows, enabled, new HashSet<string>(hidden, StringComparer.OrdinalIgnoreCase));
    if (!actual.SequenceEqual(expected))
        throw new Exception($"Expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
}

CheckFilter(new[] { 77, 101, 32, 111, 100 }, owned, true);
CheckFilter(new[] { 77, 101, 111, 100 }, owned, true, "illust_dating1");
CheckFilter(new[] { 101, 111, 100 }, owned, false);
CheckFilter(Array.Empty<int>(), new[] { 32, 77 }, false);
CheckFilter(new[] { 99, 77 }, new[] { 99, 77 }, true);
CheckFilter(new[] { 77, 101, 32, 111, 100 }, owned, true, "illust_dating9");
if (!owned.SequenceEqual(new[] { 77, 101, 32, 111, 100 }))
    throw new Exception("Filtering must preserve the caller's account list");

var visible = NativeDatingEntries.Filter(owned, rows, true, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "illust_dating1" });
var warnings = new List<string>();
var additions = NativeDatingEntries.AppendConfigured(visible,
    new[] { "illust_dating7", "illust_dating12", "illust_dating18", "illust_dating9", "ILLUST_DATING9", "illust_dating100" },
    rows, id => id == 1001197 ? new SpineInteractionTable { Id = id } : null, warnings.Add);
if (!visible.SequenceEqual(new[] { 77, 101, 111, 100, 99 }))
    throw new Exception("Append valid configured lobby IDs once while preserving the incoming order");
if (additions.Count != 1 || additions[99] != 1001197 || warnings.Count != 3)
    throw new Exception("Track only injected rows and report unavailable or ambiguous official records");

var empty = new List<int>();
NativeDatingEntries.AppendConfigured(empty, new[] { "illust_dating9", "illust_dating7" },
    rows, id => new SpineInteractionTable { Id = id }, warnings.Add);
if (!empty.SequenceEqual(new[] { 99, 77 }))
    throw new Exception("An empty incoming list must receive configured entries in settings order");

Console.WriteLine("Passed 10 native dating list checks");
