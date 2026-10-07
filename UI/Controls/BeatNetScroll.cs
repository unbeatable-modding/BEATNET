using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BEATNET;

internal sealed class BeatNetScroll : ScrollRect
{
    private float target;
    private float lastPosition;
    private float wheelTime;
    private float wheelVelocity;
    private bool smoothing;
    private bool dragging;
    private bool updating;

    public override void OnScroll(PointerEventData eventData)
    {
        if (!IsActive() || content == null || viewport == null || dragging)
        {
            return;
        }
        var delta = eventData.scrollDelta;
        var distance = -(Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ? delta.x : delta.y) * scrollSensitivity;
        if (distance == 0f)
        {
            return;
        }
        var position = content.anchoredPosition.y;
        if (!smoothing || Mathf.Abs(position - lastPosition) > 0.01f || distance * (MapPosition(target) - position) < 0f)
        {
            target = MapPosition(position, true);
            wheelVelocity = 0f;
        }
        target += distance;
        if (movementType == MovementType.Clamped)
        {
            target = MapPosition(target);
        }
        lastPosition = position;
        wheelTime = 0f;
        velocity = Vector2.zero;
        smoothing = true;
    }

    protected override void LateUpdate()
    {
        if (smoothing && content != null && viewport != null)
        {
            wheelTime += Time.unscaledDeltaTime;
            var position = content.anchoredPosition;
            if (Mathf.Abs(position.y - lastPosition) > 0.01f)
            {
                StopMovement();
            }
            else
            {
                var maximum = Mathf.Max(0f, content.rect.height - viewport.rect.height);
                if (wheelTime >= 0.12f && (position.y < 0f || position.y > maximum))
                {
                    smoothing = false;
                    velocity = new Vector2(0f, wheelVelocity);
                }
                else
                {
                    var destination = MapPosition(target);
                    var previous = position.y;
                    position.y = Mathf.Lerp(previous, destination, 1f - Mathf.Exp(-Time.unscaledDeltaTime / 0.075f));
                    if (Time.unscaledDeltaTime > 0f)
                    {
                        wheelVelocity = (position.y - previous) / Time.unscaledDeltaTime;
                    }
                    if (Mathf.Abs(position.y - destination) < 0.05f)
                    {
                        position.y = destination;
                        smoothing = false;
                    }
                    velocity = !smoothing && (position.y < 0f || position.y > maximum)
                        ? new Vector2(0f, wheelVelocity) : Vector2.zero;
                    SetContentAnchoredPosition(position);
                }
            }
        }
        var movement = movementType;
        if (smoothing)
        {
            movementType = MovementType.Unrestricted;
        }
        try
        {
            updating = true;
            base.LateUpdate();
        }
        finally
        {
            updating = false;
            movementType = movement;
        }
        RefreshScrollbar();
        if (smoothing && content != null)
        {
            lastPosition = content.anchoredPosition.y;
        }
    }

    private void RefreshScrollbar()
    {
        if (verticalScrollbar == null || content == null || viewport == null)
        {
            return;
        }
        var height = Mathf.Max(1f, content.rect.height);
        var view = Mathf.Max(1f, viewport.rect.height);
        var maximum = Mathf.Max(0f, height - view);
        var position = content.anchoredPosition.y;
        var stretch = Mathf.Abs(position - Mathf.Clamp(position, 0f, maximum));
        var normal = Mathf.Clamp01(view / height);
        var track = verticalScrollbar.handleRect?.parent as RectTransform;
        var minimum = Mathf.Min(normal, 12f / Mathf.Max(12f, track != null ? track.rect.height : view));
        verticalScrollbar.size = Mathf.Max(minimum, normal / (1f + stretch / view));
        verticalScrollbar.SetValueWithoutNotify(maximum > 0f ? Mathf.Clamp01(1f - position / maximum) : 1f);
    }

    private float MapPosition(float position, bool inverse = false)
    {
        var maximum = Mathf.Max(0f, content.rect.height - viewport.rect.height);
        var boundary = Mathf.Clamp(position, 0f, maximum);
        if (movementType == MovementType.Clamped)
        {
            return boundary;
        }
        if (movementType != MovementType.Elastic)
        {
            return position;
        }
        var stretch = position - boundary;
        var size = Mathf.Max(1f, viewport.rect.height);
        var amount = inverse ? size * (Mathf.Exp(Mathf.Abs(stretch) / size) - 1f) / 0.55f
            : size * Mathf.Log(1f + Mathf.Abs(stretch) * 0.55f / size);
        return boundary + Mathf.Sign(stretch) * amount;
    }

    public override void StopMovement()
    {
        smoothing = false;
        wheelVelocity = 0f;
        base.StopMovement();
    }

    public override void OnInitializePotentialDrag(PointerEventData eventData)
    {
        StopMovement();
        base.OnInitializePotentialDrag(eventData);
    }

    public override void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            StopMovement();
            dragging = true;
        }
        base.OnBeginDrag(eventData);
    }

    public override void OnEndDrag(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            dragging = false;
        }
        base.OnEndDrag(eventData);
    }

    protected override void SetNormalizedPosition(float value, int axis)
    {
        if (updating && axis == 1)
        {
            return;
        }
        StopMovement();
        base.SetNormalizedPosition(value, axis);
    }

    protected override void OnDisable()
    {
        StopMovement();
        dragging = false;
        base.OnDisable();
    }
}
