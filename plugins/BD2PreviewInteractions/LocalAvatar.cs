using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

namespace BD2PreviewInteractions;

// Display the costume's full standing-art thumbnail over the native thumbnail. The
// child image avoids races with the native cell's asynchronous thumbnail request.
internal sealed class LocalAvatar : MonoBehaviour
{
    private static readonly Dictionary<string, AsyncOperationHandle<Texture2D>> assets = new();
    private RawImage? portrait;
    private string? address;
    private int generation;

    internal void Bind(SceneSpec? spec)
    {
        generation++;
        address = spec?.AvatarAddress;
        if (portrait != null) portrait.gameObject.SetActive(false);
        if (address == null) return;
        if (!assets.TryGetValue(address, out var handle))
        {
            handle = Addressables.LoadAssetAsync<Texture2D>(address);
            assets[address] = handle;
        }
        var request = generation;
        if (handle.IsDone) Apply(handle, request);
        else handle.Completed += result =>
        {
            if (this != null) Apply(result, request);
        };
    }

    private void Apply(AsyncOperationHandle<Texture2D> handle, int request)
    {
        if (request != generation || address == null) return;
        if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
        {
            Plugin.Instance?.LogDiagnostic($"Cannot load costume portrait: {address}");
            return;
        }
        if (portrait == null)
        {
            var go = new GameObject("PreviewLocalPortrait", typeof(RectTransform), typeof(RawImage));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            portrait = go.GetComponent<RawImage>();
            portrait.raycastTarget = false;
            portrait.color = Color.white;
        }
        var texture = handle.Result;
        portrait.texture = texture;
        portrait.uvRect = new Rect(0, 0, 1, 1);
        portrait.gameObject.SetActive(true);
    }

    internal static void ReleaseAssets()
    {
        foreach (var handle in assets.Values) if (handle.IsValid()) Addressables.Release(handle);
        assets.Clear();
    }
}
