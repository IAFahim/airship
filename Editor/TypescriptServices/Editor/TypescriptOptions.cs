﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Editor.Settings;
using NUnit.Framework;
using Unity.CodeEditor;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;

namespace Airship.Editor {
    public enum TypescriptEditor {
        SystemDefined,
        VisualStudioCode,
        Custom,
    }
    
    public enum TypescriptCompilerVersion {
        UseEditorVersion,
        UseLocalDevelopmentBuild,
    }

    [Flags]
    public enum TypescriptPublishFlags {
        RecompileOnCodePublish = 2,
        RecompileOnFullPublish = 1,
    }
    
    public class TypescriptPopupWindow : PopupWindowContent {
        private static GUIStyle MenuItemIcon = new GUIStyle("LargeButtonMid") {
            fontSize = 13,
            fixedHeight = 25,
            fixedWidth = 0,
            padding = new RectOffset(5, 5, 0, 0),
            margin = new RectOffset(5, 5, 2, 2),
            imagePosition = ImagePosition.ImageLeft,
            alignment = TextAnchor.MiddleLeft,
        };
        
        private static GUIStyle MenuItem = new GUIStyle("LargeButtonMid") {
            fontSize = 13,
            fixedHeight = 25,
            stretchWidth = true,
            fixedWidth = 0,
            padding = new RectOffset(5, 5, 0, 0),
            margin = new RectOffset(5, 5, 5, 5),
            imagePosition = ImagePosition.ImageLeft,
            alignment = TextAnchor.MiddleLeft,
        };

        private static Texture BuildIcon = EditorGUIUtility.Load("d_CustomTool") as Texture;
        private static Texture PlayIcon = EditorGUIUtility.Load("d_PlayButton") as Texture;
        private static Texture PlayIconOn = EditorGUIUtility.Load("PlayButton On") as Texture;
        private static Texture StopIcon = EditorGUIUtility.Load("d_StopButton") as Texture;
        private static Texture RevealIcon = EditorGUIUtility.Load("d_CustomTool") as Texture;
        private static Texture SettingsIcon = EditorGUIUtility.Load("d_SettingsIcon") as Texture;

        public override Vector2 GetWindowSize() {
            return new Vector2(400, 80 + 11);
        }
        
        public override void OnGUI(Rect rect) {
            EditorGUILayout.LabelField("TypeScript", new GUIStyle(EditorStyles.largeLabel) { fontStyle = FontStyle.Bold});

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical();
            {
                if (TypescriptCompilationService.IsWatchModeRunning) {
                    if (GUILayout.Button(
                            new GUIContent(" Stop TypeScript", StopIcon, "Stops TypeScript from automatically compiling the projects it is watching"),
                            MenuItem)) {
                        TypescriptCompilationService.StopCompilers();
                    }
                }
                else {
                    if (GUILayout.Button(
                            new GUIContent(" Start TypeScript", PlayIconOn, "Start TypeScript automatically compiling all projects when changed"),
                            MenuItem)) {
                        TypescriptCompilationService.StartCompilerServices();
                    }
                }
                
                if (GUILayout.Button(
                        new GUIContent(" Build", BuildIcon, "Run a full build of the TypeScript code + generate types for the project(s) - will stop any active compilers"),
                        MenuItem)) {
                    var compileFlags = TypeScriptCompileFlags.FullClean | TypeScriptCompileFlags.DisplayProgressBar;
                    if (EditorIntegrationsConfig.instance.typescriptIncremental) {
                        compileFlags |= TypeScriptCompileFlags.Incremental;
                    }

                    if (EditorIntegrationsConfig.instance.typescriptVerbose) {
                        compileFlags |= TypeScriptCompileFlags.Verbose;
                    }
                    
                    if (TypescriptCompilationService.IsWatchModeRunning) {
                        TypescriptCompilationService.StopCompilerServices();
                        TypescriptCompilationService.BuildTypescript(compileFlags);
                        TypescriptCompilationService.StartCompilerServices();
                    }
                    else {
                        TypescriptCompilationService.BuildTypescript(compileFlags);
                    }
                }
            }
            EditorGUILayout.EndVertical();

            if (GUILayout.Button(
                    new GUIContent(" Configure", SettingsIcon, "Configure the TypeScript projects or settings around the TypeScript services"),
                    MenuItem)) {
                SettingsService.OpenProjectSettings(AirshipScriptingSettingsProvider.Path);
            }

            EditorGUILayout.EndHorizontal();
        }
    }

    enum TypescriptOptionsTab {
        ProjectSettings,
        LocalSettings,
    }
    
    public static class TypescriptOptions {
        private static string[] args = {
            "This should be the path of the editor you want to open TypeScript files with",
            "-- included variables: --",
            "{filePath} - The path of the file",
            "{line} - The line of the file",
            "{column} - The column of the file"
        };
        
        internal static void RenderProjectSettings() {
            var projectSettings = EditorIntegrationsConfig.instance;
            var localSettings = TypescriptServicesLocalConfig.instance;
            
            AirshipEditorGUI.BeginSettingGroup(new GUIContent("Service Options"));
            {
                projectSettings.typescriptAutostartCompiler =
                    EditorGUILayout.ToggleLeft(new GUIContent("Automatically Run on Editor Startup", "Compilation of TypeScript files will be handled by the editor"), projectSettings.typescriptAutostartCompiler);
                projectSettings.typescriptPreventPlayOnError =
                    EditorGUILayout.ToggleLeft(new GUIContent("Prevent Play Mode With Errors", "Stop being able to go into play mode if there are active compiler errors"), projectSettings.typescriptPreventPlayOnError);
                
                localSettings.skipCompileOnCodeDeploy =
                    EditorGUILayout.ToggleLeft(new GUIContent("Skip Compile on Code Publish *", "This will skip compilation on code publish"), localSettings.skipCompileOnCodeDeploy);

                if (localSettings.skipCompileOnCodeDeploy) {
                    EditorGUILayout.HelpBox("Skipping compilation on code deploy may speed up the deploy, but it has a very small chance of publishing broken code if the watch state is incorrect.", MessageType.Warning);
                }
            }
            AirshipEditorGUI.EndSettingGroup();
            
            AirshipEditorGUI.BeginSettingGroup(new GUIContent("Compiler Options"));
            {
                
                var prevIncremental = projectSettings.typescriptIncremental;
                var nextIncremental = EditorGUILayout.ToggleLeft(
                    new GUIContent("Incremental Compilation",
                        "Speeds up compilation times by skipping unchanged files (This is skipped when publishing)"),
                    prevIncremental);

                projectSettings.typescriptIncremental = nextIncremental;
                if (prevIncremental != nextIncremental) {
                    TypescriptCompilationService.RestartCompilers();
                }
            

                projectSettings.typescriptVerbose = EditorGUILayout.ToggleLeft(new GUIContent("Verbose Output", "Will display much more verbose information when compiling a TypeScript project"),  projectSettings.typescriptVerbose );

            
                
                #if AIRSHIP_INTERNAL
                projectSettings.typescriptWriteOnlyChanged = EditorGUILayout.ToggleLeft(new GUIContent("Write Only Changed", "Will write only changed files (this shouldn't be enabled unless there's a good reason for it)"), projectSettings.typescriptWriteOnlyChanged);
                #endif    
            }
            AirshipEditorGUI.EndSettingGroup();
            
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("* This property only applies to your instance of the project");
            
            if (GUI.changed) {
                projectSettings.Modify();
                localSettings.Modify();
            }
        }

        internal static void RenderLocalSettings() {
            var localSettings = TypescriptServicesLocalConfig.instance;
            
            var currentCompiler = TypescriptCompilationService.CompilerVersion;
            if (TypescriptCompilationService.UsableVersions.Length > 1) {
                AirshipEditorGUI.BeginSettingGroup(new GUIContent("Compiler Options"));
                
                var selectedCompiler = (TypescriptCompilerVersion) EditorGUILayout.EnumPopup(
                    new GUIContent("Editor Compiler", "The compiler to use when compiling the Typescript files in your project"), 
                    currentCompiler,
                    (version) => {
                        switch ((TypescriptCompilerVersion)version) {
                            case TypescriptCompilerVersion.UseEditorVersion:
                                return File.Exists(TypescriptCompilationService.EditorCompilerPath);
                            case TypescriptCompilerVersion.UseLocalDevelopmentBuild: 
                                return TypescriptCompilationService.HasDevelopmentCompiler;
                            default:
                                return false;
                        }
                    },
                    false
                );
                    
                if (currentCompiler != selectedCompiler) {
                    TypescriptCompilationService.RestartCompilers(() => {
                        TypescriptCompilationService.CompilerVersion = selectedCompiler;
                    });
                }
                
                AirshipEditorGUI.EndSettingGroup();
            }
            
            AirshipEditorGUI.BeginSettingGroup(new GUIContent("Editor Options"));
            {
                List<CodeEditor.Installation> installations = new();
                
                installations.Add(new CodeEditor.Installation() {
                    Name = "Open by file extension",
                    Path = ""
                });
                
                int currentIndex = -1;

                if (AirshipExternalCodeEditor.CurrentEditorPath == "") {
                    currentIndex = 0;
                }
                
                foreach (var editor in AirshipExternalCodeEditor.Editors) {
                    if (editor.Installations == null) continue;

                    int idx = 1;
                    foreach (var installation in editor.Installations) {
                        installations.Add(installation);
                        if (installation.Path == AirshipExternalCodeEditor.CurrentEditorPath) {
                            currentIndex = idx;
                        }

                        idx++;
                    }
                }
                
                var selectedIdx = EditorGUILayout.Popup("External Script Editor", 
                    currentIndex, 
                    installations.Select(installation => installation.Name).ToArray());

                if (selectedIdx != currentIndex) {
                    AirshipExternalCodeEditor.SetCodeEditor(installations[selectedIdx].Path);
                }
                
                if (AirshipExternalCodeEditor.CurrentEditorPath != "")
                    EditorGUILayout.LabelField("Editor Path", AirshipExternalCodeEditor.CurrentEditorPath);
            }
            AirshipEditorGUI.EndSettingGroup();
            
            if (GUI.changed) {
                localSettings.Modify();
            }
        }

        private static TypescriptOptionsTab selectedTab;
        internal static void RenderSettings() {
  

            selectedTab = (TypescriptOptionsTab) AirshipEditorGUI.BeginTabs((int) selectedTab, new[] {
                "Project Settings", 
                "Local Settings"
            });
            {
                if (selectedTab == TypescriptOptionsTab.ProjectSettings) {
                    RenderProjectSettings();
                }
                else {
                    RenderLocalSettings();
                }
            }
            AirshipEditorGUI.EndTabs();
        }
    }
}