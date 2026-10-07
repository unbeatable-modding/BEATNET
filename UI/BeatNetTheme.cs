using System.Collections.Generic;
using UnityEngine;

namespace BEATNET;

internal sealed class BeatNetTheme
{
    private readonly Dictionary<BeatNetColor, Color> colors = new();
    private readonly Color background;
    private readonly Color text;

    internal Color this[BeatNetColor role] => colors[role];

    internal BeatNetTheme(Color[] palette)
    {
        background = Slot(palette, 1, new Color(0.976f, 0.969f, 0.835f));
        var accent = Slot(palette, 0, new Color(1f, 0.294f, 0.49f));
        text = accent;
        var surface = Shade(0.035f);
        var card = Shade(0.065f);
        var selected = Shade(0.11f);
        var highlight = Shade(0.16f);
        var surfaces = new[] { background, surface, card, selected, highlight };
        var minimum = Mathf.Min(4.5f, ContrastOn(text, surfaces) * 0.85f);
        var muted = Color.Lerp(text, background, 0.18f);
        if (ContrastOn(muted, surfaces) < minimum)
        {
            var low = 0f;
            var high = 0.18f;
            for (var step = 0; step < 16; step++)
            {
                var amount = (low + high) * 0.5f;
                if (ContrastOn(Color.Lerp(text, background, amount), surfaces) >= minimum) { low = amount; }
                else { high = amount; }
            }
            muted = Color.Lerp(text, background, low);
        }
        colors[BeatNetColor.Backdrop] = new Color(0f, 0f, 0f, 0.78f);
        colors[BeatNetColor.Background] = background;
        colors[BeatNetColor.Surface] = surface;
        colors[BeatNetColor.Card] = card;
        colors[BeatNetColor.Selected] = selected;
        colors[BeatNetColor.Highlight] = highlight;
        colors[BeatNetColor.Text] = text;
        colors[BeatNetColor.Muted] = muted;
        colors[BeatNetColor.Accent] = accent;
        colors[BeatNetColor.AccentText] = accent;
        colors[BeatNetColor.OnAccent] = Readable(palette, accent, background, 4.5f);
        colors[BeatNetColor.Line] = Color.Lerp(background, text, 0.2f);
        colors[BeatNetColor.Decoration] = Slot(palette, 2, Color.Lerp(background, text, 0.11f));
        colors[BeatNetColor.OnHighlight] = Readable(palette, colors[BeatNetColor.Decoration], text, 4.5f);
        colors[BeatNetColor.Cover] = CoverTint(surface);
        colors[BeatNetColor.Disabled] = Color.Lerp(background, text, 0.4f);
    }

    private static Color CoverTint(Color surface)
    {
        var luminance = Luminance(surface);
        var low = 0f;
        var high = 0.10f;
        for (var step = 0; step < 16; step++)
        {
            var alpha = (low + high) * 0.5f;
            var light = (luminance + (1f - luminance) * alpha + 0.05f) / (luminance + 0.05f);
            var dark = (luminance + 0.05f) / (luminance * (1f - alpha) + 0.05f);
            var contrast = Mathf.Max(Mathf.Max(light, dark), Mathf.Max(
                Contrast(Color.Lerp(surface, Color.white, alpha), surface),
                Contrast(Color.Lerp(surface, Color.black, alpha), surface)));
            if (contrast <= 1.15f) { low = alpha; }
            else { high = alpha; }
        }
        return new Color(1f, 1f, 1f, low);
    }

    private Color Shade(float amount)
    {
        var color = Color.Lerp(background, text, amount);
        var minimum = Mathf.Min(4.5f, Contrast(background, text) * 0.85f);
        if (Contrast(color, text) < minimum)
        {
            var low = 0f;
            var high = amount;
            for (var step = 0; step < 16; step++)
            {
                var middle = (low + high) * 0.5f;
                if (Contrast(Color.Lerp(background, text, middle), text) >= minimum) { low = middle; }
                else { high = middle; }
            }
            color = Color.Lerp(background, text, low);
        }
        return color;
    }

    private static Color Slot(Color[] palette, int slot, Color fallback)
    {
        var color = palette.Length > slot && palette[slot].a > 0f ? palette[slot] : fallback;
        color.a = 1f;
        return color;
    }

    private static Color Readable(Color[] palette, Color background, Color preferred, float minimum)
    {
        if (Contrast(preferred, background) >= minimum)
        {
            return preferred;
        }
        var ink = Slot(palette, 4, preferred);
        if (Contrast(ink, background) >= minimum)
        {
            return ink;
        }
        var best = preferred;
        for (var index = 0; index < Mathf.Min(5, palette.Length); index++)
        {
            var candidate = Slot(palette, index, best);
            if (Contrast(candidate, background) >= minimum)
            {
                return candidate;
            }
            if (Contrast(candidate, background) > Contrast(best, background))
            {
                best = candidate;
            }
        }
        return best;
    }

    internal static float Contrast(Color first, Color second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Mathf.Max(a, b) + 0.05f) / (Mathf.Min(a, b) + 0.05f);
    }

    private static float Luminance(Color color)
    {
        return Linear(color.r) * 0.2126f + Linear(color.g) * 0.7152f + Linear(color.b) * 0.0722f;
    }

    private static float Linear(float value) => value <= 0.04045f ? value / 12.92f : Mathf.Pow((value + 0.055f) / 1.055f, 2.4f);

    private static float ContrastOn(Color color, Color[] surfaces)
    {
        var minimum = float.MaxValue;
        foreach (var surface in surfaces)
        {
            minimum = Mathf.Min(minimum, Contrast(color, surface));
        }
        return minimum;
    }
}
