using UnityEngine;
using UnityEngine.EventSystems;

namespace BEATNET;

public sealed class BeatNetFieldFocus : MonoBehaviour, ISelectHandler, IDeselectHandler
{
    internal GameObject Marker { get; set; } = null!;

    public void OnSelect(BaseEventData data) => Marker.SetActive(true);

    public void OnDeselect(BaseEventData data) => Marker.SetActive(false);

    private void OnDisable()
    {
        if (Marker != null)
        {
            Marker.SetActive(false);
        }
    }
}
