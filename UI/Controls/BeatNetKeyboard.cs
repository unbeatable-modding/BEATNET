using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BEATNET;

internal sealed class BeatNetKeyboard
{
    private const string Keys = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_'&";
    private const string Symbols = "!@#$%^&*()_+-=[]{};:'\",.<>/?\\|`~01234567";
    private readonly List<Button> keys = new();
    private int keyMode;
    private readonly GameObject root;
    private readonly RectTransform dialog;
    private readonly List<Button> buttons = new();
    private readonly TMP_InputField field;
    private readonly TextMeshProUGUI value;
    private readonly EventSystem events;
    private readonly System.Action submit;
    private readonly System.Action restore;
    private readonly BeatNetMotion motion;

    internal bool IsOpen => root.activeSelf;
    internal bool IsReady => motion.IsReady;

    internal BeatNetKeyboard(BeatNetUi ui, Transform parent, TMP_InputField field, EventSystem events,
        System.Action submit, System.Action restore, string title = "Search", string action = "Search")
    {
        this.field = field;
        this.events = events;
        this.submit = submit;
        this.restore = restore;
        var overlay = ui.Button(parent, "", 0f, 0f, 0f, 0f);
        var overlayRect = (RectTransform)overlay.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.pivot = new Vector2(0.5f, 0.5f);
        overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
        ((BeatNetControl)overlay).Sound = BeatNetSound.None;
        ((BeatNetControl)overlay).HoverSound = BeatNetSound.None;
        root = overlay.gameObject;
        root.name = "Keyboard";
        overlay.transition = Selectable.Transition.None;
        ui.Tint((Image)overlay.targetGraphic, BeatNetColor.Backdrop);
        overlay.onClick.AddListener(Close);
        var panel = ui.Button(root.transform, "", 380f, 190f, 880f, 560f);
        ((BeatNetControl)panel).Sound = BeatNetSound.None;
        ((BeatNetControl)panel).HoverSound = BeatNetSound.None;
        dialog = (RectTransform)panel.transform;
        dialog.pivot = new Vector2(0.5f, 0.5f);
        dialog.anchorMin = dialog.anchorMax = new Vector2(0.5f, 0.5f);
        dialog.anchoredPosition = Vector2.zero;
        panel.transition = Selectable.Transition.None;
        ui.Tint((Image)panel.targetGraphic, BeatNetColor.Background);
        ui.Fill(dialog, "Accent", BeatNetColor.Accent, 0f, 0f, 880f, 3f);
        ui.Text(dialog, title, 36f, 30f, 20f, 820f, 54f, BeatNetColor.Text, BeatNetFont.Heading);
        value = ui.Text(dialog, "", 26f, 30f, 84f, 820f, 54f);
        ui.Fill(dialog, "Underline", BeatNetColor.Line, 30f, 142f, 820f, 2f);
        for (var index = 0; index < Keys.Length; index++)
        {
            var keyIndex = index;
            var key = Keys[index].ToString();
            var button = ui.Button(dialog, key, 30f + index % 10 * 83f, 164f + index / 10 * 62f, 73f, 52f);
            button.onClick.AddListener(() => Edit(field.text + Key(keyIndex)));
            buttons.Add(button);
            keys.Add(button);
        }
        Add(ui, "Space", 30f, 164f, () => Edit(field.text + " "));
        Add(ui, "Delete", 204f, 156f, Delete);
        Add(ui, "Clear", 370f, 156f, () => Edit(""));
        Add(ui, action, 536f, 156f, Submit, true, action == "Search" ? BeatNetSound.None : BeatNetSound.Confirm);
        Add(ui, "Back", 702f, 148f, Close, sound: BeatNetSound.None);
        if (title != "Search")
        {
            var mode = ui.Button(dialog, "ABC / abc / #", 650f, 22f, 200f, 48f);
            mode.onClick.AddListener(() =>
            {
                keyMode = (keyMode + 1) % 3;
                for (var index = 0; index < keys.Count; index++) { keys[index].GetComponentInChildren<TextMeshProUGUI>(true).text = Key(index); }
            });
            buttons.Add(mode);
        }
        motion = BeatNetMotion.Create(root, dialog);
        root.SetActive(false);
    }

    private void Add(BeatNetUi ui, string label, float left, float width, UnityEngine.Events.UnityAction action,
        bool primary = false, BeatNetSound sound = BeatNetSound.Confirm)
    {
        var button = ui.Button(dialog, label, left, 432f, width, 54f, primary);
        ((BeatNetControl)button).Sound = sound;
        button.onClick.AddListener(action);
        buttons.Add(button);
    }

    private string Key(int index) => keyMode == 2 ? Symbols[index].ToString()
        : keyMode == 1 ? Keys[index].ToString().ToLowerInvariant() : Keys[index].ToString();

    private void Edit(string text)
    {
        field.text = text.Length > field.characterLimit ? text.Substring(0, field.characterLimit) : text;
        value.text = field.contentType == TMP_InputField.ContentType.Password ? new string('*', field.text.Length) : field.text;
    }

    private void Delete()
    {
        var text = field.text;
        var length = text.Length;
        if (length == 0) { return; }
        var count = length > 1 && char.IsLowSurrogate(text[length - 1]) && char.IsHighSurrogate(text[length - 2]) ? 2 : 1;
        Edit(text.Substring(0, length - count));
    }

    internal void Open()
    {
        BeatNetSounds.Play(BeatNetSound.Confirm);
        field.DeactivateInputField();
        value.text = field.contentType == TMP_InputField.ContentType.Password ? new string('*', field.text.Length) : field.text;
        motion.Show();
        events.SetSelectedGameObject(buttons[0].gameObject);
    }

    internal void Close()
    {
        if (!IsOpen || motion.IsHiding)
        {
            return;
        }
        BeatNetSounds.Play(BeatNetSound.Back);
        motion.Hide(finished: restore);
    }

    internal void Hide() => motion.Hide(true);

    private void Submit()
    {
        motion.Hide(finished: restore);
        submit();
    }

    internal void Navigate(Vector2Int direction, int step)
    {
        var current = events.currentSelectedGameObject?.GetComponent<Button>();
        var points = buttons.Select(button =>
        {
            var rect = (RectTransform)button.transform;
            var center = dialog.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            return new NavigationPoint(center.x, center.y, true);
        }).ToArray();
        var index = step != 0 ? BeatNetNavigation.Step(points, buttons.IndexOf(current!), step)
            : BeatNetNavigation.Find(points, buttons.IndexOf(current!), direction.x, direction.y);
        if (index >= 0 && buttons[index] != current)
        {
            BeatNetSounds.Move(direction, step);
            events.SetSelectedGameObject(buttons[index].gameObject);
        }
    }
}
