using System;
using System.Collections.Generic;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Scripting;
using Object = UnityEngine.Object;

[LuauAPI]
[Preserve]
public static class AssetBridge
{
	public static string BundlesPath = Path.Join(Application.persistentDataPath, "Bundles");

	public static bool useBundles = true;

	public static AssetBundle GetAssetBundle(string name)
	{
		AssetBundle retValue = SystemRoot.Instance.loadedAssetBundles[name].m_assetBundle;
		return retValue;
	}

	private static Type GetTypeFromPath(string path)
	{
		var extension = Path.GetExtension(path);
		Type type = null;
		switch (extension)
		{
			case ".asset":
				type = typeof(ScriptableObject);
				break;
			case ".png":
			case ".jpg":
				type = typeof(Texture2D);
				break;
			case ".ogg":
			case ".mp3":
			case ".wav":
				type = typeof(AudioClip);
				break;
			case ".txt":
			case ".bytes":
				type = typeof(TextAsset);
				break;
			default:
				type = typeof(Object);
				break;
		}

		return type;
	}

	/// <summary>
	/// Used by TS.
	/// C# should use <see cref="LoadAssetInternal{T}" />
	/// </summary>
	public static Object LoadAsset(string path)
	{
		return LoadAssetInternal<Object>(path);
	}

	public static Object LoadAssetIfExists(string path)
	{
		return LoadAssetInternal<Object>(path, false);
	}

	public static T LoadAssetIfExistsInternal<T>(string path) where T : Object
	{
		return LoadAssetInternal<T>(path, false);
	}

	public static bool IsLoaded()
	{
		return SystemRoot.Instance != null;
	}

	//Asset references are expected in the following format
	//  "RootBundlePath/
	public static T LoadAssetInternal<T>(string path, bool printErrorOnFail = true) where T : Object
	{
		path = path.ToLower();
		SystemRoot root = SystemRoot.Instance;

		if (root != null && useBundles && Application.isPlaying)
		{
			//determine the asset bundle via the prefix
			foreach (var bundleValue in root.loadedAssetBundles)
			{
				SystemRoot.AssetBundleMetaData bundle = bundleValue.Value;
				if (bundle.m_assetBundle == null)
				{
					continue;
				}

				bool thisBundle = bundle.PathBelongsToThisAssetBundle(path);
				if (thisBundle == false)
				{
					continue;
				}

				string file = bundle.FixupPath(path);
				//Debug.Log("file: " + file);

				if (bundle.m_assetBundle.Contains(file))
				{
					return bundle.m_assetBundle.LoadAsset<T>(file);
				}
				else
				{
					if (printErrorOnFail)
					{
						Debug.LogError("AssetBundle file not found: " + path + " (Attempted to load it from " + bundle.m_name + ")");
					}
					return null;
				}
			}

			if (printErrorOnFail)
			{
				Debug.LogError("AssetBundle file not found: " + path + " (No asset bundle understood this path - is this asset bundle loaded?)");
			}
			return null;
		}

#if UNITY_EDITOR
		//Check the resource system

		//Get path without extension
		Profiler.BeginSample("Editor.AssetBridge.LoadAsset");

		var isCore = path.Contains("coreshared/") || path.Contains("coreserver/") || path.Contains("coreclient/");

		// Assets/Game/Core/Bundles/CoreShared/Resources/TS/Main.lua
		//var fixedPath = $"Assets/Game/{(isCore ? "core" : "bedwars")}/Bundles/{path}".ToLower();

		// NOTE: For now, we're just building the core bundles into the game's bundle folder.
		var fixedPath = $"assets/bundles/{path}".ToLower();

		if (!fixedPath.Contains("/resources/"))
		{
			fixedPath = fixedPath.Replace("/ts/", "/resources/ts/");
			fixedPath = fixedPath.Replace("/include/", "/resources/include/");
			fixedPath = fixedPath.Replace("/rbxts_include/", "/resources/rbxts_include/");
		}
		
		//Debug.Log($"path: {path}, newPath: {newPath}");

		var res = AssetDatabase.LoadAssetAtPath<T>(fixedPath);

		if (res != null)
		{
			Profiler.EndSample();
			return res;
		}

		if (printErrorOnFail)
		{
			Debug.LogError("AssetBundle file not found. path: " + path + ", fixedPath: " + fixedPath);
		}

		Profiler.EndSample();
#endif
		return null;
	}

	public static string[] GetAllBundlePaths()
	{ 
        //Get a list of directories in Assets
        string[] directories = Directory.GetDirectories("Assets", "*", SearchOption.TopDirectoryOnly);
        
		//Get a list of bundles in each game
        List<string> bundles = new List<string>();
        foreach (string directory in directories)
        {
            string combinedPath = Path.Combine(directory, "Bundles");
            bundles.AddRange(Directory.GetDirectories(combinedPath, "*", SearchOption.TopDirectoryOnly));
        }
		return bundles.ToArray();	
    }

    public static string[] GetAllGameRootPaths()
    {
        //Get a list of directories in Assets
        string[] directories = Directory.GetDirectories("Assets", "*", SearchOption.TopDirectoryOnly);
        return directories;
    }

    public static string[] GetAllAssets()
	{
		List<string> results = new();
		foreach (var bundle in SystemRoot.Instance.loadedAssetBundles)
		{
			results.AddRange(bundle.Value.m_assetBundle.GetAllAssetNames());
		}

		return results.ToArray();
	}
}