using UnityEngine;

namespace BEATNET;

public sealed class BeatNetFade : MonoBehaviour
{
    private CanvasGroup group = null!;
    internal bool IsVisible => enabled || group.alpha > 0f;

    internal static BeatNetFade Create(GameObject root)
    {
        var fade = root.AddComponent<BeatNetFade>();
        fade.group = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
        return fade;
    }

    internal void Clear()
    {
        group.alpha = 0f;
        enabled = false;
    }

    internal void Show()
    {
        group.alpha = 0f;
        enabled = true;
    }

    private void Update()
    {
        group.alpha = Mathf.MoveTowards(group.alpha, 1f, Time.unscaledDeltaTime / 0.22f);
        if (group.alpha == 1f)
        {
            enabled = false;
        }
    }
}
