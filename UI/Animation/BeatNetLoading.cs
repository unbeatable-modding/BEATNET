using UnityEngine;

namespace BEATNET;

public sealed class BeatNetLoading : MonoBehaviour
{
    private CanvasGroup group = null!;
    private bool loading;
    private float elapsed;

    internal static BeatNetLoading Create(RectTransform root)
    {
        var loading = root.gameObject.AddComponent<BeatNetLoading>();
        loading.group = root.gameObject.AddComponent<CanvasGroup>();
        loading.group.interactable = false;
        loading.group.blocksRaycasts = false;
        root.gameObject.SetActive(false);
        return loading;
    }

    internal void Show()
    {
        loading = true;
        elapsed = 0f;
        group.alpha = 0f;
        gameObject.SetActive(true);
    }

    internal void Hide() => loading = false;

    private void Update()
    {
        elapsed += Time.unscaledDeltaTime;
        var target = loading && elapsed > 0.5f ? 0.85f + 0.15f * Mathf.Cos((elapsed - 0.5f) * 4f) : 0f;
        group.alpha = Mathf.MoveTowards(group.alpha, target, Time.unscaledDeltaTime / 0.2f);
        if (!loading && group.alpha == 0f)
        {
            gameObject.SetActive(false);
        }
    }
}
