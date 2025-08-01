// TextMeshPro dynamic font assets have a very annoying habit of saving their dynamically generated binary data in the
// same text file as their configuration data. This causes massive headaches for version control.
//
// This script addresses the above issue. It runs whenever any assets in the project are about to be saved. If any of
// those assets are a TMP dynamic font asset, they will have their dynamically generated data cleared before they are
// saved, which prevents that data from ever polluting the version control.
//
// For more information, see this post by @cxode: https://discussions.unity.com/t/868941/20
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace TMProDynamicDataCleaner.Editor
{
    internal class DynamicFontAssetAutoCleaner : UnityEditor.AssetModificationProcessor
    {
        static string[] OnWillSaveAssets(string[] paths)
        {
            try
            {
                foreach (string path in paths)
                {
                    var assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
                   
                    // GetMainAssetTypeAtPath() sometimes returns null, for example, when path leads to .meta file
                    if (assetType == null)
                        continue;
                   
                    // TMP_FontAsset is not marked as sealed class, so also checking for subclasses just in case
                    if (assetType != typeof(FontAsset) && assetType.IsSubclassOf(typeof(FontAsset)) == false)
                        continue;

                    // Loading the asset only when we sure it is a font asset
                    var fontAsset = AssetDatabase.LoadMainAssetAtPath(path) as FontAsset;

                    // Theoretically this case is not possible due to asset type check above, but to be on the safe side check for null
                    if (fontAsset == null)
                        continue;

                    if (fontAsset.atlasPopulationMode != AtlasPopulationMode.Dynamic)
                        continue;

                    fontAsset.ClearFontAssetData(setAtlasSizeToZero: true);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("Something went wrong while clearing dynamic font data. For more info look at log message above.");
            }

            return paths;
        }
    }
}