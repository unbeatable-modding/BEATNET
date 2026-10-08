using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BEATNET;

internal sealed class BeatNetFilters
{
    private static readonly string[] Slots = { "Beginner", "Easy", "Normal", "Hard", "UNBEATABLE", "Star" };
    private static readonly (string Id, string Label, string Name)[] Orders =
    {
        ("title", "Alphabetical", "Alphabetical"),
        ("rating", "Rating / Highest first", "Highest rating"),
        ("rating_low", "Rating / Lowest first", "Lowest rating"),
        ("downloads", "Most downloaded", "Most downloads"),
        ("downloads_low", "Least downloaded", "Least downloads"),
    };
    private readonly BeatNetUi ui;
    private readonly EventSystem events;
    private readonly Action changed;
    private readonly GameObject root;
    private readonly RectTransform dialog;
    private readonly TextMeshProUGUI heading;
    private readonly BeatNetMotion motion;
    private readonly BeatNetPerspective perspective;
    private readonly List<Button> choices = new();
    private readonly HashSet<string> draft = new();
    private bool controller;
    private Button origin = null!;

    internal Button Sort { get; }
    internal Button Difficulty { get; }
    internal string Sorting { get; private set; } = "title";
    internal string[] Difficulties { get; private set; } = Array.Empty<string>();
    internal void ClearDifficulties()
    {
        Difficulties = Array.Empty<string>();
        Difficulty.GetComponentInChildren<TextMeshProUGUI>(true).text = "Difficulties / All";
    }
    internal bool IsOpen => root.activeSelf;
    internal bool IsReady => motion.IsReady;

    internal BeatNetFilters(BeatNetUi ui, Transform parent, Transform content, EventSystem events, Action changed)
    {
        this.ui = ui;
        this.events = events;
        this.changed = changed;
        Sort = ui.Button(content, "Sort / Alphabetical", 40f, 282f, 252f, 34f);
        Difficulty = ui.Button(content, "Difficulties / All", 306f, 282f, 344f, 34f);
        foreach (var button in new[] { Sort, Difficulty }) { button.GetComponentInChildren<TextMeshProUGUI>(true).fontSize = 18f; }
        Sort.onClick.AddListener(() => Open(false));
        Difficulty.onClick.AddListener(() => Open(true));
        var overlay = ui.Button(parent, "", 0f, 0f, 0f, 0f);
        ((BeatNetControl)overlay).Sound = BeatNetSound.None;
        overlay.transition = Selectable.Transition.None;
        ui.Tint((Image)overlay.targetGraphic, BeatNetColor.Backdrop);
        var rect = (RectTransform)overlay.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        overlay.onClick.AddListener(Close);
        root = overlay.gameObject;
        root.name = "Filters";
        dialog = ui.Rect(root.transform, "Dialog", 0f, 0f, 660f, 440f);
        dialog.anchorMin = dialog.anchorMax = dialog.pivot = new Vector2(0.5f, 0.5f);
        dialog.anchoredPosition = Vector2.zero;
        var surface = dialog.gameObject.AddComponent<Image>();
        ui.Tint(surface, BeatNetColor.Background);
        var block = dialog.gameObject.AddComponent<Button>();
        block.targetGraphic = surface;
        block.transition = Selectable.Transition.None;
        block.navigation = new Navigation { mode = Navigation.Mode.None };
        ui.Fill(dialog, "Accent", BeatNetColor.Accent, 0f, 0f, 660f, 3f);
        heading = ui.Text(dialog, "", 34f, 30f, 22f, 600f, 64f, BeatNetColor.Text, BeatNetFont.Heading);
        perspective = new BeatNetPerspective(dialog.gameObject.AddComponent<Arcade.UI.CanvasMousePerspective>());
        motion = BeatNetMotion.Create(root, dialog, new Vector2(-160f, 160f));
        root.SetActive(false);
    }

    internal void SetController(bool active) => controller = active;
    internal void Tick() { if (IsOpen) { perspective.Tick(); } }

    private void Open(bool difficulties)
    {
        if (!Sort.interactable || !Difficulty.interactable) { return; }
        origin = difficulties ? Difficulty : Sort;
        draft.Clear();
        draft.UnionWith(Difficulties);
        foreach (var button in choices) { UnityEngine.Object.Destroy(button.gameObject); }
        choices.Clear();
        heading.text = difficulties ? "Filter difficulties" : "Sort beatmaps";
        if (difficulties)
        {
            Add("", 30f, 100f, 600f, () => { draft.Clear(); DrawChoices(); });
            for (var index = 0; index < Slots.Length; index++)
            {
                var slot = Slots[index];
                Add("", 30f + index % 2 * 308f, 160f + index / 2 * 60f, 292f, () =>
                {
                    if (!draft.Add(slot)) { draft.Remove(slot); }
                    DrawChoices();
                });
            }
            Add("Apply", 30f, 354f, 600f, () =>
            {
                Difficulties = Slots.Where(draft.Contains).ToArray();
                Difficulty.GetComponentInChildren<TextMeshProUGUI>(true).text = Difficulties.Length == 0 ? "Difficulties / All"
                    : "Difficulties / " + (Difficulties.Length == 1 ? BeatNetScoreText.Difficulty(Difficulties[0].ToLowerInvariant()) : Difficulties.Length + " selected");
                Hide();
                changed();
            }, primary: true);
            DrawChoices();
        }
        else
        {
            for (var index = 0; index < Orders.Length; index++)
            {
                var order = Orders[index];
                Add(order.Label, 30f, 100f + index * 60f, 600f, () => SetSort(order.Id));
            }
            ui.Style(choices[Math.Max(0, Array.FindIndex(Orders, order => order.Id == Sorting))], selected: true);
        }
        Add("Back", 30f, 406f, 600f, Close, 40f, BeatNetSound.None, primary: true);
        dialog.sizeDelta = new Vector2(660f, 466f);
        perspective.Set(3.4f);
        motion.Show();
        events.SetSelectedGameObject(controller ? choices[0].gameObject : null);
    }

    private void Add(string text, float left, float top, float width, Action action, float height = 48f, BeatNetSound sound = BeatNetSound.Confirm, bool primary = false)
    {
        var button = ui.Button(dialog, text, left, top, width, height, primary);
        ((BeatNetControl)button).Sound = sound;
        button.onClick.AddListener(() => action());
        choices.Add(button);
    }

    private void DrawChoices()
    {
        choices[0].GetComponentInChildren<TextMeshProUGUI>(true).text = (draft.Count == 0 ? "[x] " : "[ ] ") + "All";
        ui.Style(choices[0], primary: draft.Count == 0);
        for (var index = 0; index < Slots.Length; index++)
        {
            var selected = draft.Contains(Slots[index]);
            choices[index + 1].GetComponentInChildren<TextMeshProUGUI>(true).text = (selected ? "[x] " : "[ ] ") + BeatNetScoreText.Difficulty(Slots[index].ToLowerInvariant());
            ui.Style(choices[index + 1], primary: selected);
        }
    }

    private void SetSort(string sorting)
    {
        Sorting = sorting;
        Sort.GetComponentInChildren<TextMeshProUGUI>(true).text = "Sort / " + Orders.First(order => order.Id == sorting).Name;
        Hide();
        changed();
    }

    internal void Close()
    {
        if (!IsOpen || motion.IsHiding) { return; }
        BeatNetSounds.Play(BeatNetSound.Back);
        motion.Hide(finished: () => events.SetSelectedGameObject(controller ? origin.gameObject : null));
    }

    internal void Hide()
    {
        motion.Hide(true);
        events.SetSelectedGameObject(null);
    }

    internal void Navigate(Vector2Int direction, int step)
    {
        var current = events.currentSelectedGameObject?.GetComponent<Button>();
        var points = choices.Select(control =>
        {
            var rect = (RectTransform)control.transform;
            var center = dialog.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            return new NavigationPoint(center.x, center.y, control.interactable);
        }).ToArray();
        var index = choices.IndexOf(current!);
        var next = step != 0 ? BeatNetNavigation.Step(points, index, step)
            : BeatNetNavigation.Find(points, index, direction.x, direction.y, preferAligned: true);
        if (next < 0 || choices[next] == current) { return; }
        BeatNetSounds.Move(direction, step);
        events.SetSelectedGameObject(choices[next].gameObject);
    }
}
