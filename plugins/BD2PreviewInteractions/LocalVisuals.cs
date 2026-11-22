using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace BD2PreviewInteractions;

internal sealed class LocalSceneTag : MonoBehaviour { }

internal static class LocalVisuals
{
    internal static Canvas FindTopNativeCanvas(SpineInteractionUI owner, Canvas ownerCanvas) =>
        owner.GetComponentsInChildren<Canvas>(true)
            .Where(canvas => canvas.GetComponent<LocalSceneTag>() == null)
            .Append(ownerCanvas)
            .OrderBy(canvas => SortingLayer.GetLayerValueFromID(canvas.sortingLayerID))
            .ThenBy(canvas => canvas.sortingOrder).Last();

    internal static void DisableNativeScripts(GameObject root)
    {
        foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour is Spine.Unity.SkeletonGraphic || behaviour is Graphic ||
                behaviour is Canvas || behaviour is CanvasScaler || behaviour is CanvasGroup ||
                behaviour is LayoutGroup || behaviour is ContentSizeFitter || behaviour is AspectRatioFitter ||
                behaviour is Mask || behaviour is RectMask2D || behaviour is LocalSceneTag) continue;
            behaviour.enabled = false;
        }
    }
}
