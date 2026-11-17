using System;
using System.Collections.Generic;
using System.Linq;
using Proto.Design.common;

namespace BD2PreviewInteractions;

// This row is deliberately outside Specs: cells and selection must use the
// game's original implementations, including its original unlock checks.
internal sealed class NativeEntryProbe
{
    internal const int EntryId = 1_900_100_001;
    internal LobbySettingItemTable? Row { get; private set; }
    internal int SourceId { get; private set; }

    internal void Clear()
    {
        Row = null;
        SourceId = 0;
    }

    internal bool Prepare(int sourceId, IReadOnlyCollection<int> displayedIds,
        IEnumerable<LobbySettingItemTable> officialRows, IReadOnlyCollection<int> localIds, out string reason)
    {
        Clear();
        var rows = officialRows.ToArray();
        if (displayedIds.Contains(EntryId) || localIds.Contains(EntryId) || rows.Any(row => row.Id == EntryId))
        {
            reason = $"probe ID {EntryId} conflicts with an existing row";
            return false;
        }
        var source = rows.FirstOrDefault(row => row.Id == sourceId);
        if (!displayedIds.Contains(sourceId) || source == null || source.InteractionId <= 0
            || string.IsNullOrEmpty(source.InteractionResourceName))
        {
            reason = $"source {sourceId} must be a displayed official interaction row";
            return false;
        }
        // Preserve every official field and protobuf unknown field. In
        // particular InteractionId and IconSpriteName must not be synthesized.
        Row = source.Clone();
        Row.Id = EntryId;
        SourceId = source.Id;
        reason = "";
        return true;
    }
}
