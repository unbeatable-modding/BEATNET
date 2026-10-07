using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BEATNET;

public sealed class BeatNetInput : BaseInput
{
    public override bool mousePresent => true;
    public override bool touchSupported => false;
    public override int touchCount => 0;
    public override Vector2 mousePosition => ReInput.isReady ? ReInput.controllers.Mouse.screenPosition : UnityEngine.Input.mousePosition;
    public override Vector2 mouseScrollDelta => UnityEngine.Input.mouseScrollDelta;
    public override bool GetMouseButtonDown(int button) => UnityEngine.Input.GetMouseButtonDown(button)
        || ReInput.isReady && ReInput.controllers.Mouse.GetButtonDown(button);
    public override bool GetMouseButtonUp(int button) => !GetMouseButton(button)
        && (UnityEngine.Input.GetMouseButtonUp(button) || ReInput.isReady && ReInput.controllers.Mouse.GetButtonUp(button));
    public override bool GetMouseButton(int button) => UnityEngine.Input.GetMouseButton(button)
        || ReInput.isReady && ReInput.controllers.Mouse.GetButton(button);
    public override float GetAxisRaw(string axisName) => 0f;
    public override bool GetButtonDown(string buttonName) => false;
}

public sealed class BeatNetInputModule : StandaloneInputModule
{
    private GraphicRaycaster raycaster = null!;
    private int processedFrame = -1;

    public override bool ShouldActivateModule() => isActiveAndEnabled && eventSystem.isActiveAndEnabled;

    protected override void OnEnable()
    {
        base.OnEnable();
        raycaster = GetComponent<GraphicRaycaster>();
        processedFrame = -1;
    }

    public override void Process()
    {
    }

    internal void ProcessPointer()
    {
        if (!isActiveAndEnabled || !Application.isFocused || processedFrame == Time.frameCount)
        {
            return;
        }
        processedFrame = Time.frameCount;
        Cursor.lockState = CursorLockMode.None;
        EventSystem.current = eventSystem;
        base.Process();
    }

    protected override MouseState GetMousePointerEventData(int id)
    {
        var state = base.GetMousePointerEventData(id);
        var left = state.GetButtonState(PointerEventData.InputButton.Left).eventData.buttonData;
        m_RaycastResultCache.Clear();
        raycaster.Raycast(left, m_RaycastResultCache);
        var hit = FindFirstRaycast(m_RaycastResultCache);
        m_RaycastResultCache.Clear();
        left.pointerCurrentRaycast = hit;
        state.GetButtonState(PointerEventData.InputButton.Right).eventData.buttonData.pointerCurrentRaycast = hit;
        state.GetButtonState(PointerEventData.InputButton.Middle).eventData.buttonData.pointerCurrentRaycast = hit;
        return state;
    }
}
