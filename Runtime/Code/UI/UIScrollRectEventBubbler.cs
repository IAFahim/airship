using UnityEngine;
using UnityEngine.EventSystems;

/// https://forum.unity.com/threads/child-objects-blocking-scrollrect-from-scrolling.311555/#post-6894200
/// <summary>
   /// Bubbles events to the parent. Use this to overcome EventTriggers which stop scroll and drag events from bubbling.
   ///
   /// If an EventTrigger component is attached and other code is listening for
   /// onPointer events then these will NOT be triggered while dragging if DisableEventTriggerWhileDragging
   /// is true.
/// </summary>
    [LuauAPI]
    public class UIScrollRectEventBubbler : MonoBehaviour,
                                       IBeginDragHandler,
                                       IDragHandler,
                                       IEndDragHandler,
                                       IScrollHandler

   {
       [Tooltip("Should the scroll and drag events be forwarded (bubble up) to the parent?")]
       public bool Bubble = true;

       [Tooltip("Stop EventTriggers from executing events while dragging?")]
       public bool DisableEventTriggerWhileDragging = true;

       public bool DisableDragEvents = false;

       protected EventTrigger eventTrigger;
       public EventTrigger EventTrigger
       {
           get
           {
               if (eventTrigger == null)
               {
                   eventTrigger = this.GetComponent<EventTrigger>();
               }
               return eventTrigger;
           }
       }

       protected bool dragging = false;

       protected void HandleEventPropagation<T>(Transform goTransform, BaseEventData eventData, ExecuteEvents.EventFunction<T> callbackFunction) where T : IEventSystemHandler
       {
           if (Bubble && goTransform.parent != null)
           {
               ExecuteEvents.ExecuteHierarchy(goTransform.parent.gameObject, eventData, callbackFunction);
           }
       }

       public void OnScroll(PointerEventData eventData)
       {
           HandleEventPropagation(transform, eventData, ExecuteEvents.scrollHandler);
       }

       public void OnBeginDrag(PointerEventData eventData)
       {
           HandleEventPropagation(transform, eventData, ExecuteEvents.beginDragHandler);

           dragging = true;
           if (DisableEventTriggerWhileDragging && EventTrigger != null)
           {
               EventTrigger.enabled = false;
           }
       }

       public void OnDrag(PointerEventData eventData)
       {
           if (DisableDragEvents) {
               eventData.Use();
               return;
           }
           HandleEventPropagation(transform, eventData, ExecuteEvents.dragHandler);
       }

       public void OnEndDrag(PointerEventData eventData)
       {
           HandleEventPropagation(transform, eventData, ExecuteEvents.endDragHandler);

           dragging = false;
           if (DisableEventTriggerWhileDragging && EventTrigger != null)
           {
               EventTrigger.enabled = true;
           }
       }



       /// <summary>
       /// If the object is disabled while being dragged then the EventTrigger would remain disabled.
       /// </summary>
       public void OnDisable()
       {
           if (DisableEventTriggerWhileDragging && dragging && EventTrigger != null)
           {
               dragging = false;
               EventTrigger.enabled = true;
           }
       }
   }