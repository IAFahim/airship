﻿using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Nobi.UiRoundedCorners {
    [ExecuteInEditMode]								//Required to check the OnEnable function
    [DisallowMultipleComponent]                     //You can only have one of these in every object.
    [RequireComponent(typeof(RectTransform))]
	public class ImageWithRoundedCorners : MonoBehaviour {
		private static readonly int Props = Shader.PropertyToID("_WidthHeightRadius");

        public float radius = 40f;
		
        [NonSerialized]
        private Material material;

		[HideInInspector, SerializeField] private MaskableGraphic image;

		private void OnValidate() {
			if (Application.isPlaying && !RunCore.IsClient()) return;

			Validate();
			Refresh();
		}

		private void OnDestroy() {
			if (Application.isPlaying && !RunCore.IsClient()) return;

			if (image != null) {
				image.material = null;      //This makes so that when the component is removed, the UI material returns to null
				// EditorUtility.ClearDirty(image);
			}

			DestroyHelper.Destroy(material);
			image = null;
			material = null;
		}

		private void OnEnable() {
			if (Application.isPlaying && !RunCore.IsClient()) return;

            //You can only add either ImageWithRoundedCorners or ImageWithIndependentRoundedCorners
            //It will replace the other component when added into the object.
            var other = GetComponent<ImageWithIndependentRoundedCorners>();
            if (other != null)
            {
                radius = other.r.x;					//When it does, transfer the radius value to this script
                DestroyHelper.Destroy(other);
            }

            Validate();
			Refresh();
		}

		private void OnRectTransformDimensionsChange() {
			if (Application.isPlaying && !RunCore.IsClient()) return;

			if (enabled && material != null) {
				Refresh();
			}
		}

		public void Validate() {
			if (material == null) {
				var shader = Shader.Find("UI/RoundedCorners/RoundedCorners");
				if (shader == null) return;
				
				material = new Material(shader) {
					// hideFlags = HideFlags.DontSave
				};
			}

			if (image == null) {
				TryGetComponent(out image);
			}

			if (image != null) {
				image.material = material;
			}
		}

		public void Refresh() {
			var rect = ((RectTransform)transform).rect;

            //Multiply radius value by 2 to make the radius value appear consistent with ImageWithIndependentRoundedCorners script.
            //Right now, the ImageWithIndependentRoundedCorners appears to have double the radius than this.
            if (material) {
	            var newVec = new Vector4(rect.width, rect.height, radius * 2, 0);
	            var existing = material.GetVector(Props);
	            if ((existing - newVec).magnitude > 0.1f) {
		            material.SetVector( Props, newVec);
	            }
            }
		}
	}
}