#if UNITY_EDITOR
using FishNet.Configuring;
using FishNet.Managing.Object;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

public static class FishNetSetup
{
    public static void Setup()
    {
        FishNet.Configuring.Configuration.Configurations.PrefabGenerator.Enabled = false;
        var prefabs =
            AssetDatabase.LoadAssetAtPath<DefaultPrefabObjects>(
                "Packages/gg.easy.airship/Runtime/Code/DefaultPrefabObjects.asset");
        prefabs.Clear();
        prefabs.AddObject(AssetDatabase.LoadAssetAtPath<NetworkObject>("Packages/gg.easy.airship/Runtime/Prefabs/Player.prefab"));
        // FishNet.Configuring.Configuration.Configurations.PrefabGenerator.DefaultPrefabObjectsPath =
        //     "Packages/gg.easy.airship/Runtime/Code/DefaultPrefabObjects.asset";
        FishNet.Configuring.Configuration.Configurations.Write(true);

        // Delete the default place fish-net places the DefaultPrefabObjects file.
        // We want to instead reference the config located in the airship package.
        AssetDatabase.DeleteAsset("Assets/DefaultPrefabObjects.asset");
    }
}
#endif