using System;
using UnityEngine;

namespace BEATNET;

public sealed class BeatNetTabs : MonoBehaviour
{
    private RectTransform target = null!;
    private CanvasGroup group = null!;
    private Vector2 origin;
    private Action? change;
    private float elapsed;
    private float direction;
    private bool entering;

    internal bool IsMoving { get; private set; }

    internal static BeatNetTabs Create(RectTransform root)
    {
        var tabs = root.gameObject.AddComponent<BeatNetTabs>();
        tabs.target = root;
        tabs.origin = root.anchoredPosition;
        tabs.group = root.gameObject.AddComponent<CanvasGroup>();
        return tabs;
    }

    internal void Switch(bool library, Action changed)
    {
        direction = library ? -1f : 1f;
        change = changed;
        elapsed = 0f;
        entering = false;
        IsMoving = true;
        group.interactable = group.blocksRaycasts = false;
    }

    internal void Reset()
    {
        IsMoving = false;
        change = null;
        target.anchoredPosition = origin;
        group.alpha = 1f;
        group.interactable = group.blocksRaycasts = true;
    }

    private void Update()
    {
        if (!IsMoving)
        {
            return;
        }
        elapsed += Time.unscaledDeltaTime;
        var time = Mathf.Clamp01(elapsed / (entering ? 0.24f : 0.18f));
        var eased = entering ? 1f - Mathf.Pow(1f - time, 3f) : time * time;
        target.anchoredPosition = origin + new Vector2(direction * 1720f * (entering ? eased - 1f : eased), 0f);
        group.alpha = entering ? eased : Mathf.Pow(1f - time, 3f);
        if (time < 1f)
        {
            return;
        }
        if (entering)
        {
            Reset();
            return;
        }
        entering = true;
        elapsed = 0f;
        target.anchoredPosition = origin - new Vector2(direction * 1720f, 0f);
        var changed = change;
        change = null;
        changed?.Invoke();
    }
}
