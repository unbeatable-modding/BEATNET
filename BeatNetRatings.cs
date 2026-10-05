using System;
using System.Globalization;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BEATNET;

internal sealed class BeatNetRatings
{
    private readonly BeatNetUi ui;
    private readonly TextMeshProUGUI average;
    private readonly TextMeshProUGUI verdict;
    private readonly TextMeshProUGUI label;
    private readonly TextMeshProUGUI filledLabel;
    private readonly TextMeshProUGUI number;
    private readonly Image fill;
    private BeatmapEntry? entry;
    private Task<JObject>? request;
    private Task<JObject>? checkedRequest;
    private string requestKey = string.Empty;
    private string requestId = string.Empty;
    private string requestStamp = string.Empty;
    private string stamp = string.Empty;
    private string accountKey = string.Empty;
    private int? value;
    private bool available;
    private bool loaded;
    private bool saving;
    private bool previewing;
    private bool local;
    private bool blocked;
    private bool commitQueued;
    private BeatmapEntry? savedEntry;

    internal BeatNetRatingSlider Slider { get; }
    internal string Error { get; private set; } = string.Empty;
    internal Action<BeatmapEntry>? Saved { get; set; }

    internal BeatNetRatings(BeatNetUi ui, Transform parent)
    {
        this.ui = ui;
        average = ui.Text(parent, "", 34f, 1360f, 426f, 212f, 48f, BeatNetColor.Text, BeatNetFont.Heading);
        average.alignment = TextAlignmentOptions.Right;
        verdict = ui.Text(parent, "", 20f, 1360f, 472f, 212f, 32f, BeatNetColor.Text, BeatNetFont.Button);
        verdict.alignment = TextAlignmentOptions.Right;
        var track = ui.Fill(parent, "Your rating", BeatNetColor.Card, 1052f, 646f, 474f, 34f, true);
        Slider = track.gameObject.AddComponent<BeatNetRatingSlider>();
        Slider.targetGraphic = track;
        Slider.transition = Selectable.Transition.None;
        Slider.navigation = new Navigation { mode = Navigation.Mode.None };
        Slider.minValue = 1f;
        Slider.maxValue = 10f;
        Slider.wholeNumbers = false;
        var sliderFill = ui.Rect(track.transform, "Slider input", 0f, 0f, 474f, 34f);
        sliderFill.anchorMin = Vector2.zero;
        sliderFill.anchorMax = Vector2.one;
        sliderFill.offsetMin = sliderFill.offsetMax = Vector2.zero;
        Slider.fillRect = sliderFill;
        label = ui.Text(track.transform, "", 17f, 0f, 0f, 474f, 34f, BeatNetColor.Text, BeatNetFont.Button);
        label.alignment = TextAlignmentOptions.Center;
        fill = ui.Fill(track.transform, "Vote", BeatNetColor.Accent, 0f, 0f, 0f, 34f);
        fill.gameObject.AddComponent<RectMask2D>();
        filledLabel = ui.Text(fill.transform, "", 17f, 0f, 0f, 474f, 34f, BeatNetColor.Background, BeatNetFont.Button);
        filledLabel.alignment = TextAlignmentOptions.Center;
        number = ui.Text(parent, "-", 22f, 1534f, 646f, 38f, 34f, BeatNetColor.Text, BeatNetFont.Button);
        number.alignment = TextAlignmentOptions.Right;
        Slider.onValueChanged.AddListener(_ => { previewing = true; DrawVote(); });
        Slider.Released = Commit;
    }

    internal void Show(BeatmapEntry? beatmap, bool library)
    {
        entry = beatmap;
        local = library;
        var next = (beatmap?.Id ?? string.Empty) + ":" + (Plugin.Accounts?.Key ?? string.Empty);
        if (stamp != next)
        {
            stamp = next;
            value = null;
            available = previewing = false;
            loaded = false;
            commitQueued = false;
            savedEntry = null;
            checkedRequest = null;
            Plugin.Accounts?.Ratings.Retry(Plugin.Accounts.Key);
            Error = string.Empty;
            Slider.Restore(1f);
        }
        DrawAverage();
        Refresh(library, blocked);
    }

    internal void Refresh(bool library, bool busy)
    {
        local = library;
        blocked = busy;
        Slider.gameObject.SetActive(local && entry != null);
        number.gameObject.SetActive(local && entry != null);
        Slider.interactable = local && entry != null && available && Plugin.Accounts?.User != null && !blocked && !saving;
        DrawVote();
    }

    private void DrawAverage()
    {
        var online = entry != null && BeatNetClient.IsId(entry.Id);
        average.text = entry == null ? string.Empty : online ? (entry.Rating * 10).ToString("0.00", CultureInfo.GetCultureInfo("de-DE")) + "%" : "-";
        verdict.text = entry == null ? string.Empty : !online ? "Not on BEATNET" : entry.RatingCount == 0 ? "Unrated" : BeatNetRatingText.Describe(entry.Rating * 10);
    }

    private void DrawVote()
    {
        var vote = previewing ? BeatNetRatingMotion.Nearest(Slider.value) : value;
        var enabled = available && Plugin.Accounts?.User != null;
        number.text = vote?.ToString(CultureInfo.InvariantCulture) ?? "-";
        label.text = entry != null && !BeatNetClient.IsId(entry.Id) ? "BEATNET maps only"
            : BeatNetRatingText.Vote(vote, available, loaded, Plugin.Accounts?.User != null);
        filledLabel.text = label.text;
        fill.gameObject.SetActive(enabled && vote.HasValue && !blocked);
        fill.rectTransform.sizeDelta = new Vector2(vote.HasValue ? 474f * Mathf.InverseLerp(Slider.minValue, Slider.maxValue, Slider.DisplayValue) : 0f, 34f);
        ui.Tint(label, enabled ? BeatNetColor.Text : BeatNetColor.Disabled);
        ui.Tint(number, enabled ? BeatNetColor.Text : BeatNetColor.Disabled);
    }

    internal void Adjust(int direction)
    {
        if (!Slider.interactable) { return; }
        previewing = true;
        Slider.Snap(BeatNetRatingMotion.Nearest(Slider.value + direction));
        DrawVote();
    }

    internal void Commit()
    {
        if (!Slider.interactable || entry == null || Plugin.Accounts == null) { return; }
        var vote = BeatNetRatingMotion.Nearest(Slider.value);
        Slider.Snap(vote);
        previewing = true;
        DrawVote();
        if (request != null) { commitQueued = true; return; }
        commitQueued = false;
        if (vote == value) { previewing = false; DrawVote(); return; }
        saving = true;
        previewing = true;
        Error = string.Empty;
        requestStamp = stamp;
        requestKey = Plugin.Accounts.Key;
        requestId = entry.Id;
        request = Plugin.Accounts.Request(new JObject { ["action"] = "rate", ["key"] = Plugin.Accounts.Key, ["projectId"] = entry.Id, ["value"] = vote });
        Refresh(local, blocked);
    }

    internal void Tick()
    {
        var key = Plugin.Accounts?.Key ?? string.Empty;
        if (accountKey != key)
        {
            accountKey = key;
            Show(entry, local);
        }
        if (request?.IsCompleted == true)
        {
            var wasSaving = saving;
            var matches = requestStamp == stamp && (wasSaving || Plugin.Accounts?.Ratings.IsCurrent(requestKey, request) == true);
            if (matches && !wasSaving) { checkedRequest = request; }
            try
            {
                var response = request.GetAwaiter().GetResult();
                var result = wasSaving ? response : BeatNetRatingCache.ForMap(response, requestId);
                var savedRequest = wasSaving ? Plugin.Accounts?.Ratings.Store(requestKey, requestId, result) : null;
                if (matches && entry != null)
                {
                    if (wasSaving) { checkedRequest = savedRequest; }
                    if (wasSaving)
                    {
                        entry.Rating = (double?)result["average"] ?? 0;
                        entry.RatingCount = (int?)result["count"] ?? 0;
                    }
                    value = (int?)result["value"];
                    available = (bool?)result["available"] == true;
                    loaded = true;
                    if (!Slider.IsDragging && !previewing || wasSaving)
                    {
                        if (wasSaving) { Slider.Snap(value ?? 1); }
                        else { Slider.Restore(value ?? 1); }
                        previewing = false;
                    }
                    Error = string.Empty;
                    DrawAverage();
                    if (wasSaving) { savedEntry = entry; }
                }
            }
            catch (Exception error)
            {
                if (matches)
                {
                    Error = error is OnlineException ? error.Message : "Could not load or save your rating / try again";
                    if (wasSaving)
                    {
                        previewing = false;
                        Slider.Snap(value ?? 1);
                    }
                }
            }
            request = null;
            saving = false;
        }
        Slider.Tick(Time.unscaledDeltaTime);
        Refresh(local, blocked);
        if (savedEntry != null && !Slider.IsSnapping && !Slider.IsDragging && !previewing && !saving)
        {
            var saved = savedEntry;
            savedEntry = null;
            Saved?.Invoke(saved);
        }
        if (commitQueued && request == null) { Commit(); }
        if (!local || entry == null || !BeatNetClient.IsId(entry.Id) || request != null || Slider.IsDragging || previewing || Plugin.Accounts?.User == null) { return; }
        var next = Plugin.Accounts.Ratings.Read(key,
            () => Plugin.Accounts.Request(new JObject { ["action"] = "ratings", ["key"] = key }));
        if (ReferenceEquals(checkedRequest, next)) { return; }
        requestStamp = stamp;
        requestKey = key;
        requestId = entry.Id;
        request = checkedRequest = next;
    }
}

public sealed class BeatNetRatingSlider : Slider
{
    private readonly BeatNetRatingMotion motion = new();
    internal Action? Released { get; set; }
    internal bool IsDragging { get; private set; }
    internal float DisplayValue => IsDragging ? value : motion.Position;
    internal bool IsSnapping => motion.IsAnimating;

    internal void Restore(float rating)
    {
        SetValueWithoutNotify(rating);
        motion.Reset(value);
    }

    internal void Snap(int rating)
    {
        SetValueWithoutNotify(rating);
        motion.Snap(BeatNetRatingMotion.Nearest(value));
    }

    internal void Tick(float seconds) => motion.Tick(seconds);

    public override void OnPointerDown(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left || !IsInteractable()) { return; }
        IsDragging = true;
        base.OnPointerDown(data);
    }

    public override void OnPointerUp(PointerEventData data)
    {
        base.OnPointerUp(data);
        if (!IsDragging || data.button != PointerEventData.InputButton.Left) { return; }
        IsDragging = false;
        motion.Reset(value);
        Released?.Invoke();
    }

    protected override void OnDisable()
    {
        IsDragging = false;
        motion.Reset(value);
        base.OnDisable();
    }
}
