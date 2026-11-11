using System;
using System.Collections.Generic;
using System.Linq;

namespace BD2CostumeSkins;

public sealed class SkinMapping
{
    public string SourceId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Enabled { get; set; } = true;
}

public sealed class SkinSettings
{
    public int SchemaVersion { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public List<SkinMapping> Mappings { get; set; } = new List<SkinMapping>();
    public Dictionary<string, string> SelectedSkins { get; set; } = new Dictionary<string, string>();

    public static SkinSettings Defaults() => new SkinSettings {
        Mappings = new List<SkinMapping> {
            new SkinMapping { SourceId = "001207", TargetId = "001297", Label = "甜蜜的诱惑" }
        },
        SelectedSkins = new Dictionary<string, string> { ["001207"] = "001207" }
    };

    public void Validate()
    {
        if (SchemaVersion != 1 || Mappings == null || SelectedSkins == null)
            throw new ArgumentException("不支持的皮肤配置版本");
        var pairs = new HashSet<string>();
        var sources = new HashSet<string>();
        foreach (var row in Mappings) {
            if (row == null || !ValidId(row.SourceId) || !ValidId(row.TargetId) || row.SourceId == row.TargetId
                || row.Label == null || row.Label.Length > 100 || !pairs.Add(row.SourceId + ":" + row.TargetId))
                throw new ArgumentException("皮肤映射编号或名称无效，或映射重复");
            sources.Add(row.SourceId);
        }
        if (Mappings.Any(row => sources.Contains(row.TargetId)))
            throw new ArgumentException("皮肤目标不能同时作为映射来源");
        foreach (var pair in SelectedSkins) {
            if (!sources.Contains(pair.Key) || !ValidId(pair.Value)
                || (pair.Key != pair.Value && !Mappings.Any(row => row.SourceId == pair.Key && row.TargetId == pair.Value)))
                throw new ArgumentException("当前皮肤不属于该服装的映射");
        }
    }

    public static bool ValidId(string id) => id != null && id.Length == 6 && id.All(c => c >= '0' && c <= '9') && id != "000000";
    public bool HasSource(int id) => Enabled && Mappings.Any(row => row.Enabled && row.SourceId == id.ToString("D6"));

    // A disabled or removed choice falls back to the base appearance, without changing the saved preference.
    public int Resolve(int source, int nativeDesign)
    {
        var key = source.ToString("D6");
        if (!HasSource(source)) return nativeDesign;
        if (!SelectedSkins.TryGetValue(key, out var selected)) return nativeDesign;
        if (selected == key) return source;
        return Mappings.Any(row => row.Enabled && row.SourceId == key && row.TargetId == selected)
            ? int.Parse(selected) : source;
    }

    public string Next(int source, int currentDesign)
    {
        var key = source.ToString("D6");
        var choices = new[] { key }.Concat(Mappings.Where(row => row.Enabled && row.SourceId == key).Select(row => row.TargetId)).ToList();
        return choices[(choices.IndexOf(currentDesign.ToString("D6")) + 1) % choices.Count];
    }
}
