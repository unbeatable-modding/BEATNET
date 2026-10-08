using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Arcade.UI;
using Arcade.UI.YourProfile;
using CrossPlatform;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BEATNET;

internal sealed class BeatNetBpBoard : IDisposable
{
    private const int PageSize = 50;
    private const float RowHeight = 162f;
    private readonly BeatNetUi ui;
    private readonly EventSystem events;
    private readonly GameObject root;
    private readonly TextMeshProUGUI message;
    private readonly TextMeshProUGUI own;
    private readonly TextMeshProUGUI page;
    private readonly Button previous;
    private readonly Button next;
    private readonly List<Button> rows = new();
    private readonly List<LeaderboardScoreDisplay> cards = new();
    private CancellationTokenSource? cancellation;
    private Task? pending;
    private int generation;
    private int offset;
    private int total;
    private int bpChanged = -1;
    private float refreshAfter;
    private string session = string.Empty;

    internal ScrollRect List { get; }
    internal IEnumerable<Selectable> Controls => new Selectable[] { previous, next }.Concat(rows);
    internal bool IsOpen => root.activeSelf;

    internal BeatNetBpBoard(BeatNetUi ui, Transform parent, EventSystem events)
    {
        this.ui = ui;
        this.events = events;
        root = ui.Rect(parent, "Leaderboard", 0f, 0f, 1640f, 940f).gameObject;
        root.SetActive(false);
        ui.Text(root.transform, "Beatpoints leaderboard", 34f, 40f, 214f, 820f, 58f, BeatNetColor.Text, BeatNetFont.Heading);
        own = ui.Text(root.transform, "", 20f, 1060f, 224f, 540f, 40f, BeatNetColor.Text);
        own.alignment = TextAlignmentOptions.Right;
        List = ui.List(root.transform, 40f, 286f, 1560f, 548f);
        message = ui.Text(List.viewport, "", 24f, 40f, 140f, 1480f, 120f, BeatNetColor.Muted);
        message.alignment = TextAlignmentOptions.Center;
        message.textWrappingMode = TextWrappingModes.Normal;
        previous = ui.Button(root.transform, "< Previous", 40f, 852f, 180f, 48f, true);
        previous.onClick.AddListener(() => Load(Math.Max(0, offset - PageSize)));
        next = ui.Button(root.transform, "Next >", 1420f, 852f, 180f, 48f, true);
        next.onClick.AddListener(() => Load(offset + PageSize));
        page = ui.Text(root.transform, "", 18f, 600f, 852f, 440f, 48f, BeatNetColor.Muted);
        page.alignment = TextAlignmentOptions.Center;
    }

    internal void Show()
    {
        root.SetActive(true);
        Plugin.Accounts?.RefreshBp();
        Load(0);
    }

    internal void Hide()
    {
        root.SetActive(false);
        cancellation?.Cancel();
        generation++;
        pending = null;
        List.StopMovement();
    }

    internal void Tick()
    {
        if (!IsOpen) { return; }
        var accounts = Plugin.Accounts;
        var current = (accounts?.UserId ?? string.Empty) + "/" + (accounts?.Key ?? string.Empty);
        if (session != current) { Load(0); }
        else if (pending == null && (bpChanged != (accounts?.BpChanged ?? 0) || Time.unscaledTime >= refreshAfter)) { Load(offset, true); }
    }

    private void Load(int start, bool keepRows = false)
    {
        cancellation?.Cancel();
        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var request = ++generation;
        var accounts = Plugin.Accounts;
        session = (accounts?.UserId ?? string.Empty) + "/" + (accounts?.Key ?? string.Empty);
        bpChanged = accounts?.BpChanged ?? 0;
        refreshAfter = Time.unscaledTime + 30f;
        previous.interactable = next.interactable = false;
        if (!keepRows)
        {
            foreach (var row in rows) { row.gameObject.SetActive(false); }
            message.text = "Loading leaderboard";
            message.gameObject.SetActive(true);
            own.text = page.text = string.Empty;
            List.StopMovement();
            List.content.anchoredPosition = Vector2.zero;
        }
        if (accounts == null) { Fail("BEATNET is not ready"); return; }
        var input = new JObject { ["action"] = "bp_leaderboard", ["offset"] = start, ["limit"] = PageSize };
        if (accounts.User != null) { input["key"] = accounts.Key; }
        accounts.SyncProfile();
        pending = Fetch();
        async Task Fetch()
        {
            try
            {
                var result = await accounts.Request(input, token).ConfigureAwait(false);
                accounts.Post(() =>
                {
                    if (!IsOpen || request != generation) { return; }
                    pending = null;
                    try { Apply(result, start); }
                    catch (Exception) { Fail("Cannot display leaderboard / retrying shortly"); }
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception)
            {
                accounts.Post(() =>
                {
                    if (IsOpen && request == generation) { pending = null; Fail("Cannot load leaderboard / retrying shortly"); }
                });
            }
        }
    }

    private void Apply(JObject result, int start)
    {
        var items = result["items"] as JArray ?? throw new InvalidOperationException("Invalid leaderboard response");
        total = (int)result["total"]!;
        if (items.Count > PageSize || total < 0) { throw new InvalidOperationException("Invalid leaderboard response"); }
        if (items.Count == 0 && total > 0 && start > 0) { Load((total - 1) / PageSize * PageSize); return; }
        offset = start;
        var template = Resources.FindObjectsOfTypeAll<ArcadeLeaderboardView>()
            .Select(view => AccessTools.Field(typeof(ArcadeLeaderboardView), "largeScorePrefab").GetValue(view) as LeaderboardScoreDisplay)
            .FirstOrDefault(card => card != null);
        if (items.Count > 0 && template == null) { throw new InvalidOperationException("Leaderboard profile template is unavailable"); }
        for (var index = 0; index < items.Count; index++)
        {
            if (index >= rows.Count) { AddRow(template!, index); }
            FillRow(index, (JObject)items[index]);
            rows[index].gameObject.SetActive(true);
        }
        for (var index = items.Count; index < rows.Count; index++) { rows[index].gameObject.SetActive(false); }
        List.content.sizeDelta = new Vector2(1560f, Math.Max(548f, items.Count * RowHeight));
        message.text = "No BEATNET players yet";
        message.gameObject.SetActive(items.Count == 0);
        own.text = result["own"] is JObject player ? "No." + (int)player["rank"]! : "Log in to see your rank";
        page.text = $"{(items.Count == 0 ? 0 : offset + 1)}-{offset + items.Count} of {total}";
        previous.interactable = offset > 0;
        next.interactable = offset + items.Count < total;
    }

    private void AddRow(LeaderboardScoreDisplay template, int index)
    {
        var button = ui.Button(List.content, "", 0f, index * RowHeight, 1538f, 150f);
        ((BeatNetControl)button).Sound = BeatNetSound.None;
        button.onClick.AddListener(() => events.SetSelectedGameObject(button.gameObject));
        var card = UnityEngine.Object.Instantiate(template, button.transform, false);
        card.enabled = false;
        BeatNetButton.RemoveMenuBehaviours(card.gameObject);
        foreach (var animator in card.GetComponentsInChildren<Animator>(true)) { animator.enabled = false; }
        foreach (var control in card.GetComponentsInChildren<Selectable>(true)) { control.enabled = false; }
        foreach (var graphic in card.GetComponentsInChildren<Graphic>(true)) { graphic.raycastTarget = false; }
        var rect = (RectTransform)card.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(18f, 0f);
        rect.sizeDelta = new Vector2(1000f, 100f);
        rect.localScale = new Vector3(1.5f, 1.5f, 1f);
        foreach (var path in new[] { "Sections", "Sections/RightSection" })
        {
            var section = card.transform.Find(path);
            var layout = section.GetComponent<LayoutElement>() ?? section.gameObject.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;
        }
        card.gameObject.SetActive(true);
        foreach (var name in new[] { "songClearState", "songFCState", "songPCState" })
        {
            ((GameObject)AccessTools.Field(typeof(LeaderboardScoreDisplay), name).GetValue(card)).SetActive(false);
        }
        var score = Field(card, "songScore");
        ui.Font(score, BeatNetFont.Score);
        ui.Tint(score, BeatNetColor.Text);
        Place(score.rectTransform, rect, 350f, 18f, 540f, 64f);
        score.alignment = TextAlignmentOptions.MidlineRight;
        score.enableAutoSizing = true;
        score.fontSizeMin = 24f;
        score.fontSizeMax = score.fontSize;
        var label = Field(card, "songRank");
        ui.Font(label, BeatNetFont.Rank);
        ui.Tint(label, BeatNetColor.Text);
        Place(label.rectTransform, rect, 912f, 18f, 78f, 64f);
        label.alignment = TextAlignmentOptions.MidlineRight;
        label.text = "BP";
        label.gameObject.SetActive(true);
        ((TextMeshProUGUI)AccessTools.Field(typeof(LeaderboardScoreDisplay), "errorText").GetValue(card)).gameObject.SetActive(false);
        rows.Add(button);
        cards.Add(card);
    }

    private void FillRow(int index, JObject row)
    {
        var id = "beatnet:" + (string)row["userId"]!;
        var points = (double)row["bp"]!;
        if (double.IsNaN(points) || double.IsInfinity(points) || points < 0 || (int)row["rank"]! < 1) { throw new InvalidOperationException("Invalid beatpoints"); }
        BeatNetLeaderboard.Profiles[id] = row;
        var card = cards[index];
        var request = generation;
        var user = new UserId(id, PlatformTypes.Editor);
        PlatformManager.PlayerProfile.GetPlayerProfile(user, profile =>
        {
            if (card == null || request != generation || !IsOpen) { return; }
            card.Fill(profile);
        });
        Field(card, "playerRank").text = "<mspace=0.8em>" + (int)row["rank"]!;
        Field(card, "songScore").text = "<mspace=0.8em>" + BeatNetNumbers.Format(points);
        card.SetLoading(false);
    }

    private static TextMeshProUGUI Field(LeaderboardScoreDisplay card, string name) =>
        (TextMeshProUGUI)AccessTools.Field(typeof(LeaderboardScoreDisplay), name).GetValue(card);

    private static void Place(RectTransform rect, Transform parent, float left, float top, float width, float height)
    {
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
        var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
    }

    private void Fail(string text)
    {
        message.text = text;
        message.gameObject.SetActive(true);
        previous.interactable = offset > 0;
        next.interactable = offset + PageSize < total;
    }

    internal void Navigate(Vector2Int direction, int step, IEnumerable<Selectable> header)
    {
        var choices = header.Concat(Controls).Where(control => control.gameObject.activeInHierarchy && control.interactable).ToList();
        var current = events.currentSelectedGameObject?.GetComponent<Selectable>();
        var points = choices.Select(control =>
        {
            var rect = (RectTransform)control.transform;
            var center = root.transform.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            return new NavigationPoint(center.x, center.y, true);
        }).ToArray();
        var selected = step != 0 ? BeatNetNavigation.Step(points, choices.IndexOf(current!), step)
            : BeatNetNavigation.Find(points, choices.IndexOf(current!), direction.x, direction.y, preferAligned: true);
        if (selected < 0 || choices[selected] == current) { return; }
        BeatNetSounds.Move(direction, step);
        var target = choices[selected];
        events.SetSelectedGameObject(target.gameObject);
        var row = target is Button button ? rows.IndexOf(button) : -1;
        if (row < 0) { return; }
        var top = row * RowHeight;
        var position = List.content.anchoredPosition;
        position.y = Mathf.Clamp(position.y, Mathf.Max(0f, top + 150f - List.viewport.rect.height), top);
        List.StopMovement();
        List.content.anchoredPosition = position;
    }

    public void Dispose()
    {
        Hide();
        cancellation?.Dispose();
        cancellation = null;
    }
}
