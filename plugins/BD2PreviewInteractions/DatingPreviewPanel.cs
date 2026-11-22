using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BD2PreviewInteractions;

internal sealed class DatingPreviewPanel
{
    private static readonly Color PanelColor = new(0.09f, 0.12f, 0.18f, 0.98f);
    private static readonly Color ButtonColor = new(0.17f, 0.30f, 0.48f, 1f);
    private const float EntryHeight = 52f;

    internal SpineInteractionUI Owner { get; }

    private readonly RectTransform root;
    private readonly RectTransform list;
    private readonly GameObject? cover;
    private readonly GameObject exit;
    private readonly Font font;
    private readonly bool ownsFont;
    private readonly SceneSpec[] scenes;
    private readonly Action<SceneSpec> openScene;
    private bool listBuilt;

    internal DatingPreviewPanel(SpineInteractionUI owner, IEnumerable<SceneSpec> scenes, bool nativeListEmpty,
        Action<SceneSpec> openScene, Action closePreview)
    {
        Owner = owner;
        this.scenes = scenes.OrderBy(scene => scene.LocalId).ToArray();
        this.openScene = openScene;

        var sceneRoot = AccessTools.Field(typeof(SpineInteractionUI), "_spinRootParent")?.GetValue(owner) as RectTransform;
        if (sceneRoot == null) throw new InvalidOperationException("Dating scene display is unavailable");
        var ownerCanvas = owner.GetComponentInParent<Canvas>(true) ?? sceneRoot.GetComponentInParent<Canvas>(true);
        if (ownerCanvas == null) throw new InvalidOperationException("Dating canvas is unavailable");

        root = CreateRect("BD2 P Preview", (RectTransform)ownerCanvas.transform);
        root.gameObject.SetActive(false);
        try
        {
            var systemFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei UI", "Microsoft JhengHei UI", "Arial" }, 20);
            ownsFont = systemFont != null;
            font = systemFont ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            root.gameObject.AddComponent<LocalSceneTag>();
            Stretch(root);
            var top = LocalVisuals.FindTopNativeCanvas(owner, ownerCanvas);
            var canvas = root.gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingLayerID = top.sortingLayerID;
            canvas.sortingOrder = top.sortingOrder + 2;
            canvas.worldCamera = ownerCanvas.worldCamera;
            root.gameObject.AddComponent<GraphicRaycaster>();

            if (nativeListEmpty)
            {
                var background = CreateRect("Empty Native Dating List", root);
                cover = background.gameObject;
                Stretch(background);
                background.gameObject.AddComponent<Image>().color = new Color(0.05f, 0.07f, 0.10f);
                var message = AddLabel(background, "原生约会入口已隐藏", 23);
                message.alignment = TextAnchor.MiddleCenter;
                var back = AddButton(root, "返回", () => owner.CloseUI());
                Place(back, new Vector2(0, 1), new Vector2(24, -100), new Vector2(112, 44));
            }

            var toggle = AddButton(root, "P 预览", ToggleList);
            Place(toggle, Vector2.one, new Vector2(-24, -100), new Vector2(120, 44));

            var exitButton = AddButton(root, "退出预览", closePreview);
            Place(exitButton, Vector2.one, new Vector2(-152, -100), new Vector2(120, 44));
            exit = exitButton.gameObject;
            exit.SetActive(false);

            list = CreateRect("P Scenes", root);
            list.gameObject.AddComponent<Image>().color = PanelColor;
            Place(list, Vector2.one, new Vector2(-24, -152), new Vector2(326, Mathf.Clamp(this.scenes.Length * EntryHeight + 16, 70, 324)));
            list.gameObject.SetActive(false);
            root.gameObject.SetActive(true);
        }
        catch
        {
            Destroy();
            throw;
        }
    }

    internal void SetPreview(bool open, bool loading)
    {
        if (root == null) return;
        var canExit = open || loading;
        if (exit.activeSelf != canExit) exit.SetActive(canExit);
        if (cover != null && cover.activeSelf == open) cover.SetActive(!open);
    }

    internal void Destroy()
    {
        if (root != null)
        {
            root.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(root.gameObject);
        }
        if (ownsFont) UnityEngine.Object.Destroy(font);
    }

    private void ToggleList()
    {
        if (list.gameObject.activeSelf)
        {
            list.gameObject.SetActive(false);
            return;
        }

        if (!listBuilt)
        {
            BuildList();
            listBuilt = true;
        }
        list.gameObject.SetActive(true);
    }

    private void BuildList()
    {
        if (scenes.Length == 0)
        {
            AddLabel(list, "请在 Mod Manager 中启用角色的 P 额外入口。", 18);
            return;
        }

        var scroll = list.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = CreateRect("Viewport", list);
        Stretch(viewport);
        viewport.offsetMin = new Vector2(8, 8);
        viewport.offsetMax = new Vector2(-8, -8);
        viewport.gameObject.AddComponent<RectMask2D>();

        var content = CreateRect("Entries", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0, scenes.Length * EntryHeight);
        scroll.viewport = viewport;
        scroll.content = content;

        for (var index = 0; index < scenes.Length; index++)
        {
            var scene = scenes[index];
            var button = AddButton(content, scene.Title, () =>
            {
                list.gameObject.SetActive(false);
                openScene(scene);
            });
            button.anchorMin = new Vector2(0, 1);
            button.anchorMax = Vector2.one;
            button.pivot = new Vector2(0.5f, 1);
            button.anchoredPosition = new Vector2(0, -index * EntryHeight);
            button.sizeDelta = new Vector2(0, EntryHeight - 4);
            var label = button.GetComponentInChildren<Text>(true);
            label.alignment = TextAnchor.MiddleLeft;
            label.rectTransform.offsetMin = new Vector2(58, 0);

            var portrait = CreateRect("Portrait", button);
            portrait.anchorMin = portrait.anchorMax = new Vector2(0, 0.5f);
            portrait.anchoredPosition = new Vector2(28, 0);
            portrait.sizeDelta = new Vector2(42, 42);
            var image = portrait.gameObject.AddComponent<RawImage>();
            image.color = Color.clear;
            image.raycastTarget = false;
            portrait.gameObject.AddComponent<LocalAvatar>().Bind(scene);
        }
    }

    private RectTransform AddButton(RectTransform parent, string label, Action clicked)
    {
        var rect = CreateRect(label, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = ButtonColor;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => clicked());
        AddLabel(rect, label, 19).alignment = TextAnchor.MiddleCenter;
        return rect;
    }

    private Text AddLabel(RectTransform parent, string value, int size)
    {
        var rect = CreateRect("Label", parent);
        Stretch(rect);
        rect.offsetMin = new Vector2(12, 0);
        rect.offsetMax = new Vector2(-12, 0);
        var label = rect.gameObject.AddComponent<Text>();
        label.font = font;
        label.fontSize = size;
        label.color = Color.white;
        label.text = value;
        label.alignment = TextAnchor.MiddleLeft;
        label.raycastTarget = false;
        return label;
    }

    private static RectTransform CreateRect(string name, RectTransform parent)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        var rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static void Place(RectTransform rect, Vector2 corner, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = corner;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
