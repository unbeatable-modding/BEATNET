using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Arcade.UI;
using Arcade.UI.SongSelect;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace BEATNET;

internal enum BeatNetColor
{
    Backdrop,
    Background,
    Surface,
    Card,
    Line,
    Text,
    Muted,
    Accent,
    AccentText,
    OnAccent,
    OnHighlight,
    Highlight,
    Selected,
    Decoration,
    Cover,
    Disabled,
}

internal enum BeatNetFont
{
    Body,
    Heading,
    Button,
    Display,
    Wallpaper,
    Prompt,
    Level,
    Score,
    Rank,
}

internal sealed class BeatNetUi : IDisposable
{
    private readonly Dictionary<BeatNetFont, TMP_FontAsset> fonts = new();
    private readonly Dictionary<BeatNetFont, Material> materials = new();
    private readonly Dictionary<BeatNetFont, Material> nativeMaterials = new();
    private readonly TextMeshProUGUI template;
    private readonly Graphic background;
    private readonly Scrollbar? scrollbarTemplate;
    private readonly Dictionary<Graphic, BeatNetColor> graphics = new();
    private readonly Dictionary<Selectable, (bool Primary, bool Selected, bool Inverted)> styles = new();
    private readonly List<TMP_InputField> fields = new();
    private UIColorPalette? palette;
    private BeatNetTheme theme = null!;
    private string paletteName = "";
    private bool controller;
    private BeatNetControl? back;

    internal BeatNetUi(TextMeshProUGUI template, Graphic background)
    {
        this.template = template;
        this.background = background;
        var available = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
        fonts[BeatNetFont.Body] = FindFont(available, "NotoSans-Regular SDF", template.font);
        fonts[BeatNetFont.Heading] = FindFont(available, "NotoSans-Black SDF", template.font);
        fonts[BeatNetFont.Button] = FindFont(available, "NotoSans-SemiBold SDF", fonts[BeatNetFont.Heading]);
        fonts[BeatNetFont.Display] = fonts[BeatNetFont.Heading];
        fonts[BeatNetFont.Prompt] = Resources.Load<TMP_FontAsset>("fonts & materials/promptfont sdf")
            ?? FindFont(available, "promptfont SDF", template.font);
        fonts[BeatNetFont.Level] = fonts[BeatNetFont.Score] = fonts[BeatNetFont.Rank] = template.font;
        foreach (var score in Resources.FindObjectsOfTypeAll<ArcadeSongScore>())
        {
            foreach (var item in new[] { (BeatNetFont.Level, "songLevel"), (BeatNetFont.Score, "songScore"), (BeatNetFont.Rank, "songRank") })
            {
                if (AccessTools.Field(typeof(ArcadeSongScore), item.Item2).GetValue(score) is TextMeshProUGUI native)
                {
                    fonts[item.Item1] = native.font;
                    nativeMaterials[item.Item1] = native.fontSharedMaterial;
                }
            }
            break;
        }
        fonts[BeatNetFont.Display] = FindFont(available, "RubikMonoOne-Regular SDF HQ",
            FindFont(available, "RubikMonoOne-Regular SDF", fonts[BeatNetFont.Score]));
        fonts[BeatNetFont.Wallpaper] = fonts[BeatNetFont.Display];
        foreach (var scrollbar in Resources.FindObjectsOfTypeAll<Scrollbar>())
        {
            if (scrollbar.name == "Scrollbar Vertical" && scrollbar.transform.parent?.name == "Scores"
                && scrollbar.transform.parent.parent?.name == "Leaderboard")
            {
                scrollbarTemplate = scrollbar;
                break;
            }
        }
        RefreshTheme();
    }

    private static TMP_FontAsset FindFont(TMP_FontAsset[] available, string name, TMP_FontAsset fallback)
    {
        foreach (var font in available)
        {
            if (font.name == name)
            {
                return font;
            }
        }
        return fallback;
    }

    internal void Font(TextMeshProUGUI text, BeatNetFont role)
    {
        var font = fonts[role];
        if (!materials.TryGetValue(role, out var material))
        {
            material = new Material(nativeMaterials.TryGetValue(role, out var native) ? native : font.material);
            materials[role] = material;
            var shader = Shader.Find("TextMeshPro/Distance Field") ?? Shader.Find("TextMeshPro/Mobile/Distance Field");
            if (shader != null)
            {
                material.shader = shader;
            }
            var outlined = role == BeatNetFont.Rank || role == BeatNetFont.Wallpaper;
            material.SetColor("_FaceColor", outlined ? new Color(1f, 1f, 1f, 0f) : Color.white);
            if (outlined)
            {
                material.SetColor("_OutlineColor", Color.white);
                if (material.GetFloat("_OutlineWidth") == 0f)
                {
                    material.SetFloat("_OutlineWidth", role == BeatNetFont.Rank ? 0.165f : 0.10f);
                }
                material.EnableKeyword("OUTLINE_ON");
            }
            if (role == BeatNetFont.Display)
            {
                material.SetFloat("_OutlineWidth", 0f);
                material.DisableKeyword("OUTLINE_ON");
            }
        }
        text.font = font;
        text.fontSharedMaterial = material;
        if (role == BeatNetFont.Display || role == BeatNetFont.Wallpaper)
        {
            text.fontStyle = FontStyles.Italic;
        }
    }

    internal IEnumerator PrepareShaders()
    {
        var variants = new ShaderVariantCollection();
        try
        {
            foreach (var material in materials.Values.Append(Graphic.defaultGraphicMaterial))
            {
                var keywords = material.shaderKeywords;
                variants.Add(new ShaderVariantCollection.ShaderVariant(material.shader, PassType.Normal, keywords));
                variants.Add(new ShaderVariantCollection.ShaderVariant(material.shader, PassType.Normal,
                    keywords.Append("UNITY_UI_CLIP_RECT").Distinct().ToArray()));
                variants.Add(new ShaderVariantCollection.ShaderVariant(material.shader, PassType.Normal,
                    keywords.Append("UNITY_UI_ALPHACLIP").Distinct().ToArray()));
                variants.Add(new ShaderVariantCollection.ShaderVariant(material.shader, PassType.Normal,
                    keywords.Concat(new[] { "UNITY_UI_CLIP_RECT", "UNITY_UI_ALPHACLIP" }).Distinct().ToArray()));
            }
            while (!variants.WarmUpProgressively(1))
            {
                yield return null;
            }
        }
        finally
        {
            UnityEngine.Object.Destroy(variants);
        }
    }

    internal void RefreshTheme()
    {
        var name = UIColorPaletteUpdater.SelectedPalette;
        if (palette != null && paletteName == name)
        {
            return;
        }
        var index = MenuPaletteIndex.CachedDefaultIndex;
        if (index != null && (index.TryGetPalette(name, out var entry) || index.TryGetPalette("Default", out entry)))
        {
            palette = entry.palette;
        }
        paletteName = name;
        theme = new BeatNetTheme(palette?.colors ?? new[] { template.color, background.color });
        foreach (var item in graphics)
        {
            if (item.Key != null)
            {
                ApplyTint(item.Key, item.Value);
            }
        }
        foreach (var item in styles)
        {
            if (item.Key != null)
            {
                ApplyStyle(item.Key, item.Value.Primary, item.Value.Selected, item.Value.Inverted);
            }
        }
        foreach (var field in fields)
        {
            field.caretColor = ColorFor(BeatNetColor.Accent);
            var selection = ColorFor(BeatNetColor.Accent);
            selection.a = 0.3f;
            field.selectionColor = selection;
        }
    }

    private Color ColorFor(BeatNetColor tone)
    {
        return theme[tone];
    }

    internal void Tint(Graphic graphic, BeatNetColor tone)
    {
        graphics[graphic] = tone;
        ApplyTint(graphic, tone);
    }

    private void ApplyTint(Graphic graphic, BeatNetColor tone)
    {
        var color = ColorFor(tone);
        if (graphic is TextMeshProUGUI text && text.fontSharedMaterial is Material material
            && material.GetColor("_FaceColor").a == 0f && material.GetFloat("_OutlineWidth") > 0f)
        {
            material.SetColor("_OutlineColor", color);
            if (text.materialForRendering is Material rendered)
            {
                rendered.SetColor("_OutlineColor", color);
            }
            text.color = Color.white;
            text.SetMaterialDirty();
            return;
        }
        graphic.color = color;
    }

    internal void Style(Selectable control, bool primary = false, bool selected = false, bool inverted = false)
    {
        styles[control] = (primary, selected, inverted);
        ApplyStyle(control, primary, selected, inverted);
    }

    private void ApplyStyle(Selectable control, bool primary, bool selected, bool inverted)
    {
        var colors = Colors(primary, selected);
        if (inverted)
        {
            var normal = colors.normalColor;
            colors.normalColor = colors.highlightedColor;
            colors.highlightedColor = colors.pressedColor = colors.selectedColor = normal;
        }
        control.colors = colors;
        if (control is BeatNetControl button)
        {
            var normal = ColorFor(primary ? BeatNetColor.OnAccent : BeatNetColor.Text);
            var highlight = ColorFor(BeatNetColor.OnHighlight);
            button.NormalText = inverted ? highlight : normal;
            button.HighlightText = inverted ? normal : highlight;
            button.DisabledText = ColorFor(BeatNetColor.Disabled);
            button.Refresh();
        }
    }

    internal void SetController(bool active)
    {
        controller = active;
        foreach (var control in styles.Keys)
        {
            if (control is BeatNetControl button)
            {
                button.Controller = active;
                button.Refresh();
            }
        }
        if (back != null)
        {
            back.Controller = active;
            back.Refresh();
        }
    }

    internal RectTransform Rect(Transform parent, string name, float left, float top, float width, float height)
    {
        var node = new GameObject(name, typeof(RectTransform));
        node.layer = parent.gameObject.layer;
        node.transform.SetParent(parent, false);
        var rect = (RectTransform)node.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
        return rect;
    }

    internal Image Fill(Transform parent, string name, BeatNetColor tone, float left, float top, float width, float height, bool raycast = false)
    {
        var image = Rect(parent, name, left, top, width, height).gameObject.AddComponent<Image>();
        Tint(image, tone);
        image.raycastTarget = raycast;
        return image;
    }

    internal TextMeshProUGUI Text(Transform parent, string value, float size, float left, float top, float width, float height, BeatNetColor tone = BeatNetColor.Text, BeatNetFont role = BeatNetFont.Body)
    {
        var rect = Rect(parent, "Label", left, top, width, height);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        Font(text, role);
        text.fontSize = size;
        Tint(text, tone);
        var numeric = role == BeatNetFont.Level || role == BeatNetFont.Score || role == BeatNetFont.Rank;
        text.alignment = numeric ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Left;
        text.richText = false;
        text.raycastTarget = false;
        text.overflowMode = numeric ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.text = value;
        return text;
    }

    internal void Backdrop(Transform parent)
    {
        var root = Rect(parent, "Background typography", 0f, 0f, 1640f, 188f);
        var mask = root.gameObject.AddComponent<Image>();
        mask.raycastTarget = false;
        root.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        var name = Text(root, "BEATNET", 240f, 46f, -38f, 1548f, 270f, BeatNetColor.Decoration, BeatNetFont.Wallpaper);
        name.alignment = TextAlignmentOptions.Center;
        name.enableAutoSizing = true;
        name.fontSizeMin = 180f;
        name.fontSizeMax = 240f;
        name.characterSpacing = 5f;
        name.overflowMode = TextOverflowModes.Overflow;
    }

    internal Button Button(Transform parent, string value, float left, float top, float width, float height, bool primary = false)
    {
        var rect = Rect(parent, "Button", left, top, width, height);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = Color.white;
        var button = image.gameObject.AddComponent<BeatNetControl>();
        button.Controller = controller;
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        Style(button, primary);
        var text = Text(rect, value, 22f, 16f, 0f, width - 32f, height, primary ? BeatNetColor.OnAccent : BeatNetColor.Text, BeatNetFont.Button);
        text.alignment = TextAlignmentOptions.Center;
        button.Label = text;
        button.Refresh();
        return button;
    }

    internal Button Back(Transform parent, Button original, TextMeshProUGUI number)
    {
        var clone = UnityEngine.Object.Instantiate(original.gameObject, parent, false);
        clone.SetActive(false);
        clone.name = "Back";
        var baseTriangle = UnityEngine.Object.Instantiate(number.transform.parent.Find("Triangle").gameObject, clone.transform, false);
        baseTriangle.name = "Base";
        baseTriangle.transform.SetAsFirstSibling();
        baseTriangle.SetActive(true);
        var index = UnityEngine.Object.Instantiate(number, clone.transform, false);
        index.transform.SetSiblingIndex(1);
        index.rectTransform.anchorMin = index.rectTransform.anchorMax = new Vector2(0f, 1f);
        index.rectTransform.localPosition = original.transform.InverseTransformPoint(number.transform.position);
        index.rectTransform.localRotation = Quaternion.identity;
        index.overflowMode = TextOverflowModes.Overflow;
        index.richText = true;
        index.text = "<mspace=12>04";
        index.gameObject.SetActive(true);
        BeatNetButton.RemoveMenuBehaviours(clone);
        foreach (var selectable in clone.GetComponentsInChildren<AnimatedSelectable>(true))
        {
            UnityEngine.Object.DestroyImmediate(selectable);
        }
        var source = clone.GetComponent<Button>();
        var target = clone.transform.Find("Triangle")?.GetComponent<Graphic>() ?? source.targetGraphic;
        UnityEngine.Object.DestroyImmediate(source);
        var button = clone.AddComponent<BeatNetControl>();
        button.Sound = BeatNetSound.None;
        button.Controller = controller;
        button.HoverOnly = true;
        button.targetGraphic = target;
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        foreach (var animator in clone.GetComponentsInChildren<Animator>(true))
        {
            animator.enabled = true;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime;
        }
        button.NativeAnimator = clone.GetComponent<Animator>();
        foreach (var group in clone.GetComponentsInChildren<CanvasGroup>(true))
        {
            group.alpha = 1f;
        }
        var rect = (RectTransform)clone.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = ((RectTransform)original.transform).anchoredPosition;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        foreach (var text in clone.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.raycastTarget = false;
            text.canvasRenderer.SetAlpha(1f);
        }
        if (target != null)
        {
            target.canvasRenderer.SetAlpha(1f);
        }
        back = button;
        clone.SetActive(true);
        button.Refresh();
        return button;
    }

    private ColorBlock Colors(bool primary = false, bool selected = false) => new()
    {
        normalColor = ColorFor(primary ? BeatNetColor.Accent : selected ? BeatNetColor.Selected : BeatNetColor.Card),
        highlightedColor = ColorFor(BeatNetColor.Decoration),
        pressedColor = ColorFor(BeatNetColor.Decoration),
        selectedColor = ColorFor(BeatNetColor.Decoration),
        disabledColor = ColorFor(BeatNetColor.Card),
        colorMultiplier = 1f,
        fadeDuration = 0.12f,
    };

    internal TMP_InputField Search(Transform parent, float left, float top, float width, float height)
    {
        var rect = Rect(parent, "Search", left, top, width, height);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = Color.white;
        var field = image.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic = image;
        field.gameObject.AddComponent<BeatNetFocusSound>().Click = true;
        Style(field);
        field.navigation = new Navigation { mode = Navigation.Mode.None };
        field.characterLimit = 256;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.customCaretColor = true;
        field.caretColor = ColorFor(BeatNetColor.Accent);
        var selection = ColorFor(BeatNetColor.Accent);
        selection.a = 0.3f;
        field.selectionColor = selection;
        fields.Add(field);
        var viewport = Rect(rect, "Text", 18f, 0f, width - 36f, height);
        viewport.gameObject.AddComponent<RectMask2D>();
        field.textViewport = viewport;
        field.textComponent = Text(viewport, "", 23f, 0f, 0f, width - 36f, height);
        field.textComponent.overflowMode = TextOverflowModes.Overflow;
        field.textComponent.isTextObjectScaleStatic = false;
        field.placeholder = Text(viewport, "Search songs, artists or mappers", 23f, 0f, 0f, width - 36f, height, BeatNetColor.Muted);
        Fill(rect, "Underline", BeatNetColor.Accent, 0f, height - 2f, width, 2f);
        FieldFocus(field);
        return field;
    }

    private void FieldFocus(TMP_InputField field)
    {
        var root = Rect(field.transform, "Focus", 0f, 0f, 0f, 0f);
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = new Vector2(4f, 4f);
        root.offsetMax = new Vector2(-4f, -4f);
        foreach (var vertical in new[] { false, true })
        {
            foreach (var far in new[] { false, true })
            {
                var edge = Fill(root, "Edge", BeatNetColor.Accent, 0f, 0f, 0f, 0f);
                var rect = edge.rectTransform;
                rect.anchorMin = vertical ? new Vector2(far ? 1f : 0f, 0f) : new Vector2(0f, far ? 1f : 0f);
                rect.anchorMax = vertical ? new Vector2(far ? 1f : 0f, 1f) : new Vector2(1f, far ? 1f : 0f);
                rect.pivot = new Vector2(far ? 1f : 0f, far ? 1f : 0f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = vertical ? new Vector2(2f, 0f) : new Vector2(0f, 2f);
            }
        }
        field.gameObject.AddComponent<BeatNetFieldFocus>().Marker = root.gameObject;
        root.gameObject.SetActive(false);
    }

    internal ScrollRect List(Transform parent, float left, float top, float width, float height)
    {
        var image = Fill(parent, "List", BeatNetColor.Background, left, top, width, height, true);
        var viewport = Rect(image.transform, "Viewport", 0f, 0f, width, height);
        viewport.gameObject.AddComponent<RectMask2D>();
        var scroll = image.gameObject.AddComponent<BeatNetScroll>();
        scroll.viewport = viewport;
        scroll.content = Rect(viewport, "Content", 0f, 0f, width, height);
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.elasticity = 0.1f;
        scroll.inertia = true;
        scroll.decelerationRate = 0.135f;
        scroll.scrollSensitivity = 120f;
        scroll.verticalScrollbar = Scrollbar(image.transform, width + 6f, 0f, height);
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return scroll;
    }

    private Scrollbar Scrollbar(Transform parent, float left, float top, float height)
    {
        Scrollbar scrollbar;
        if (scrollbarTemplate != null)
        {
            scrollbar = UnityEngine.Object.Instantiate(scrollbarTemplate, parent, false);
            scrollbar.name = "Scrollbar";
            scrollbar.gameObject.SetActive(false);
            BeatNetButton.RemoveMenuBehaviours(scrollbar.gameObject);
            var rect = (RectTransform)scrollbar.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(20f, height);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;
            foreach (var graphic in scrollbar.GetComponentsInChildren<Graphic>(true))
            {
                graphic.material = null;
                graphic.raycastTarget = graphic.gameObject == scrollbar.gameObject;
                if (graphic.gameObject == scrollbar.gameObject)
                {
                    graphic.color = Color.clear;
                }
                else
                {
                    Tint(graphic, BeatNetColor.Accent);
                }
            }
        }
        else
        {
            var track = Rect(parent, "Scrollbar", left, top, 20f, height);
            var background = track.gameObject.AddComponent<Image>();
            background.color = Color.clear;
            var area = Rect(track, "Sliding Area", 0f, 25f, 20f, height - 50f);
            var handle = Fill(area, "Handle", BeatNetColor.Accent, 7.8f, 0f, 4.4f, height - 50f);
            var handleRect = handle.rectTransform;
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = new Vector2(7.8f, 0f);
            handleRect.offsetMax = new Vector2(-7.8f, 0f);
            foreach (var center in new[] { 11f, height - 11f })
            {
                Fill(track, "Cross", BeatNetColor.Accent, 0.5f, center - 3f, 19f, 6f);
                Fill(track, "Cross", BeatNetColor.Accent, 7f, center - 9.5f, 6f, 19f);
            }
            scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handle;
        }
        scrollbar.onValueChanged = new UnityEngine.UI.Scrollbar.ScrollEvent();
        scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        scrollbar.transition = Selectable.Transition.None;
        scrollbar.gameObject.SetActive(true);
        return scrollbar;
    }

    public void Dispose()
    {
        foreach (var material in materials.Values)
        {
            UnityEngine.Object.Destroy(material);
        }
    }
}

internal static class BeatNetNumbers
{
    private static readonly CultureInfo Culture = ReadCulture();

    internal static string Format(double value) => value.ToString("N0", Culture);

    private static CultureInfo ReadCulture()
    {
        try
        {
            var name = new StringBuilder(85);
            if (GetUserDefaultLocaleName(name, name.Capacity) > 0) { return CultureInfo.GetCultureInfo(name.ToString()); }
        }
        catch (System.Exception) { }
        return CultureInfo.CurrentCulture;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetUserDefaultLocaleName(StringBuilder name, int length);
}
