using UnityEngine;
using UnityEngine.EventSystems;

public class CarpenterDimensionDialDragHandle : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private CarpenterDimensionDialUI dialUI;

    public void Initialize(CarpenterDimensionDialUI owner)
    {
        dialUI = owner;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        dialUI?.BeginDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        dialUI?.Drag(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        dialUI?.EndDrag(eventData);
    }
}
