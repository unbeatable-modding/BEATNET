using System;
using Arcade.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BEATNET;

internal sealed class BeatNetRhythm : IDisposable
{
    private readonly BeatNetAudioLevel audio = new();
    private readonly BeatNetMark[] beats = new BeatNetMark[4];
    private readonly RectTransform[] crosses = new RectTransform[4];
    private readonly AnimationCurve turn;
    private readonly BeatNetSpectrum bass;
    private readonly BeatNetSpectrum treble;
    private readonly TextMeshProUGUI tempo;
    private readonly TextMeshProUGUI points;
    private readonly BeatNetBpAnimation reward;
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
        var label = ui.Text(parent, "[beat tracking]", 11f, 218f, 32f, 150f, 20f, BeatNetColor.Accent);
        label.characterSpacing = 3f;
        tempo = ui.Text(parent, "", 11f, 358f, 32f, 98f, 20f, BeatNetColor.Accent);
        tempo.alignment = TextAlignmentOptions.Right;
        tempo.characterSpacing = 3f;
        points = ui.Text(parent, "", 16f, 218f, 128f, 210f, 28f, BeatNetColor.Text, BeatNetFont.Score);
        points.alignment = TextAlignmentOptions.Right;
        points.enableAutoSizing = true;
        points.fontSizeMin = 10f;
        points.fontSizeMax = 16f;
        for (var i = 0; i < beats.Length; i++)
        {
            beats[i] = Mark(ui, parent, "Beat", 220f + i * 58f, 62f, 34f, false);
            var number = ui.Text(parent, (i + 1).ToString(), 11f, 220f + i * 58f, 100f, 34f, 20f, BeatNetColor.Accent);
            number.alignment = TextAlignmentOptions.Center;
            if (i < 3)
            {
                ui.Text(parent, "&", 11f, 264f + i * 58f, 100f, 14f, 20f, BeatNetColor.Accent);
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
        reward = new BeatNetBpAnimation(ui, parent, points);
    }

    private static BeatNetMark Mark(BeatNetUi ui, Transform parent, string name, float left, float top, float size, bool cross)
    {
        var mark = ui.Rect(parent, name, left, top, size, size).gameObject.AddComponent<BeatNetMark>();
        mark.Cross = cross;
        mark.raycastTarget = false;
        ui.Tint(mark, BeatNetColor.Accent);
        return mark;
    }

    private static BeatNetSpectrum Bars(BeatNetUi ui, Transform parent, string name, string label, float left, Vector2 range, float height)
    {
        var root = ui.Rect(parent, name, left, 852f, 146f, 48f);
        var text = ui.Text(root, label, 10f, 0f, 0f, 146f, 14f, BeatNetColor.Accent);
        text.characterSpacing = 3f;
        text.alignment = TextAlignmentOptions.Center;
        var bars = ui.Rect(root, "Spectrum", 0f, 14f, 146f, 34f);
        ui.Fill(bars, "Start", BeatNetColor.Accent, 0f, 3f, 1.5f, 28f);
        ui.Fill(bars, "End", BeatNetColor.Accent, 144.5f, 3f, 1.5f, 28f);
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
            var bar = ui.Fill(bars, "Bar", BeatNetColor.Accent, 10f + i * 123f / (transforms.Length - 1), 17f, 3f, 3f).rectTransform;
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
        var accounts = Plugin.Accounts;
        reward.Tick(Time.unscaledDeltaTime);
        var value = reward.Value ?? accounts?.BpReward?.Before ?? accounts?.Bp;
        var text = accounts?.User == null ? "BP / log in" : value.HasValue
            ? BeatNetNumbers.Format(value.Value) + " BP" : "BP / syncing";
        if (points.text != text)
        {
            points.text = text;
            points.ForceMeshUpdate();
            var count = points.textInfo.characterCount;
            if (count > 0)
            {
                var character = points.textInfo.characterInfo[count - 1];
                var metrics = character.textElement.glyph.metrics;
                var right = character.origin + (metrics.horizontalBearingX + metrics.width) * character.scale;
                var margin = points.margin;
                margin.z -= points.rectTransform.rect.xMax - right;
                points.margin = margin;
            }
        }
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
        var audible = audio.Read();
        bass.Sync(audible);
        treble.Sync(audible);
    }

    internal void Reward(RectTransform source, BeatNetBpReward value) => reward.Start(source, value);

    internal void CancelReward() => reward.Cancel();

    public void Dispose()
    {
        reward.Cancel();
        audio.Dispose();
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

    internal void Sync(bool audible)
    {
        var active = source != null && source.isActiveAndEnabled && sourceBars != null;
        analyzer.enabled = !active;
        var values = active ? sourceBars! : samples;
        for (var i = 0; i < bars.Length; i++)
        {
            var size = bars[i].sizeDelta;
            size.y = audible && ArcadeAudioSpectrum.IsReady()
                ? Height(values[i].sizeDelta.y, minimum)
                : Decay(size.y, Time.unscaledDeltaTime);
            bars[i].sizeDelta = size;
            if (!audible)
            {
                var sample = samples[i].sizeDelta;
                sample.y = minimum;
                samples[i].sizeDelta = sample;
            }
        }
    }

    internal static float Height(float value, float minimum) => float.IsNaN(value) || float.IsInfinity(value)
        ? 3f : 3f + 31f * (1f - Mathf.Exp(-Mathf.Max(0f, value - minimum) / 31f));

    internal static float Decay(float height, float delta) => float.IsNaN(height) || float.IsInfinity(height)
        ? 3f : Mathf.MoveTowards(height, 3f, 200f * Mathf.Max(0f, delta));
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

internal sealed class BeatNetBpAnimation
{
    private readonly RectTransform root;
    private readonly RectTransform square;
    private readonly RectTransform[] pieces = new RectTransform[8];
    private readonly CanvasGroup group;
    private readonly TextMeshProUGUI target;
    private BeatNetBpReward? reward;
    private Vector3 origin;
    private float elapsed;

    internal double? Value => reward == null ? null : Count(reward.Before, reward.After, elapsed);
    internal bool Active => reward != null;

    internal BeatNetBpAnimation(BeatNetUi ui, Transform parent, TextMeshProUGUI target)
    {
        this.target = target;
        root = ui.Rect(parent, "Bp reward", 0f, 0f, 1640f, 940f);
        group = root.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = group.interactable = false;
        square = ui.Fill(root, "Square", BeatNetColor.Accent, 0f, 0f, 24f, 24f).rectTransform;
        square.pivot = new Vector2(0.5f, 0.5f);
        for (var index = 0; index < pieces.Length; index++)
        {
            pieces[index] = ui.Fill(root, "Fragment", BeatNetColor.Accent, 0f, 0f, 7f, 7f).rectTransform;
            pieces[index].pivot = new Vector2(0.5f, 0.5f);
        }
        root.gameObject.SetActive(false);
    }

    internal void Start(RectTransform source, BeatNetBpReward value)
    {
        Cancel();
        reward = value;
        elapsed = 0f;
        root.SetAsLastSibling();
        root.gameObject.SetActive(true);
        origin = root.InverseTransformPoint(source.TransformPoint(source.rect.center));
        square.gameObject.SetActive(true);
        foreach (var piece in pieces) { piece.gameObject.SetActive(false); }
        Tick(0f);
    }

    internal void Tick(float delta)
    {
        if (reward == null) { return; }
        var accounts = Plugin.Accounts;
        if (accounts?.BpReward != reward || accounts.UserId + "/" + accounts.Key != reward.Owner) { Cancel(); return; }
        elapsed += Mathf.Max(0f, delta);
        var rect = target.rectTransform;
        var end = root.InverseTransformPoint(rect.TransformPoint(new Vector3(rect.rect.xMax - 44f, rect.rect.center.y)));
        if (elapsed < 0.95f)
        {
            var travel = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - 0.15f) / 0.8f));
            var bend = Vector3.Lerp(origin, end, 0.5f) + new Vector3(100f, 120f);
            square.localPosition = (1f - travel) * (1f - travel) * origin + 2f * (1f - travel) * travel * bend + travel * travel * end;
            square.localScale = Vector3.one * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 0.15f));
            square.localRotation = Quaternion.Euler(0f, 0f, 180f * travel);
            group.alpha = 1f;
        }
        else
        {
            square.gameObject.SetActive(false);
            var fade = Mathf.Clamp01((elapsed - 0.95f) / 0.25f);
            group.alpha = 1f - Mathf.SmoothStep(0f, 1f, fade);
            for (var index = 0; index < pieces.Length; index++)
            {
                var piece = pieces[index];
                piece.gameObject.SetActive(true);
                var angle = index * Mathf.PI / 4f;
                piece.localPosition = end + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * (6f + 28f * fade);
                piece.localScale = Vector3.one * (1f - fade * 0.8f);
                piece.localRotation = Quaternion.Euler(0f, 0f, index * 45f + 90f * fade);
            }
        }
        if (elapsed >= 2.5f) { Cancel(); }
    }

    internal static double Count(double before, double after, float time)
    {
        var progress = Mathf.Clamp01((time - 1.2f) / 1.3f);
        var ease = 1f - (1f - progress) * (1f - progress) * (1f - progress);
        return before + (after - before) * ease;
    }

    internal void Cancel()
    {
        if (reward != null) { Plugin.Accounts?.FinishBpReward(reward); }
        reward = null;
        root.gameObject.SetActive(false);
    }
}

