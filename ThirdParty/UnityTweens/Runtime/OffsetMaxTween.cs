using ElRaccoone.Tweens.Core;
using UnityEngine;

namespace ElRaccoone.Tweens {
    public static partial class NativeTween {
        public static Tween<Vector2> OffsetMax(this Component self, Vector2 to, float duration) =>
            Tween<Vector2>.Add<OffsetMaxDriver>(self).Finalize(to, duration);

        public static Tween<Vector2> OffsetMax(this GameObject self, Vector2 to, float duration) =>
            Tween<Vector2>.Add<OffsetMaxDriver>(self).Finalize(to, duration);

        /// <summary>
        /// The driver is responsible for updating the tween's state.
        /// </summary>
        private class OffsetMaxDriver : TweenComponent<Vector2, RectTransform> {

            /// <summary>
            /// Overriden method which is called when the tween starts and should
            /// return the tween's initial value.
            /// </summary>
            public override Vector2 OnGetFrom() {
                return this.component.offsetMax;
            }

            /// <summary>
            /// Overriden method which is called every tween update and should be used
            /// to update the tween's value.
            /// </summary>
            /// <param name="easedTime">The current eased time of the tween's step.</param>
            public override void OnUpdate(float easedTime) {
                this.valueCurrent.x = this.InterpolateValue(this.valueFrom.x, this.valueTo.x, easedTime);
                this.valueCurrent.y = this.InterpolateValue(this.valueFrom.y, this.valueTo.y, easedTime);
                this.component.offsetMax = this.valueCurrent;
            }
        }
    }
}