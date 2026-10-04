using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BEATNET;

public sealed class BeatNetControl : Button
{
    private bool selected;
    private bool pointerInside;

    internal bool Controller { get; set; }
    internal bool HoverOnly { get; set; } = true;
    internal Animator? NativeAnimator { get; set; }
    internal TextMeshProUGUI? Label { get; set; }
    internal Color NormalText { get; set; }
    internal Color HighlightText { get; set; }
    internal Color DisabledText { get; set; }
    internal BeatNetSound Sound { get; set; } = BeatNetSound.Confirm;
    internal BeatNetSound HoverSound { get; set; } = BeatNetSound.None;

    internal void Refresh() => DoStateTransition(currentSelectionState, false);

    protected override void DoStateTransition(SelectionState state, bool instant)
    {
        if (Controller && state != SelectionState.Disabled)
        {
            state = selected ? SelectionState.Highlighted : SelectionState.Normal;
        }
        else if (HoverOnly && state == SelectionState.Selected)
        {
            state = pointerInside ? SelectionState.Highlighted : SelectionState.Normal;
        }
        base.DoStateTransition(state, instant);
        if (Label != null)
        {
            Label.color = state == SelectionState.Disabled ? DisabledText
                : state == SelectionState.Normal ? NormalText : HighlightText;
        }
        if (NativeAnimator != null)
        {
            NativeAnimator.SetBool("Selected", state == SelectionState.Highlighted || state == SelectionState.Selected || state == SelectionState.Pressed);
        }
    }

    public override void OnPointerEnter(PointerEventData data)
    {
        if (!pointerInside && !Controller && IsInteractable())
        {
            BeatNetSounds.Play(HoverSound);
        }
        pointerInside = true;
        base.OnPointerEnter(data);
    }

    public override void OnPointerExit(PointerEventData data)
    {
        pointerInside = false;
        base.OnPointerExit(data);
    }

    public override void OnSelect(BaseEventData data)
    {
        selected = true;
        base.OnSelect(data);
    }

    public override void OnDeselect(BaseEventData data)
    {
        selected = false;
        base.OnDeselect(data);
    }

    public override void OnPointerClick(PointerEventData data)
    {
        if (data.button == PointerEventData.InputButton.Left && IsActive() && IsInteractable())
        {
            BeatNetSounds.Play(Sound);
        }
        base.OnPointerClick(data);
    }

    public override void OnSubmit(BaseEventData data)
    {
        if (IsActive() && IsInteractable())
        {
            BeatNetSounds.Play(Sound);
        }
        base.OnSubmit(data);
    }

    protected override void OnEnable()
    {
        selected = EventSystem.current?.currentSelectedGameObject == gameObject;
        base.OnEnable();
    }

    protected override void OnDisable()
    {
        selected = false;
        pointerInside = false;
        base.OnDisable();
    }
}
