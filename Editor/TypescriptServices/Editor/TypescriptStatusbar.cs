﻿using System;
using Editor.EditorInternal;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Airship.Editor {
    [InitializeOnLoad]
    public class TypescriptStatusbar {
        static TypescriptStatusbar() {
#if AIRSHIP_PLAYER
            return;
#endif
            if (!TypescriptServices.IsValidEditor) return;
            EditorApplication.delayCall += MountStatusbar;
        }

        private static void MountStatusbar() {
            IMGUIContainer statusbar = GUIViewExtensions.GetIMGUIContainerForStatusbar();
            if (statusbar != null) statusbar.onGUIHandler += OnGUI;
            
            EditorApplication.delayCall -= MountStatusbar;
        }

        private static string GetTimeString(TimeSpan span) {
            if (span.Minutes > 0) {
                return $"{span.Minutes} minutes ago";
            }
            else {
                return $"{span.Seconds} seconds ago";
            }
        }
        
        private static void OnGUI() {
            Rect lastRect = GUILayoutUtility.GetLastRect();
            lastRect.xMin = lastRect.xMax - 80 - 200;
            if (Progress.GetRunningProgressCount() > 0) {
                lastRect.xMin -= 200;
            }
            
            lastRect.width = 200;

            switch (TypescriptCompilationService.CompilerState) {
                case TypescriptCompilerState.Idle:
                    GUI.Button(
                        lastRect,
                        TypescriptCompilationService.ErrorCount > 0
                            ? $"Failed to compile {GetTimeString((DateTime.Now - TypescriptCompilationService.LastCompiled))}"
                            : $"Last compiled {GetTimeString((DateTime.Now - TypescriptCompilationService.LastCompiled))}",
                        "StatusBarIcon"
                    );
                    break;
            }
        }
    }
}