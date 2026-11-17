using System;
using System.Collections.Generic;
using System.Linq;
using Proto.Design.common;

namespace BD2PreviewInteractions;

internal sealed class NativeDatingEntries
{
    internal Dictionary<int, NativeDatingEntry> Entries { get; } = new();
    internal HashSet<int> AddedIds { get; } = new();
    internal HashSet<int> HiddenIds { get; } = new();
    private List<int>? lastPreparedList;
    private List<int> lastOfficialIds = new();

    internal bool IsLocalOnlyInteraction(int interactionId) => interactionId > 0 &&
        Entries.Values.Any(entry => entry.Row.InteractionId == interactionId && AddedIds.Contains(entry.Row.Id));

    internal List<int> CopyOfficialIds(List<int> incoming)
    {
        // A fresh caller list can now contain a genuinely acquired official row.
        // Strip additions only when the UI reuses our own decorated list.
        return ReferenceEquals(incoming, lastPreparedList)
            ? new List<int>(lastOfficialIds)
            : new List<int>(incoming);
    }

    internal void RememberList(List<int> ids, List<int>? originals = null)
    {
        lastPreparedList = ids;
        if (originals != null) lastOfficialIds = new List<int>(originals);
    }

    internal void Clear()
    {
        Entries.Clear();
        AddedIds.Clear();
        HiddenIds.Clear();
    }

    internal List<string> Rebuild(List<int> ids, IEnumerable<string> enabledSceneIds,
        IEnumerable<LobbySettingItemTable> officialRows, Func<int, SpineInteractionTable?> getInteraction,
        bool hideDisabled = false)
    {
        Clear();
        var failures = new List<string>();
        var enabled = new HashSet<string>(enabledSceneIds, StringComparer.OrdinalIgnoreCase);
        var rows = officialRows.ToArray();
        lastOfficialIds = new List<int>(ids);
        if (hideDisabled)
        {
            var hidden = new HashSet<int>(rows.Where(row => row.Type == 2
                && row.InteractionResourceName.StartsWith("Illust_dating", StringComparison.OrdinalIgnoreCase)
                && !enabled.Contains(row.InteractionResourceName.ToLowerInvariant())).Select(row => row.Id));
            foreach (var id in ids.Where(hidden.Contains)) HiddenIds.Add(id);
            ids.RemoveAll(hidden.Contains);
        }
        foreach (var sceneId in enabled.OrderBy(id => id, StringComparer.Ordinal))
        {
            try
            {
                var candidates = rows.Where(candidate => candidate.Type == 2 && candidate.InteractionId > 0
                    && string.Equals(candidate.InteractionResourceName, sceneId, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (candidates.Length != 1)
                    throw new InvalidOperationException($"expected one official interaction row; found {candidates.Length}");
                var row = candidates[0];
                var interaction = getInteraction(row.InteractionId);
                if (interaction == null || interaction.Id != row.InteractionId)
                    throw new InvalidOperationException($"interaction table row {row.InteractionId} is missing");

                Entries.Add(row.Id, new NativeDatingEntry(sceneId, row, interaction));
                if (!ids.Contains(row.Id))
                {
                    ids.Add(row.Id);
                    AddedIds.Add(row.Id);
                }
            }
            catch (Exception error) { failures.Add($"{sceneId}: {error.GetBaseException().Message}"); }
        }
        return failures;
    }
}

internal sealed class NativeDatingEntry
{
    internal NativeDatingEntry(string sceneId, LobbySettingItemTable row, SpineInteractionTable interaction)
    {
        SceneId = sceneId;
        Row = row;
        Interaction = interaction;
    }

    internal string SceneId { get; }
    internal LobbySettingItemTable Row { get; }
    internal SpineInteractionTable Interaction { get; }
}
