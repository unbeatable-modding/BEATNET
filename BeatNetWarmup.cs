using System;
using System.Collections;
using System.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BEATNET;

internal static class BeatNetWarmup
{
    //all this does is fix a tiny lag that happened when you opened the BEATNET panel for the first time.. I HATE LAGS SO MUCH THIS IS TOO MUCH WORK TO FIX A 2MS LAG
    internal static IEnumerator Run(GameObject root, CanvasGroup group, Action ready, bool keepActive = false)
    {
        var nodes = root.GetComponentsInChildren<Transform>(true);
        var active = new bool[nodes.Length];
        var behaviours = root.GetComponentsInChildren<Behaviour>(true);
        var enabled = new bool[behaviours.Length];
        var alpha = group.alpha;
        var interactable = group.interactable;
        var raycasts = group.blocksRaycasts;
        for (var index = 0; index < nodes.Length; index++)
        {
            active[index] = nodes[index].gameObject.activeSelf;
        }
        for (var index = 0; index < behaviours.Length; index++)
        {
            var behaviour = behaviours[index];
            enabled[index] = behaviour.enabled;
            if (behaviour is not Graphic && behaviour is not Selectable && behaviour is not Canvas
                && behaviour is not CanvasGroup
                && behaviour is not CanvasScaler && behaviour is not RectMask2D && behaviour is not Mask
                && behaviour is not ScrollRect)
            {
                behaviour.enabled = false;
            }
        }
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        for (var index = nodes.Length - 1; index > 0; index--)
        {
            nodes[index].gameObject.SetActive(false);
        }
        try
        {
            root.SetActive(true);
            yield return null;
            var budget = Stopwatch.StartNew();
            var count = 0;
            for (var index = 1; index < nodes.Length; index++)
            {
                if (root == null)
                {
                    yield break;
                }
                var node = nodes[index];
                node.gameObject.SetActive(true);
                if (node.TryGetComponent<TextMeshProUGUI>(out var text))
                {
                    var value = text.text;
                    if (value.Length == 0)
                    {
                        text.text = "0123456789AS+";
                    }
                    text.ForceMeshUpdate();
                    text.text = value;
                }
                if (++count >= 12 || budget.Elapsed.TotalMilliseconds >= 2d)
                {
                    yield return null;
                    budget.Restart();
                    count = 0;
                }
            }
            yield return null;
        }
        finally
        {
            if (root != null)
            {
                if (!keepActive)
                {
                    root.SetActive(false);
                }
                for (var index = nodes.Length - 1; index > 0; index--)
                {
                    nodes[index].gameObject.SetActive(active[index]);
                }
                for (var index = 0; index < behaviours.Length; index++)
                {
                    behaviours[index].enabled = enabled[index];
                }
                group.alpha = alpha;
                group.interactable = interactable;
                group.blocksRaycasts = raycasts;
            }
        }
        if (root != null)
        {
            ready();
        }
    }
}
