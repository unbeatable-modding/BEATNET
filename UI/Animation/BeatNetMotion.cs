using UnityEngine;

namespace BEATNET;

public sealed class BeatNetMotion : MonoBehaviour
{
    private RectTransform target = null!;
    private CanvasGroup group = null!;
    private Vector2 origin;
    private float value;
    private float from;
    private float destination;
    private float elapsed;
    private bool moving;
    private System.Action? hidden;
    private Vector2 offset;
    private System.Action<bool>? visibility;

    internal bool IsHiding => moving && destination == 0f;
    internal bool IsReady => !moving && destination == 1f;

    internal static BeatNetMotion Create(GameObject root, RectTransform target, Vector2? offset = null, System.Action<bool>? visibility = null)
    {
        var motion = root.AddComponent<BeatNetMotion>();
        motion.target = target;
        motion.origin = target.anchoredPosition;
        motion.offset = offset ?? new Vector2(0f, -64f);
        motion.visibility = visibility;
        motion.group = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
        return motion;
    }

    internal void Show()
    {
        hidden = null;
        value = 0f;
        SetVisible(true);
        Begin(1f);
        Apply();
    }

    internal void Hide(bool immediate = false, System.Action? finished = null)
    {
        if (immediate)
        {
            moving = false;
            hidden = null;
            SetVisible(false);
            finished?.Invoke();
        }
        else if (!IsHiding && gameObject.activeSelf)
        {
            hidden = finished;
            Begin(0f);
        }
    }

    private void Begin(float end)
    {
        from = value;
        destination = end;
        elapsed = 0f;
        moving = true;
        group.interactable = false;
        group.blocksRaycasts = false;
    }

    private void Update()
    {
        if (!moving)
        {
            return;
        }
        elapsed += Time.unscaledDeltaTime;
        var time = Mathf.Clamp01(elapsed / (destination == 1f ? 0.28f : 0.2f));
        var eased = destination == 1f ? 1f - Mathf.Pow(1f - time, 3f) : time * time * time;
        value = Mathf.Lerp(from, destination, eased);
        Apply();
        if (time < 1f)
        {
            return;
        }
        moving = false;
        group.interactable = destination == 1f;
        group.blocksRaycasts = destination == 1f;
        if (destination == 0f)
        {
            var finished = hidden;
            hidden = null;
            SetVisible(false);
            finished?.Invoke();
        }
    }

    private void Apply()
    {
        group.alpha = value;
        target.anchoredPosition = origin + offset * (1f - value);
        target.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, value);
    }

    private void SetVisible(bool visible)
    {
        if (visibility != null)
        {
            visibility(visible);
        }
        else
        {
            gameObject.SetActive(visible);
        }
    }
}
