// #if TWEENS_DEFINED_COM_UNITY_TEXTMESHPRO

using ElRaccoone.Tweens.Core;
using UnityEngine;
using TMPro;

namespace ElRaccoone.Tweens {
  public static partial class NativeTween {
    public static Tween<Color> TextMeshProColor (this Component self, Color to, float duration) =>
      Tween<Color>.Add<TextMeshProColorDriver> (self).Finalize (to, duration);

    public static Tween<Color> TextMeshProColor (this GameObject self, Color to, float duration) =>
      Tween<Color>.Add<TextMeshProColorDriver> (self).Finalize (to, duration);

    /// <summary>
    /// The driver is responsible for updating the tween's state.
    /// </summary>
    private class TextMeshProColorDriver : TweenComponent<Color, TextMeshPro> {
      
      /// <summary>
      /// Overriden method which is called when the tween starts and should
      /// return the tween's initial value.
      /// </summary>
      public override Color OnGetFrom () {
        return this.component.color;
      }

      /// <summary>
      /// Overriden method which is called every tween update and should be used
      /// to update the tween's value.
      /// </summary>
      /// <param name="easedTime">The current eased time of the tween's step.</param>
      public override void OnUpdate (float easedTime) {
        this.valueCurrent.r = this.InterpolateValue (this.valueFrom.r, this.valueTo.r, easedTime);
        this.valueCurrent.g = this.InterpolateValue (this.valueFrom.g, this.valueTo.g, easedTime);
        this.valueCurrent.b = this.InterpolateValue (this.valueFrom.b, this.valueTo.b, easedTime);
        this.valueCurrent.a = this.InterpolateValue (this.valueFrom.a, this.valueTo.a, easedTime);
        this.component.color = this.valueCurrent;
      }
    }
  }
}

// #endif