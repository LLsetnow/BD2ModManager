using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Spine;
using Spine.Unity;
using UnityEngine;

namespace BD2PreviewInteractions;

internal static class ManagedSkeletonLoader
{
    private static readonly MethodInfo initializeWithData = typeof(SkeletonDataAsset).GetMethod("InitializeWithData",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(SkeletonData) }, null)
        ?? throw new MissingMethodException("Spine SkeletonDataAsset.InitializeWithData");
    // Capture Unity-backed objects on the main thread. Spine's readers then work
    // exclusively on managed bytes, atlas regions and geometry on a worker.
    internal static Task<SkeletonData> Read(TextAsset raw, AtlasAssetBase[] assets, float scale)
    {
        var bytes = raw.bytes;
        var atlases = assets.Select(a => a.GetAtlas()).ToArray();
        var textHeader = Encoding.UTF8.GetString(bytes, 0, Math.Min(128, bytes.Length)).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
        var binary = !textHeader.StartsWith("{", StringComparison.Ordinal);
        return Task.Run(() =>
        {
            var loader = new AtlasAttachmentLoader(atlases);
            if (binary)
            {
                var reader = new SkeletonBinary(loader) { Scale = scale };
                using var stream = new MemoryStream(bytes, false);
                return reader.ReadSkeletonData(stream);
            }
            else
            {
                var reader = new SkeletonJson(loader) { Scale = scale };
                using var text = new StringReader(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
                return reader.ReadSkeletonData(text);
            }
        });
    }

    internal static void SetData(SkeletonDataAsset asset, SkeletonData data)
    {
        if (data == null) throw new InvalidDataException("骨骼解析结果为空");
        // Assigning skeletonData alone skips Spine's coupled stateData cache.
        // Invoke the same initializer used by GetSkeletonData: it initializes
        // both caches and applies all configured animation mix settings.
        initializeWithData.Invoke(asset, new object[] { data });
    }
}
