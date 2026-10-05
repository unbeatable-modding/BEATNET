using Arcade.UI;
using Arcade.UI.MenuStates;
using Rewired;
using TMPro;
using UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BEATNET;

public sealed class BeatNetButton : MonoBehaviour
{
    private Button? shortcut;
    private BeatNetPanel? panel;
    private RectTransform? highscoreRect;
    private TextMeshProUGUI? highscoreLabel;
    private TextMeshProUGUI? shortcutLabel;
    private TextMeshProUGUI? keyLabel;
    private CanvasGroup? highscoreFade;
    private CanvasGroup? shortcutFade;
    private Vector2 highscoreSize;

    private bool IsMainSongSelect => ArcadeMenuStateMachine.Instance?.CurrentState?.StateName == EArcadeMenuStates.SongSelect
        && gameObject.activeInHierarchy;

    internal void Initialize(Button template, MonoBehaviour runner)
    {
        var label = template.transform.Find("ButtonText").GetComponent<TextMeshProUGUI>();
        highscoreRect = (RectTransform)template.transform;
        highscoreLabel = label;
        highscoreFade = template.GetComponent<CanvasGroup>();
        highscoreSize = highscoreRect.sizeDelta;
        var staging = new GameObject("BEATNET.Staging");
        staging.SetActive(false);
        GameObject? clone = null;
        try
        {
            clone = Instantiate(template.gameObject, staging.transform, false);
            clone.SetActive(false);
            clone.name = "BEATNET.Shortcut";
            RemoveMenuBehaviours(clone);
            foreach (var selectable in clone.GetComponentsInChildren<AnimatedSelectable>(true))
            {
                BeatNetSounds.Mute(selectable);
            }
            clone.AddComponent<BeatNetFocusSound>();

            shortcut = clone.GetComponent<Button>();
            shortcutFade = clone.GetComponent<CanvasGroup>();
            shortcut.onClick = new Button.ButtonClickedEvent();
            shortcut.navigation = new Navigation { mode = Navigation.Mode.None };
            keyLabel = clone.transform.Find("KeyName").GetComponentInChildren<TextMeshProUGUI>(true);
            keyLabel.text = "2";
            shortcutLabel = clone.transform.Find("ButtonText").GetComponent<TextMeshProUGUI>();
            shortcutLabel.text = "<cspace=0.15em>Open BEATNET.";
            clone.transform.SetParent(transform, false);
            var background = ArcadeMenuStateMachine.Instance.transform.Find("Background").GetComponent<Graphic>();
            var back = transform.Find("BackButton").GetComponent<Button>();
            var number = ArcadeMenuStateMachine.Instance.transform.Find("ScreenArea/RecurentElements/CornerClock/MenuIndex").GetComponent<TextMeshProUGUI>();
            panel = BeatNetPanel.Create(transform, label, background, back, number);
            runner.StartCoroutine(panel.Prepare(() => IsMainSongSelect));
            shortcut.onClick.AddListener(Open);
            clone.SetActive(true);
            LateUpdate();
        }
        catch
        {
            if (clone != null)
            {
                Destroy(clone);
            }

            throw;
        }
        finally
        {
            Destroy(staging);
        }
    }

    internal static void RemoveMenuBehaviours(GameObject button)
    {
        foreach (var behaviour in button.GetComponentsInChildren<MonoBehaviour>(true))
        {
            var typeName = behaviour.GetType().Name;
            if (behaviour is UIFocusOnButton
                || behaviour is ArcadeMenuStateButton
                || behaviour is CustomUINavigation
                || behaviour is KeybindTextReplace
                || typeName == "LocalizeStringEvent"
                || typeName == "LocalizedFont")
            {
                DestroyImmediate(behaviour);
            }
        }
    }

    private void LateUpdate()
    {
        if (highscoreRect == null || shortcut == null)
        {
            return;
        }

        UpdatePrompt();
        if (IsMainSongSelect && panel?.KeepCursor == true && !panel.IsOpen && !JeffBezosController.usingJoystick)
        {
            if (!Cursor.visible || Cursor.lockState != CursorLockMode.None)
            {
                panel.RestoreCursor();
            }
        }
        shortcut.gameObject.SetActive(highscoreRect.gameObject.activeSelf);
        if (highscoreFade != null && shortcutFade != null)
        {
            shortcutFade.alpha = highscoreFade.alpha;
            shortcutFade.interactable = IsMainSongSelect && highscoreFade.interactable;
            shortcutFade.blocksRaycasts = IsMainSongSelect && highscoreFade.blocksRaycasts && highscoreFade.alpha > 0f;
        }

        var rect = (RectTransform)shortcut.transform;
        if (highscoreLabel != null && shortcutLabel != null && IsMainSongSelect)
        {
            FitButton(highscoreRect, highscoreLabel);
            FitButton(rect, shortcutLabel);
        }

        rect.anchoredPosition = highscoreRect.anchoredPosition + new Vector2(highscoreRect.rect.width + 8f, 0f);
    }

    private static void FitButton(RectTransform rect, TextMeshProUGUI label)
    {
        var textRect = label.rectTransform;
        var textWidth = label.GetPreferredValues(float.PositiveInfinity, float.PositiveInfinity).x * Mathf.Abs(textRect.localScale.x);
        var width = Mathf.Ceil(textRect.offsetMin.x + textWidth - textRect.offsetMax.x);
        if (!Mathf.Approximately(rect.rect.width, width))
        {
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        }
    }

    internal void HandleInput()
    {
        if (panel == null || shortcut == null)
        {
            return;
        }

        var controllerPressed = ReadControllerInput();
        var keyPressed = !panel.IsTyping && OpenKeyPressed();
        if (IsMainSongSelect && (keyPressed || UnityEngine.Input.GetKeyDown(KeyCode.Escape)))
        {
            JeffBezosController.currentControllerType = ControllerType.Keyboard;
            JeffBezosController.currentControllerId = 0;
            panel.SetController(false);
        }

        var openPressed = controllerPressed || keyPressed;
        if (!panel.IsOpen && panel.RestoreInput())
        {
            return;
        }

        if (panel.IsOpen)
        {
            if (!IsMainSongSelect || openPressed)
            {
                panel.Close(!IsMainSongSelect);
            }
            else if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                panel.Back();
            }
            else
            {
                panel.HandleNavigation();
            }

            return;
        }

        if (!IsMainSongSelect || JeffBezosController.instance == null || !JeffBezosController.instance.UIInputEnabled)
        {
            return;
        }

        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected != null && selected.transform.IsChildOf(transform) && openPressed)
        {
            Open();
        }
    }

    private static bool ReadControllerInput()
    {
        if (!ReInput.isReady)
        {
            return false;
        }

        var pressed = false;
        foreach (var joystick in ReInput.players.GetPlayer(0).controllers.Joysticks)
        {
            var trigger = joystick.GetTemplate<IGamepadTemplate>()?.rightTrigger;
            if (trigger == null || !trigger.exists)
            {
                continue;
            }

            if (trigger.AsButton.justPressed)
            {
                pressed = true;
                JeffBezosController.currentControllerType = ControllerType.Joystick;
                JeffBezosController.currentControllerId = joystick.id;
            }
        }

        return pressed;
    }

    private void UpdatePrompt()
    {
        var active = ReInput.isReady && JeffBezosController.usingJoystick
            && ReInput.players.GetPlayer(0).controllers.GetController(ControllerType.Joystick, JeffBezosController.currentControllerId) is Joystick joystick
            && joystick.GetTemplate<IGamepadTemplate>()?.rightTrigger?.exists == true;
        var key = active ? "RT" : "2";
        if (keyLabel != null && keyLabel.text != key)
        {
            keyLabel.text = key;
        }

        panel?.SetController(active);
    }

    private void Open()
    {
        if (IsMainSongSelect)
        {
            panel?.Show();
        }
    }

    internal bool OpenUpdate(string id)
    {
        if (!IsMainSongSelect || panel == null) { return false; }
        panel.RestoreInput();
        return panel.ShowLibrary(id);
    }

    private static bool OpenKeyPressed() => UnityEngine.Input.GetKeyDown(KeyCode.Alpha2) || UnityEngine.Input.GetKeyDown(KeyCode.Keypad2);

    private void OnDisable()
    {
        if (panel != null)
        {
            panel.Close(true);
        }
    }

    private void OnDestroy()
    {
        if (highscoreRect != null)
        {
            highscoreRect.sizeDelta = highscoreSize;
        }

        if (shortcut != null)
        {
            Destroy(shortcut.gameObject);
        }

        if (panel != null)
        {
            panel.Close(true);
            panel.RestoreInput(true);
            Destroy(panel.gameObject);
        }
    }
}
