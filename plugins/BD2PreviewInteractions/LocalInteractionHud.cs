using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace BD2PreviewInteractions;

internal sealed class LocalInteractionHud : MonoBehaviour
{
    private PreviewInteractionController controller = null!;
    private SpineInteractionUI ui = null!;
    private RectTransform root = null!;
    private GameObject? gaugeRoot;
    private Image? gaugeImage;
    private Component? percentText;
    private Transform? heart;
    private Image? originalGaugeImage;
    private RectTransform? resetVisual;
    private readonly List<(int Id, InteractionToolbarItem Item)> tools = new();
    private readonly Dictionary<string, PointMarkerGraphic> markers = new();
    private string toolSignature = "";
    private Canvas? canvas;
    private bool diagnosticWritten;
    private bool? previousPointVisibility;
    private GameObject? resetButton;
    private Rect resetClickRect;
    private Rect pointToggleRect;
    private Rect controlsRect;

    internal Rect ResetClickRect => resetClickRect;
    internal Rect PointToggleRect => pointToggleRect;
    internal bool ContainsControl(Vector2 screen) => controlsRect.Contains(new Vector2(screen.x, Screen.height - screen.y));

    internal void Initialize(SpineInteractionUI owner, PreviewInteractionController interaction, Canvas ownerCanvas)
    {
        ui = owner;
        controller = interaction;
        root = (RectTransform)transform;
        resetButton = AccessTools.Field(typeof(SpineInteractionUI), "_resetButton")?.GetValue(owner) as GameObject;
        // The character can use a nested Canvas that sorts above UIBase's
        // Canvas. Give all local controls one explicit UI canvas above those
        // character canvases, using the same camera and UI layer.
        var top = owner.GetComponentsInChildren<Canvas>(true).Append(ownerCanvas)
            .OrderBy(c => SortingLayer.GetLayerValueFromID(c.sortingLayerID)).ThenBy(c => c.sortingOrder).Last();
        gameObject.layer = ownerCanvas.gameObject.layer;
        canvas = gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingLayerID = top.sortingLayerID;
        canvas.sortingOrder = top.sortingOrder + 1;
        canvas.worldCamera = ownerCanvas.worldCamera;
        gameObject.AddComponent<GraphicRaycaster>();
        var nativeController = AccessTools.Field(typeof(SpineInteractionUI), "_spineInteractionGaugeController")?.GetValue(owner);
        var original = nativeController == null ? null : AccessTools.Field(nativeController.GetType(), "_gaugeUI")?.GetValue(nativeController) as Component;
        if (original != null && original.gameObject != owner.gameObject)
        {
            originalGaugeImage = AccessTools.Field(original.GetType(), "_gaugeImage")?.GetValue(original) as Image;
            gaugeRoot = Instantiate(original.gameObject, transform, false);
            gaugeRoot.name = "LocalAffectionGauge";
            PrepareVisualClone(gaugeRoot);
            var component = gaugeRoot.GetComponent(original.GetType());
            gaugeImage = AccessTools.Field(original.GetType(), "_gaugeImage")?.GetValue(component) as Image;
            percentText = AccessTools.Field(original.GetType(), "_percentText")?.GetValue(component) as Component;
            heart = (AccessTools.Field(original.GetType(), "_heratObject")?.GetValue(component) as GameObject)?.transform;
            foreach (var field in new[] { "_idleEffect", "_scoreUpEffect" })
                if (AccessTools.Field(original.GetType(), field)?.GetValue(component) is GameObject effect) effect.SetActive(false);
            if (gaugeRoot.transform is RectTransform rect && original.transform is RectTransform source)
            {
                rect.anchorMin = rect.anchorMax = new Vector2(0, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = source.rect.size;
                rect.anchoredPosition = new Vector2(130, -18);
                rect.localScale = Vector3.one;
            }
            if (gaugeImage == null) throw new InvalidOperationException("无法定位原生好感度条图像");
            ShowPath(gaugeImage.transform, gaugeRoot.transform);
            if (percentText != null) ShowPath(percentText.transform, gaugeRoot.transform);
            if (heart != null) ShowPath(heart, gaugeRoot.transform);
            // The gauge's native container may also contain Reset. Its local
            // copy must not draw another Reset alongside the dedicated control.
            if (resetButton != null && resetButton.transform != original.transform && resetButton.transform.IsChildOf(original.transform))
            {
                var indices = new Stack<int>();
                for (var node = resetButton.transform; node != original.transform; node = node.parent) indices.Push(node.GetSiblingIndex());
                var copy = gaugeRoot.transform;
                while (indices.Count > 0) copy = copy.GetChild(indices.Pop());
                copy.gameObject.SetActive(false);
            }
            gaugeRoot.SetActive(true);
        }
        else throw new InvalidOperationException("无法定位原生好感度条模板");
        CreateResetControl();
        controller.AttachHud(this);
        Plugin.Instance?.LogDiagnostic($"Local HUD canvas: layer={LayerMask.LayerToName(gameObject.layer)}, sort={canvas.sortingLayerName}/{canvas.sortingOrder}, camera={canvas.worldCamera?.name ?? "overlay"}, gauge={gaugeImage != null}");
    }

    internal void Refresh()
    {
        var group = controller.CurrentGroup;
        if (group == null) return;
        var gauge = group["gauge"];
        var enabled = gauge?["IsOn"]?.Value<bool>() ?? false;
        if (gaugeRoot != null) gaugeRoot.SetActive(enabled);
        if (enabled && gaugeImage != null)
        {
            AlignGaugeWithNativePosition();
            var min = gauge?["MinMaxScore"]?["x"]?.Value<float>() ?? 0;
            var max = gauge?["MinMaxScore"]?["y"]?.Value<float>() ?? 100;
            var value = Mathf.Clamp01((controller.CurrentGauge - min) / Mathf.Max(1, max - min));
            gaugeImage.fillAmount = value;
            if (percentText != null) AccessTools.Property(percentText.GetType(), "text")?.SetValue(percentText, $"{Mathf.RoundToInt(value * 100)}%");
            if (heart != null)
            {
                var rect = gaugeImage.rectTransform.rect;
                heart.position = gaugeImage.transform.TransformPoint(new Vector3(rect.center.x, Mathf.Lerp(rect.yMin, rect.yMax, value), 0));
            }
        }
        var ids = (group["tools"] as JArray ?? new JArray()).Select(t => t.Value<int>("id")).ToArray();
        var signature = string.Join(",", ids);
        if (signature != toolSignature) { RebuildTools(ids); toolSignature = signature; }
        LayoutControls();
        foreach (var tool in tools)
        {
            tool.Item.SetSelected(tool.Id == controller.CurrentTool);
            var button = tool.Item.GetComponentInChildren<Button>(true);
            if (button != null) button.interactable = !controller.IsTransitioning;
        }
        var active = new HashSet<string>();
        var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        foreach (var marker in controller.VisibleMarkers())
        {
            active.Add(marker.Key);
            if (!markers.TryGetValue(marker.Key, out var graphic))
            {
                var obj = new GameObject("LocalTouchPoint", typeof(RectTransform), typeof(PointMarkerGraphic));
                obj.layer = gameObject.layer;
                obj.transform.SetParent(transform, false);
                graphic = obj.GetComponent<PointMarkerGraphic>();
                graphic.raycastTarget = false;
                markers.Add(marker.Key, graphic);
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, marker.Screen, camera, out var position);
            graphic.rectTransform.anchoredPosition = position;
            graphic.rectTransform.sizeDelta = Vector2.one * (marker.Drag ? 44 : 28);
            graphic.Configure(marker.Drag, Plugin.Settings.MarkerOpacity);
            graphic.gameObject.SetActive(true);
        }
        foreach (var marker in markers) if (!active.Contains(marker.Key)) marker.Value.gameObject.SetActive(false);
        if (previousPointVisibility != controller.PointsVisible)
        {
            previousPointVisibility = controller.PointsVisible;
            if (controller.PointsVisible) Plugin.Instance?.LogDiagnostic($"Local markers visible: pose={controller.PoseIndex}, tool={controller.CurrentTool}, count={active.Count}, hudSize={root.rect.size}");
        }
        if (!diagnosticWritten)
        {
            diagnosticWritten = true;
            Plugin.Instance?.LogDiagnostic($"Local HUD ready: tools={tools.Count}, gaugeConfigured={enabled}, canvasActive={canvas?.isActiveAndEnabled}, cameraCullsLayer={canvas?.worldCamera == null || (canvas.worldCamera.cullingMask & (1 << gameObject.layer)) != 0}");
        }
    }

    private void PrepareVisualClone(GameObject clone, bool preserveVisualState = false)
    {
        LocalVisuals.DisableNativeScripts(clone);
        foreach (var child in clone.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = gameObject.layer;
        if (!preserveVisualState)
            foreach (var sprite in clone.GetComponentsInChildren<UISprite>(true)) sprite.enabled = true;
        foreach (var nested in clone.GetComponentsInChildren<Canvas>(true)) nested.overrideSorting = false;
        foreach (var group in clone.GetComponentsInChildren<CanvasGroup>(true))
        {
            if (!preserveVisualState || group.gameObject == clone) group.alpha = 1;
            group.interactable = true;
            group.blocksRaycasts = true;
        }
        foreach (var graphic in clone.GetComponentsInChildren<Graphic>(true))
        {
            if (!preserveVisualState)
            {
                graphic.enabled = true;
                graphic.canvasRenderer.SetAlpha(1);
            }
            graphic.raycastTarget = false;
        }
    }

    private static void ShowPath(Transform child, Transform boundary)
    {
        for (var current = child; current != null; current = current.parent)
        {
            current.gameObject.SetActive(true);
            if (current == boundary) break;
        }
    }

    private static Camera? CanvasCamera(Canvas? owner)
    {
        var rootCanvas = owner?.rootCanvas;
        if (rootCanvas == null || rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return owner!.worldCamera != null ? owner.worldCamera : rootCanvas.worldCamera;
    }

    private Rect ScreenBounds(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        var camera = CanvasCamera(rect.GetComponentInParent<Canvas>(true));
        var screen = corners.Select(c => RectTransformUtility.WorldToScreenPoint(camera, c)).ToArray();
        return Rect.MinMaxRect(screen.Min(p => p.x), Screen.height - screen.Max(p => p.y),
            screen.Max(p => p.x), Screen.height - screen.Min(p => p.y));
    }

    private Vector2 HudPosition(Vector2 topDownScreen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root,
            new Vector2(topDownScreen.x, Screen.height - topDownScreen.y), CanvasCamera(canvas), out var local);
        return local;
    }

    private void PlaceScreenRect(RectTransform rect, Rect bounds)
    {
        var min = HudPosition(new Vector2(bounds.xMin, bounds.yMax));
        var max = HudPosition(new Vector2(bounds.xMax, bounds.yMin));
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = (min + max) * 0.5f;
        rect.sizeDelta = new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y));
        rect.localScale = Vector3.one;
    }

    private void AlignGaugeWithNativePosition()
    {
        if (gaugeRoot == null || gaugeImage == null || originalGaugeImage == null) return;
        // Use the native bar below the avatar/list as the layout reference, even
        // while its graphics are hidden. Match both size and position instead
        // of anchoring a second, lower bar to the middle of the screen.
        var target = ScreenBounds(originalGaugeImage.rectTransform);
        var current = ScreenBounds(gaugeImage.rectTransform);
        if (target.height < 1 || current.height < 1) return;
        var ratio = target.height / current.height;
        if (Mathf.Abs(ratio - 1) > 0.001f) gaugeRoot.transform.localScale *= ratio;
        current = ScreenBounds(gaugeImage.rectTransform);
        var delta = HudPosition(target.center) - HudPosition(current.center);
        if (delta.sqrMagnitude > 0.0001f)
            gaugeRoot.transform.localPosition += new Vector3(delta.x, delta.y, 0);
    }

    private void CreateResetControl()
    {
        if (resetButton == null) throw new InvalidOperationException("无法定位原生 Reset 按钮");
        var copy = Instantiate(resetButton, transform, false);
        copy.name = "LocalReset";
        PrepareVisualClone(copy, preserveVisualState: true);
        // Keep inactive hover layers inactive and preserve their original alpha.
        // CanvasRenderer fade alpha is runtime state, so copy it explicitly too.
        var sourceGraphics = resetButton.GetComponentsInChildren<Graphic>(true);
        var copyGraphics = copy.GetComponentsInChildren<Graphic>(true);
        for (var i = 0; i < Math.Min(sourceGraphics.Length, copyGraphics.Length); i++)
        {
            copyGraphics[i].enabled = sourceGraphics[i].enabled;
            copyGraphics[i].color = sourceGraphics[i].color;
            copyGraphics[i].canvasRenderer.SetAlpha(sourceGraphics[i].canvasRenderer.GetAlpha());
        }
        // The native Selectable controls only the original visual transitions.
        // Its callbacks are cleared; the controller's screen-space hit region
        // owns the reset independently of native raycast filters/animations.
        var nativeButtons = copy.GetComponentsInChildren<Button>(true);
        foreach (var button in nativeButtons)
        {
            button.onClick = new Button.ButtonClickedEvent();
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.interactable = true;
            button.enabled = true;
        }
        if (nativeButtons.Any(button => button.transition == Selectable.Transition.Animation))
            foreach (var animator in copy.GetComponentsInChildren<Animator>(true)) animator.enabled = true;
        resetVisual = copy.transform as RectTransform;
        AddResetHoverSurface(copy);
        copy.SetActive(true);
    }

    private void AddResetHoverSurface(GameObject parent)
    {
        // This surface forwards pointer hover/press visuals to the native
        // Selectable. It deliberately has no Button and no click callback.
        var surface = new GameObject("LocalResetHover", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        surface.layer = gameObject.layer;
        surface.transform.SetParent(parent.transform, false);
        var rect = (RectTransform)surface.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var image = surface.GetComponent<Image>();
        image.color = Color.clear;
        image.raycastTarget = true;
        image.canvasRenderer.cullTransparentMesh = false;
        var group = surface.GetComponent<CanvasGroup>();
        group.ignoreParentGroups = true;
        group.interactable = true;
        group.blocksRaycasts = true;
    }

    private void AddClickSurface(GameObject parent, UnityEngine.Events.UnityAction clicked)
    {
        var click = new GameObject("LocalClick", typeof(RectTransform), typeof(Image), typeof(Button));
        click.layer = gameObject.layer;
        click.transform.SetParent(parent.transform, false);
        var rect = (RectTransform)click.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        click.GetComponent<Image>().color = Color.clear;
        var button = click.GetComponent<Button>();
        // A click must not leave the Reset in Selectable's highlighted focus state.
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(clicked);
    }

    private void LayoutControls()
    {
        const float gap = 8;
        const float toggleWidth = 96;
        const float toggleHeight = 28;
        var resetBounds = new Rect(Screen.width - 132, 24, 112, 40);
        if (resetButton != null && resetButton.transform is RectTransform resetRect)
        {
            var native = ScreenBounds(resetRect);
            if (native.width > 1 && native.height > 1) resetBounds = native;
        }
        resetClickRect = resetBounds;
        if (resetVisual != null) PlaceScreenRect(resetVisual, resetBounds);
        var center = Mathf.Clamp(resetBounds.center.x, toggleWidth * 0.5f + 8,
            Mathf.Max(toggleWidth * 0.5f + 8, Screen.width - toggleWidth * 0.5f - 8));
        var top = Mathf.Max(8, resetBounds.yMax + 12);
        var size = Mathf.Clamp(resetBounds.width * 0.85f, 64, 88);
        if (tools.Count > 0)
            size = Mathf.Min(size, Mathf.Max(36, (Screen.height - top - toggleHeight - 16 - gap * tools.Count) / tools.Count));
        for (var index = 0; index < tools.Count; index++)
        {
            if (tools[index].Item.transform is not RectTransform rect) continue;
            PlaceScreenRect(rect, new Rect(center - size * 0.5f, top + index * (size + gap), size, size));
        }
        pointToggleRect = new Rect(center - toggleWidth * 0.5f, top + tools.Count * (size + gap) + 4, toggleWidth, toggleHeight);
        var left = Mathf.Min(resetBounds.xMin, center - Mathf.Max(toggleWidth, size) * 0.5f);
        var right = Mathf.Max(resetBounds.xMax, center + Mathf.Max(toggleWidth, size) * 0.5f);
        controlsRect = Rect.MinMaxRect(left, resetBounds.yMin, right, pointToggleRect.yMax);
    }

    private void RebuildTools(int[] ids)
    {
        foreach (var tool in tools) if (tool.Item != null) Destroy(tool.Item.gameObject);
        tools.Clear();
        if (ids.Length == 0) return;
        var template = AccessTools.Field(typeof(SpineInteractionUI), "_orinInteractionToolbarItem")?.GetValue(ui) as InteractionToolbarItem;
        if (template == null) throw new InvalidOperationException("无法定位原生道具按钮模板");
        for (var index = 0; index < ids.Length; index++)
        {
            var id = ids[index];
            var obj = Instantiate(template.gameObject, transform, false);
            obj.name = "LocalTool_" + id;
            PrepareVisualClone(obj);
            var item = obj.GetComponent<InteractionToolbarItem>();
            if (id != 0) item.SetItem(id);
            else
            {
                var sprite = AccessTools.Field(typeof(InteractionToolbarItem), "_imageTool")?.GetValue(item);
                if (sprite != null) AccessTools.Method(sprite.GetType(), "SetSprite", new[] { typeof(string) })?.Invoke(sprite, new object[] { "icon_interaction_toolbar_1" });
            }
            if (AccessTools.Field(typeof(InteractionToolbarItem), "_imageTool")?.GetValue(item) is Component icon)
                ShowPath(icon.transform, obj.transform);
            item.SetActiveUI_HintObject(false);
            AddClickSurface(obj, () => controller.SelectTool(id));
            obj.SetActive(true);
            tools.Add((id, item));
        }
    }
}

internal sealed class PointMarkerGraphic : MaskableGraphic
{
    private bool drag;
    internal void Configure(bool draggingPoint, float opacity)
    {
        var updated = new Color(1, 1, 1, opacity);
        if (drag == draggingPoint && color == updated) return;
        drag = draggingPoint; color = updated; SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var radius = drag ? 20f : 12f;
        const int segments = 40;
        for (var i = 0; i < segments; i++)
        {
            var a = i * Mathf.PI * 2 / segments;
            var b = (i + 1) * Mathf.PI * 2 / segments;
            Quad(mesh, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius,
                new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius,
                new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * (radius - 1),
                new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius - 1));
            Triangle(mesh, Vector2.zero, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 2.5f, new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * 2.5f);
        }
        if (drag)
        {
            Quad(mesh, new Vector2(-10, -0.6f), new Vector2(10, -0.6f), new Vector2(10, 0.6f), new Vector2(-10, 0.6f));
            Triangle(mesh, new Vector2(-12, 0), new Vector2(-7, 3), new Vector2(-7, -3));
            Triangle(mesh, new Vector2(12, 0), new Vector2(7, -3), new Vector2(7, 3));
        }
    }

    private void Quad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        var index = mesh.currentVertCount;
        mesh.AddVert(a, color, Vector2.zero); mesh.AddVert(b, color, Vector2.zero);
        mesh.AddVert(c, color, Vector2.zero); mesh.AddVert(d, color, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2); mesh.AddTriangle(index, index + 2, index + 3);
    }
    private void Triangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c)
    {
        var index = mesh.currentVertCount;
        mesh.AddVert(a, color, Vector2.zero); mesh.AddVert(b, color, Vector2.zero); mesh.AddVert(c, color, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
    }
}
