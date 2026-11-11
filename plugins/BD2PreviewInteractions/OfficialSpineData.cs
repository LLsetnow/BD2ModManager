using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Collections;
using HarmonyLib;
using Spine.Unity;
using Spine;
using UnityEngine;

namespace BD2PreviewInteractions;

// BrownDustX swaps these references in a shared SkeletonDataAsset. Capture them
// before that swap; local graphics get their own asset and never change the shared one.
internal sealed class OfficialSpineData
{
    private static readonly Dictionary<string, OfficialSpineData> originals = new(StringComparer.OrdinalIgnoreCase);
    private static MethodInfo? replaceMethod;
    private static MethodInfo? prepareMethod;
    internal TextAsset Raw { get; }
    internal AtlasAssetBase[] Atlases { get; }
    internal float Scale { get; }
    internal float DefaultMix { get; }
    private bool Valid => Raw != null && Atlases.Length > 0 && Atlases.All(a => a != null);

    private OfficialSpineData(SkeletonDataAsset asset)
    {
        Raw = asset.skeletonJSON;
        Atlases = asset.atlasAssets.ToArray();
        Scale = asset.scale;
        DefaultMix = asset.defaultMix;
    }

    internal static void Install(Harmony harmony)
    {
        var replacer = AccessTools.TypeByName("BrownDustX.HarmonyPatches.SpineReplacer");
        if (replacer == null) return;
        var method = AccessTools.Method(replacer, "ReplaceSpineData", new[] { typeof(SkeletonDataAsset) });
        if (method == null) throw new MissingMethodException("BrownDustX.ReplaceSpineData");
        replaceMethod = method;
        prepareMethod = AccessTools.Method(replacer, "PrepareModsData");
        harmony.Patch(method, prefix: new HarmonyMethod(typeof(OfficialSpineData), nameof(CaptureBeforeReplacement))
        { priority = Priority.First });
        Plugin.Instance?.LogDiagnostic("Official Spine capture hook installed before BrownDustX replacement");
    }

    internal static IEnumerator PrepareEnabledMod(string skeleton)
    {
        if (prepareMethod == null) yield break;
        var taskType = prepareMethod.GetParameters()[1].ParameterType.GetElementType()!;
        var arguments = new object?[] { skeleton, Activator.CreateInstance(taskType), skeleton };
        if (prepareMethod.Invoke(null, arguments) is not true) yield break;
        var task = arguments[1]!;
        var awaiter = task.GetType().GetMethod("GetAwaiter")!.Invoke(task, null)!;
        var completed = awaiter.GetType().GetProperty("IsCompleted")!;
        while (completed.GetValue(awaiter) is not true) yield return null;
        var runtime = awaiter.GetType().GetMethod("GetResult")!.Invoke(awaiter, null);
        // PrepareRuntimeAsync loads textures asynchronously but does not publish a
        // ModData itself. Publish its ready result before ReplaceSpineData, which
        // would otherwise call the old synchronous LoadModData fallback.
        var replacer = prepareMethod.DeclaringType!;
        var modData = AccessTools.Method(replacer, "BuildModDataFromPrepared")!.Invoke(null, new[] { runtime });
        var manager = AccessTools.TypeByName("BrownDustX.ModManager");
        if (manager != null && AccessTools.Field(manager, "LoadedModsData")?.GetValue(null) is IDictionary loaded && !loaded.Contains(skeleton))
            loaded[skeleton] = modData;
    }

    internal static bool ApplyEnabledMod(SkeletonDataAsset isolated, string skeleton)
    {
        if (replaceMethod == null) return false;
        var raw = isolated.skeletonJSON;
        var atlases = isolated.atlasAssets.ToArray();
        // Use BrownDustX's normal scene/part key while resolving the enabled mod.
        // Rename afterward so Initialize cannot replace this private instance again.
        isolated.name = skeleton + "_SkeletonData";
        replaceMethod.Invoke(null, new object[] { isolated });
        return isolated.skeletonJSON != raw || !isolated.atlasAssets.SequenceEqual(atlases);
    }

    internal int RestoreInteractionBones(SkeletonDataAsset isolated, IEnumerable<string> requiredBones, SkeletonData? parsedOriginal = null)
    {
        var target = isolated.GetSkeletonData(true);
        if (target == null) throw new InvalidOperationException("模组骨骼读取失败");
        var missing = requiredBones.Distinct().Where(name => target.FindBone(name) == null).ToArray();
        if (missing.Length == 0) return 0;
        var template = SkeletonDataAsset.CreateRuntimeInstance(Raw, Atlases, false, Scale);
        try
        {
            var original = parsedOriginal ?? template.GetSkeletonData(true);
            var previousCount = target.Bones.Count;
            BoneData EnsureBone(BoneData source)
            {
                var existing = target.FindBone(source.Name);
                if (existing != null) return existing;
                var parent = source.Parent == null ? null : EnsureBone(source.Parent);
                var bone = new BoneData(target.Bones.Count, source.Name, parent)
                {
                    X = source.X, Y = source.Y, Rotation = source.Rotation,
                    ScaleX = source.ScaleX, ScaleY = source.ScaleY,
                    ShearX = source.ShearX, ShearY = source.ShearY,
                    Length = source.Length, TransformMode = source.TransformMode,
                    // These are invisible input helpers; group/tool filtering controls availability.
                    SkinRequired = false,
                };
                target.Bones.Add(bone);
                return bone;
            }
            foreach (var name in missing)
            {
                var source = original.FindBone(name);
                if (source == null) throw new InvalidOperationException($"原版也缺少触点骨骼：{name}");
                EnsureBone(source);
            }
            return target.Bones.Count - previousCount;
        }
        finally { UnityEngine.Object.Destroy(template); }
    }

    private static void CaptureBeforeReplacement(SkeletonDataAsset __0)
    {
        try { Capture(__0); }
        catch (Exception error) { Plugin.Instance?.LogDiagnostic($"Cannot capture official Spine data: {error.Message}"); }
    }

    private static bool Matches(string assetName, string skeleton) =>
        assetName.Equals(skeleton, StringComparison.OrdinalIgnoreCase) ||
        assetName.StartsWith(skeleton + "_", StringComparison.OrdinalIgnoreCase);

    private static void Capture(SkeletonDataAsset asset)
    {
        if (asset == null || !Plugin.Specs.Values.Any(spec => Matches(asset.name, spec.SceneId))) return;
        if (originals.TryGetValue(asset.name, out var saved) && saved.Valid) return;
        if (asset.skeletonJSON == null || asset.atlasAssets == null || asset.atlasAssets.Length == 0) return;
        var data = new OfficialSpineData(asset);
        if (!data.Valid) return;
        originals[asset.name] = data;
        Plugin.Instance?.LogDiagnostic($"Captured official Spine data: {asset.name}; raw={data.Raw.name}; atlases={data.Atlases.Length}; scale={data.Scale}");
    }

    internal static OfficialSpineData Resolve(string skeleton)
    {
        var saved = originals.FirstOrDefault(p => Matches(p.Key, skeleton) && p.Value.Valid).Value;
        if (saved != null) return saved;
        // Inactive parts may not have reached SkeletonGraphic.Initialize yet.
        var source = Resources.FindObjectsOfTypeAll<SkeletonDataAsset>()
            .FirstOrDefault(asset => asset != null && Matches(asset.name, skeleton));
        if (source == null) throw new InvalidOperationException($"未加载官方骨骼：{skeleton}");
        Capture(source);
        if (originals.TryGetValue(source.name, out saved) && saved.Valid) return saved;
        throw new InvalidOperationException($"官方骨骼数据不完整：{skeleton}");
    }
}
