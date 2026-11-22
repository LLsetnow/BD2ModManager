using System;
using System.Collections.Generic;
using System.Linq;
using Proto.Design.common;

namespace BD2PreviewInteractions;

internal static class NativeDatingEntries
{
    internal static List<int> Filter(IEnumerable<int> incomingIds, IEnumerable<LobbySettingItemTable> rows,
        bool enabled, HashSet<string> hiddenScenes)
    {
        var hiddenIds = new HashSet<int>(rows
            .Where(row => row.Type == 2 && row.InteractionResourceName.StartsWith("Illust_dating", StringComparison.OrdinalIgnoreCase))
            .Where(row => !enabled || hiddenScenes.Contains(row.InteractionResourceName))
            .Select(row => row.Id));
        return incomingIds.Where(id => !hiddenIds.Contains(id)).ToList();
    }

    internal static Dictionary<int, int> AppendConfigured(List<int> ids, IEnumerable<string> sceneIds,
        IEnumerable<LobbySettingItemTable> rows, Func<int, SpineInteractionTable?> getInteraction, Action<string> warn)
    {
        var officialRows = rows.Where(row => row.Type == 2 && row.Id > 0 && row.InteractionId > 0)
            .ToLookup(row => row.InteractionResourceName, StringComparer.OrdinalIgnoreCase);
        var added = new Dictionary<int, int>();
        foreach (var sceneId in sceneIds)
        {
            var matches = officialRows[sceneId].ToArray();
            if (matches.Length != 1)
            {
                warn($"Native dating entry {sceneId} skipped: expected one official lobby row, found {matches.Length}.");
                continue;
            }

            var row = matches[0];
            if (ids.Contains(row.Id)) continue;
            try
            {
                if (getInteraction(row.InteractionId)?.Id != row.InteractionId)
                {
                    warn($"Native dating entry {sceneId} skipped: interaction row {row.InteractionId} is missing.");
                    continue;
                }
                ids.Add(row.Id);
                added.Add(row.Id, row.InteractionId);
            }
            catch (Exception error)
            {
                warn($"Native dating entry {sceneId} skipped: {error.GetBaseException().Message}");
            }
        }
        return added;
    }
}
