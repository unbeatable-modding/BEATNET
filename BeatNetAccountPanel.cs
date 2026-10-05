using System;
using System.Collections.Generic;
using System.Linq;
using Arcade.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BEATNET;

internal sealed class BeatNetAccountPanel
{
    private readonly GameObject root;
    private readonly RectTransform dialog;
    private readonly BeatNetMotion motion;
    private readonly BeatNetPerspective perspective;
    private readonly EventSystem events;
    private readonly TextMeshProUGUI state;
    private readonly TextMeshProUGUI message;
    private readonly TMP_InputField username;
    private readonly TMP_InputField password;
    private readonly TMP_InputField confirmation;
    private readonly TextMeshProUGUI confirmLabel;
    private readonly Button loginTab;
    private readonly Button createTab;
    private readonly Button submit;
    private readonly Button logout;
    private readonly Button back;
    private readonly BeatNetUi ui;
    private readonly List<Selectable> controls = new();
    private readonly Dictionary<TMP_InputField, BeatNetKeyboard> keyboards = new();
    private BeatNetKeyboard? keyboard;
    private bool register;
    private string validation = string.Empty;

    internal Button Manage { get; }
    internal bool IsOpen => root.activeSelf;
    internal bool IsReady => motion.IsReady && (keyboard == null || !keyboard.IsOpen || keyboard.IsReady);
    internal bool IsTyping => username.isFocused || password.isFocused || confirmation.isFocused || keyboard?.IsOpen == true;

    internal BeatNetAccountPanel(BeatNetUi ui, Transform parent, Transform window, EventSystem events)
    {
        this.ui = ui;
        this.events = events;
        state = ui.Text(window, "Not logged in", 19f, 1140f, 34f, 400f, 42f);
        state.richText = false;
        state.alignment = TextAlignmentOptions.Right;
        Manage = ui.Button(window, "Manage", 1320f, 84f, 220f, 48f);
        Manage.onClick.AddListener(Open);
        var overlay = ui.Button(parent, "", 0f, 0f, 0f, 0f);
        var overlayRect = (RectTransform)overlay.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.pivot = new Vector2(0.5f, 0.5f);
        overlayRect.offsetMin = overlayRect.offsetMax = Vector2.zero;
        ((BeatNetControl)overlay).Sound = BeatNetSound.None;
        ((BeatNetControl)overlay).HoverSound = BeatNetSound.None;
        overlay.transition = Selectable.Transition.None;
        ui.Tint((Image)overlay.targetGraphic, BeatNetColor.Backdrop);
        root = overlay.gameObject;
        root.name = "Account";
        overlay.onClick.AddListener(Close);
        dialog = ui.Rect(root.transform, "Dialog", 0f, 0f, 570f, 650f);
        dialog.pivot = new Vector2(0.5f, 0.5f);
        dialog.anchorMin = dialog.anchorMax = new Vector2(0.5f, 0.5f);
        dialog.anchoredPosition = Vector2.zero;
        perspective = new BeatNetPerspective(dialog.gameObject.AddComponent<CanvasMousePerspective>());
        perspective.Set(3.4f);
        var image = dialog.gameObject.AddComponent<Image>();
        ui.Tint(image, BeatNetColor.Background);
        var surface = dialog.gameObject.AddComponent<Button>();
        surface.targetGraphic = image;
        surface.transition = Selectable.Transition.None;
        surface.navigation = new Navigation { mode = Navigation.Mode.None };
        ui.Fill(dialog, "Accent", BeatNetColor.Accent, 0f, 0f, 570f, 3f);
        ui.Text(dialog, "Manage account", 34f, 30f, 20f, 510f, 60f, BeatNetColor.Text, BeatNetFont.Heading);
        loginTab = ui.Button(dialog, "Log in", 30f, 92f, 247f, 48f, true);
        createTab = ui.Button(dialog, "Create account", 293f, 92f, 247f, 48f);
        loginTab.onClick.AddListener(() => SetMode(false));
        createTab.onClick.AddListener(() => SetMode(true));
        ui.Text(dialog, "Username / up to 32 characters", 18f, 30f, 158f, 510f, 30f);
        username = ui.Search(dialog, 30f, 192f, 510f, 48f);
        username.characterLimit = 64;
        ((TextMeshProUGUI)username.placeholder).text = "Username";
        username.textComponent.richText = false;
        ui.Text(dialog, "Password / 8 to 256 characters", 18f, 30f, 252f, 510f, 30f);
        password = ui.Search(dialog, 30f, 286f, 510f, 48f);
        password.contentType = TMP_InputField.ContentType.Password;
        password.characterLimit = 256;
        ((TextMeshProUGUI)password.placeholder).text = "Password";
        confirmLabel = ui.Text(dialog, "Confirm password", 18f, 30f, 346f, 510f, 30f);
        confirmation = ui.Search(dialog, 30f, 380f, 510f, 48f);
        confirmation.contentType = TMP_InputField.ContentType.Password;
        confirmation.characterLimit = 256;
        ((TextMeshProUGUI)confirmation.placeholder).text = "Confirm password";
        message = ui.Text(dialog, "", 18f, 30f, 440f, 510f, 72f, BeatNetColor.Muted);
        message.textWrappingMode = TextWrappingModes.Normal;
        message.richText = false;
        submit = ui.Button(dialog, "Log in", 30f, 526f, 247f, 48f, true);
        logout = ui.Button(dialog, "Log out", 293f, 526f, 247f, 48f);
        back = ui.Button(dialog, "Back", 30f, 592f, 510f, 40f);
        submit.onClick.AddListener(Submit);
        logout.onClick.AddListener(() => { validation = string.Empty; Plugin.Accounts?.Logout(); });
        back.onClick.AddListener(Close);
        controls.AddRange(new Selectable[] { loginTab, createTab, username, password, confirmation, submit, logout, back });
        motion = BeatNetMotion.Create(root, dialog, new Vector2(160f, 160f));
        foreach (var field in new[] { username, password, confirmation })
        {
            var captured = field;
            keyboards[field] = new BeatNetKeyboard(ui, root.transform, field, events,
                () => events.SetSelectedGameObject(captured.gameObject), () => events.SetSelectedGameObject(captured.gameObject),
                field == username ? "Username" : "Password", "Done");
        }
        SetMode(false);
        root.SetActive(false);
    }

    private void SetMode(bool create)
    {
        register = create;
        validation = string.Empty;
        password.text = confirmation.text = string.Empty;
        password.contentType = confirmation.contentType = create
            ? TMP_InputField.ContentType.Standard : TMP_InputField.ContentType.Password;
        ui.Style(loginTab, !create);
        ui.Style(createTab, create);
        confirmation.gameObject.SetActive(create);
        confirmLabel.gameObject.SetActive(create);
        submit.GetComponentInChildren<TextMeshProUGUI>(true).text = create ? "Create account" : "Log in";
        username.text = create ? LimitName(BeatNetAccounts.SteamName()) : string.Empty;
    }

    private static string LimitName(string value)
    {
        var count = 0;
        var length = 0;
        while (length < value.Length && count < 32)
        {
            length += char.IsHighSurrogate(value[length]) && length + 1 < value.Length && char.IsLowSurrogate(value[length + 1]) ? 2 : 1;
            count++;
        }
        return value.Substring(0, length);
    }

    private void Open()
    {
        validation = string.Empty;
        SetMode(false);
        motion.Show();
        events.SetSelectedGameObject(null);
    }

    internal void Close()
    {
        if (keyboard?.IsOpen == true) { keyboard.Close(); return; }
        if (!IsOpen || motion.IsHiding) { return; }
        username.DeactivateInputField();
        password.DeactivateInputField();
        confirmation.DeactivateInputField();
        motion.Hide(finished: () =>
        {
            password.text = confirmation.text = string.Empty;
            events.SetSelectedGameObject(Manage.gameObject);
        });
    }

    internal void Hide()
    {
        foreach (var item in keyboards.Values) { item.Hide(); }
        keyboard = null;
        username.DeactivateInputField();
        password.DeactivateInputField();
        confirmation.DeactivateInputField();
        password.text = confirmation.text = string.Empty;
        motion.Hide(true);
    }

    private void Submit()
    {
        validation = string.Empty;
        if (register && password.text != confirmation.text) { validation = "Passwords do not match"; return; }
        Plugin.Accounts?.Login(username.text, password.text, register);
        password.text = confirmation.text = string.Empty;
        events.SetSelectedGameObject(submit.gameObject);
    }

    internal void Tick()
    {
        var accounts = Plugin.Accounts;
        state.text = accounts?.State ?? "Not logged in";
        if (!IsOpen) { return; }
        perspective.Tick();
        message.text = validation.Length > 0 ? validation : accounts?.Error.Length > 0 ? accounts.Error
            : accounts?.Busy == true ? "Connecting account" : accounts?.User != null ? accounts.State : string.Empty;
        submit.interactable = accounts != null && !accounts.Busy;
        logout.interactable = accounts?.User != null && !accounts.Busy;
        loginTab.interactable = createTab.interactable = accounts?.Busy != true;
        username.interactable = password.interactable = confirmation.interactable = accounts?.Busy != true;
    }

    internal void Navigate(Vector2Int direction, int step, bool controller = false)
    {
        if (keyboard?.IsOpen == true) { keyboard.Navigate(direction, step); return; }
        if (IsTyping && step == 0 && !controller) { return; }
        var choices = controls.Where(c => c.gameObject.activeInHierarchy && c.interactable).ToList();
        var current = events.currentSelectedGameObject?.GetComponent<Selectable>();
        var index = choices.IndexOf(current!);
        var next = step != 0 ? (index + step + choices.Count) % choices.Count
            : BeatNetNavigation.Find(choices.Select(c =>
            {
                var rect = (RectTransform)c.transform;
                var center = dialog.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
                return new NavigationPoint(center.x, center.y, true);
            }).ToArray(), index, direction.x, direction.y);
        if (next < 0 || choices[next] == current) { return; }
        username.DeactivateInputField();
        password.DeactivateInputField();
        confirmation.DeactivateInputField();
        events.SetSelectedGameObject(choices[next].gameObject);
    }

    internal void Activate(bool controller)
    {
        var selected = events.currentSelectedGameObject?.GetComponent<Selectable>();
        if (selected is Button button && button.interactable) { button.OnSubmit(new BaseEventData(events)); }
        else if (selected is TMP_InputField field && field.interactable)
        {
            if (controller) { keyboard = keyboards[field]; keyboard.Open(); }
            else if (field.isFocused) { field.DeactivateInputField(); events.SetSelectedGameObject(submit.gameObject); }
            else { field.ActivateInputField(); }
        }
    }
}
