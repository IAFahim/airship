﻿#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Luau;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public static class AdvancedDropdownExtensions {
    public static void Show(this AdvancedDropdown dropdown, Rect buttonRect, float maxHeight) {
        dropdown.Show(buttonRect);
        SetMaxHeightForOpenedPopup(buttonRect, maxHeight);
    }

    private static void SetMaxHeightForOpenedPopup(Rect buttonRect, float maxHeight) {
        var window = EditorWindow.focusedWindow;

        if (window == null) {
            Debug.LogWarning("EditorWindow.focusedWindow was null.");
            return;
        }

        if (!string.Equals(window.GetType().Namespace, typeof(AdvancedDropdown).Namespace)) {
            Debug.LogWarning("EditorWindow.focusedWindow " + EditorWindow.focusedWindow.GetType().FullName +
                             " was not in expected namespace.");
            return;
        }

        var position = window.position;

        position.height = maxHeight;
        position.width = buttonRect.width;
        window.minSize = position.size;
        window.maxSize = position.size;
        window.position = position;
        window.ShowAsDropDown(GUIUtility.GUIToScreenRect(buttonRect), position.size);
    }
}


public class AirshipComponentDropdown : AdvancedDropdown {
    private const string IconAsset = "Packages/gg.easy.airship/Editor/AirshipScriptIcon.png";
    private static Texture2D _assetIcon;
    public static Texture2D AssetIcon {
        get
        {
            if (_assetIcon == null) {
                _assetIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconAsset);
            }

            return _assetIcon;
        }
    }
    
    public class BinaryFileItem : AdvancedDropdownItem {
        public AirshipScript file;
        
        public BinaryFileItem(AirshipScript file, string name) : base(name) {
            this.file = file;
        }
    }

    private readonly Action<AirshipScript> binaryFileSelected;
    
    public AirshipComponentDropdown(AdvancedDropdownState state, Action<AirshipScript> binaryFileSelected) : base(state) {
        this.binaryFileSelected = binaryFileSelected;
    }

    protected override void ItemSelected(AdvancedDropdownItem item) {
        if (item is BinaryFileItem binaryFileItem) {
            binaryFileSelected.Invoke(binaryFileItem.file);
        }
    }


    private AdvancedDropdownItem FindOrCreateRelative(AdvancedDropdownItem root, string name) {
        foreach (var child in root.children) {
            if (child.name == name) {
                return child;
            }
        }

        var item = new AdvancedDropdownItem(name);
        root.AddChild(item);
        return item;
    }
    
    [CanBeNull]
    private BinaryFileItem GetOrCreateDropdownPath(AdvancedDropdownItem root, string[] folders, string componentName, AirshipScript airshipScript) {
        if (folders.Length == 0) {
            var item = new BinaryFileItem(airshipScript, componentName);
            root.AddChild(item);
            return item;
        }
  
        var rootFolder = folders[0];

        if (folders.Length > 1) {
            AdvancedDropdownItem relativeItem = FindOrCreateRelative(root, rootFolder);
            foreach (var nextItemName in folders[1..]) {
                relativeItem = FindOrCreateRelative(relativeItem, nextItemName);
            }
            
            var child = new BinaryFileItem(airshipScript, componentName);
            relativeItem.AddChild(child);
            return child;
        }
        else {
            var child = new BinaryFileItem(airshipScript, componentName);

            var relativeItem = FindOrCreateRelative(root, rootFolder);
            relativeItem.AddChild(child);
            return child;
        }
    }

    protected override AdvancedDropdownItem BuildRoot() {
        var root = new AdvancedDropdownItem("Airship Components");
        

        List<AirshipScript> binaryFiles = new();
        string[] guids = AssetDatabase.FindAssets("t:AirshipScript");
        foreach (var guid in guids) {
            AirshipScript airshipScript = AssetDatabase.LoadAssetAtPath<AirshipScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (airshipScript.airshipBehaviour) {
                binaryFiles.Add(airshipScript);
            }
        }

        var scripts = new AdvancedDropdownItem("Scripts");

        var icon = AssetIcon;

        foreach (var binaryFile in binaryFiles) {
            if (binaryFile.m_metadata == null) continue;
            
            var arshipComponentMenu = binaryFile.m_metadata.decorators.Find(f => f.name == "AirshipComponentMenu");
            if (arshipComponentMenu != null && arshipComponentMenu.parameters[0].TryGetString(out string customPath)) {
                var pathComponents = customPath.Split("/");
                var path = pathComponents.Last();
                
                var item = GetOrCreateDropdownPath(root, pathComponents[..^1], path, binaryFile);
                if (item != null) {
                    if (binaryFile.m_metadata?.displayIcon != null) {
                        item.icon = binaryFile.m_metadata.displayIcon;
                    }
                    else {
                        item.icon = icon;
                    }
                    
                    
                }
            }
            else {
                var isPackage = binaryFile.m_path.StartsWith("Assets/Bundles/@");
                if (isPackage) {
                    var packagePath = binaryFile.m_path[15..].Split("/")[0..2];
                    var parent = FindOrCreateRelative(root, string.Join("/", packagePath));
                    var item = new BinaryFileItem(binaryFile, binaryFile.m_metadata.displayName)
                    {
                        icon = icon
                    };
                    parent.AddChild(item);
                }
                else {
                    var item = new BinaryFileItem(binaryFile, binaryFile.m_metadata.displayName)
                    {
                        icon = icon
                    };
                    scripts.AddChild(item);
                }
            }
        }
        
        root.AddChild(scripts);

        return root;
    }
}
#endif