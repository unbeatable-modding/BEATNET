using Arcade.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BEATNET;

internal sealed class BeatNetRhythm
{
    private readonly BeatNetMark[] beats = new BeatNetMark[4];
    private readonly RectTransform[] crosses = new RectTransform[4];
    private readonly AnimationCurve turn;
    private readonly BeatNetSpectrum bass;
    private readonly BeatNetSpectrum treble;
    private readonly TextMeshProUGUI tempo;
    private int bpm = -1;

    internal BeatNetRhythm(BeatNetUi ui, Transform parent, Transform content)
    {
        turn = new AnimationCurve(new Keyframe(0f, 0f, 12.139828f, 12.139828f), new Keyframe(0.35f, 1f), new Keyframe(1f, 1f));
        foreach (var animation in Resources.FindObjectsOfTypeAll<global::UI.RhythmAnimation>())
        {
            if (animation.animationType == global::UI.RhythmAnimation.EAnimationType.Rotation
                && animation.animationDuration == global::UI.RhythmAnimation.EAnimationDuration.EveryBar
                && animation.endVector3.z == 90f)
            {
                turn = animation.animationCurve;
                break;
            }
        }
        var label = ui.Text(parent, "[beat tracking]", 11f, 218f, 32f, 150f, 20f);
        label.characterSpacing = 3f;
        tempo = ui.Text(parent, "", 11f, 358f, 32f, 98f, 20f);
        tempo.alignment = TextAlignmentOptions.Right;
        tempo.characterSpacing = 3f;
        for (var i = 0; i < beats.Length; i++)
        {
            beats[i] = Mark(ui, parent, "Beat", 220f + i * 58f, 62f, 34f, false);
            var number = ui.Text(parent, (i + 1).ToString(), 11f, 220f + i * 58f, 100f, 34f, 20f);
            number.alignment = TextAlignmentOptions.Center;
            if (i < 3)
            {
                ui.Text(parent, "&", 11f, 264f + i * 58f, 100f, 14f, 20f);
            }
        }
        for (var i = 0; i < crosses.Length; i++)
        {
            var mark = Mark(ui, parent, "Plus", 1582f, 42f + i * 36f, 18f, true);
            var rect = mark.rectTransform;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition += new Vector2(9f, -9f);
            crosses[i] = rect;
        }
        bass = Bars(ui, content, "Bass", "[bass]", 220f, new Vector2(0.22408807f, 0.33410543f), 150f);
        treble = Bars(ui, content, "Treble", "[treble]", 556f, new Vector2(0f, 0.07932838f), 90f);
    }

    private static BeatNetMark Mark(BeatNetUi ui, Transform parent, string name, float left, float top, float size, bool cross)
    {
        var mark = ui.Rect(parent, name, left, top, size, size).gameObject.AddComponent<BeatNetMark>();
        mark.Cross = cross;
        mark.raycastTarget = false;
        ui.Tint(mark, BeatNetColor.Text);
        return mark;
    }

    private static BeatNetSpectrum Bars(BeatNetUi ui, Transform parent, string name, string label, float left, Vector2 range, float height)
    {
        var root = ui.Rect(parent, name, left, 852f, 146f, 48f);
        var text = ui.Text(root, label, 10f, 0f, 0f, 146f, 14f);
        text.characterSpacing = 3f;
        text.alignment = TextAlignmentOptions.Center;
        var bars = ui.Rect(root, "Spectrum", 0f, 14f, 146f, 34f);
        ui.Fill(bars, "Start", BeatNetColor.Text, 0f, 3f, 1.5f, 28f);
        ui.Fill(bars, "End", BeatNetColor.Text, 144.5f, 3f, 1.5f, 28f);
        var analyzer = bars.gameObject.AddComponent<SpectrumArray>();
        var transforms = new RectTransform[18];
        var samples = new RectTransform[18];
        var smoothing = 0.025f;
        SpectrumArray? source = null;
        RectTransform[]? sourceBars = null;
        var sourceHeight = new Vector2(3f, height);
        foreach (var native in Resources.FindObjectsOfTypeAll<SpectrumArray>())
        {
            if (native == analyzer || !native.gameObject.scene.isLoaded || native.transform.parent == null
                || native.GetComponentInParent<BeatNetPanel>() != null)
            {
                continue;
            }
            var match = name == "Bass" ? "Bass" : "Trebel";
            if (native.transform.parent.name == match)
            {
                range = (Vector2)AccessTools.Field(typeof(SpectrumArray), "spectrumRange").GetValue(native);
                sourceHeight = (Vector2)AccessTools.Field(typeof(SpectrumArray), "height").GetValue(native);
                smoothing = (float)AccessTools.Field(typeof(SpectrumArray), "smoothing").GetValue(native);
                sourceBars = (RectTransform[])AccessTools.Field(typeof(SpectrumArray), "bars").GetValue(native);
                if (sourceBars.Length == transforms.Length)
                {
                    source = native;
                }
                break;
            }
        }
        for (var i = 0; i < transforms.Length; i++)
        {
            var bar = ui.Fill(bars, "Bar", BeatNetColor.Text, 10f + i * 123f / (transforms.Length - 1), 17f, 3f, 3f).rectTransform;
            bar.pivot = new Vector2(0f, 0.5f);
            transforms[i] = bar;
            samples[i] = ui.Rect(bars, "Sample", 0f, 0f, 0f, sourceHeight.x);
        }
        AccessTools.Field(typeof(SpectrumArray), "bars").SetValue(analyzer, samples);
        AccessTools.Field(typeof(SpectrumArray), "height").SetValue(analyzer, sourceHeight);
        AccessTools.Field(typeof(SpectrumArray), "spectrumRange").SetValue(analyzer, range);
        AccessTools.Field(typeof(SpectrumArray), "smoothing").SetValue(analyzer, smoothing);
        analyzer.enabled = source == null || !source.isActiveAndEnabled;
        return new BeatNetSpectrum(transforms, samples, analyzer, source, sourceBars, sourceHeight.x);
    }

    internal void Tick()
    {
        var playing = ArcadeBGMManager.Instance != null && !ArcadeBGMManager.Paused && ArcadeBGMManager.BPM > 0;
        var currentBpm = playing ? Mathf.RoundToInt(ArcadeBGMManager.BPM * FileStorage.beatmapOptions.songSpeed) : 0;
        if (bpm != currentBpm)
        {
            bpm = currentBpm;
            tempo.text = bpm > 0 ? bpm + " bpm" : "-- bpm";
        }
        var time = ArcadeBGMManager.BarTime;
        for (var i = 0; i < crosses.Length; i++)
        {
            var phase = Mathf.Repeat(time - i * 0.25f, 1f);
            crosses[i].localRotation = Quaternion.Euler(0f, 0f, turn.Evaluate(phase) * 90f);
            beats[i].Set(playing ? 1f - phase : 0f);
        }
    }

    internal void LateTick()
    {
        bass.Sync();
        treble.Sync();
    }
}

internal sealed class BeatNetSpectrum
{
    private readonly RectTransform[] bars;
    private readonly RectTransform[] samples;
    private readonly SpectrumArray analyzer;
    private readonly SpectrumArray? source;
    private readonly RectTransform[]? sourceBars;
    private readonly float minimum;

    internal BeatNetSpectrum(RectTransform[] bars, RectTransform[] samples, SpectrumArray analyzer, SpectrumArray? source,
        RectTransform[]? sourceBars, float minimum)
    {
        this.bars = bars;
        this.samples = samples;
        this.analyzer = analyzer;
        this.source = source;
        this.sourceBars = sourceBars;
        this.minimum = minimum;
    }

    internal void Sync()
    {
        var active = source != null && source.isActiveAndEnabled && sourceBars != null;
        analyzer.enabled = !active;
        var values = active ? sourceBars! : samples;
        for (var i = 0; i < bars.Length; i++)
        {
            var size = bars[i].sizeDelta;
            size.y = Height(values[i].sizeDelta.y, minimum);
            bars[i].sizeDelta = size;
        }
    }

    internal static float Height(float value, float minimum) => 3f + 31f * (1f - Mathf.Exp(-Mathf.Max(0f, value - minimum) / 31f));
}

internal abstract class BeatNetGraphic : MaskableGraphic
{
    protected void Quad(VertexHelper mesh, float left, float top, float width, float height, float alpha = 1f)
    {
        var rect = rectTransform.rect;
        var tint = color;
        tint.a *= alpha;
        var start = mesh.currentVertCount;
        mesh.AddVert(new Vector3(rect.xMin + left, rect.yMax - top), tint, Vector2.zero);
        mesh.AddVert(new Vector3(rect.xMin + left + width, rect.yMax - top), tint, Vector2.zero);
        mesh.AddVert(new Vector3(rect.xMin + left + width, rect.yMax - top - height), tint, Vector2.zero);
        mesh.AddVert(new Vector3(rect.xMin + left, rect.yMax - top - height), tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start + 2, start + 3, start);
    }
}

internal sealed class BeatNetMark : BeatNetGraphic
{
    internal bool Cross;
    private float strength;

    internal void Set(float value)
    {
        if (Mathf.Abs(strength - value) > 0.01f)
        {
            strength = value;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var size = rectTransform.rect.width;
        if (Cross)
        {
            Quad(mesh, 0f, size * 0.33f, size, size * 0.34f);
            Quad(mesh, size * 0.33f, 0f, size * 0.34f, size * 0.33f);
            Quad(mesh, size * 0.33f, size * 0.67f, size * 0.34f, size * 0.33f);
            return;
        }
        Quad(mesh, 0f, 0f, size, size, strength);
        Quad(mesh, 0f, 0f, size, 1.5f);
        Quad(mesh, 0f, size - 1.5f, size, 1.5f);
        Quad(mesh, 0f, 0f, 1.5f, size);
        Quad(mesh, size - 1.5f, 0f, 1.5f, size);
        var rect = rectTransform.rect;
        var start = mesh.currentVertCount;
        mesh.AddVert(new Vector3(rect.xMin, rect.yMax - 2f), color, Vector2.zero);
        mesh.AddVert(new Vector3(rect.xMin + 2f, rect.yMax), color, Vector2.zero);
        mesh.AddVert(new Vector3(rect.xMax, rect.yMin + 2f), color, Vector2.zero);
        mesh.AddVert(new Vector3(rect.xMax - 2f, rect.yMin), color, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start + 2, start + 3, start);
    }
}

