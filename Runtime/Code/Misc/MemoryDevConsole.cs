﻿using System;
using System.Collections.Generic;
using Mirror;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MemoryDevConsole : MonoBehaviour {
	[Min(0.25f)]
	[Tooltip("Seconds between data refreshes")]
	public float refreshRate = 1;

	public GameObject logInstance;
	public ScrollRect scrollRect;
	public RectTransform contentFrame;
	public TMP_InputField searchField;
	public TMP_Text totalBytesLabel;
	public TMP_Text unityObjectsLabel;

	[SerializeField] private GameObject sortedButton;
	
	[SerializeField] private Color sortedButtonBackgroundColorActive;
	[SerializeField] private Color sortedButtonBackgroundColorInactive;
	[SerializeField] private Color sortedButtonTextColorActive;
	[SerializeField] private Color sortedButtonTextColorInactive;

	private LuauContext _context = LuauContext.Game;
	private MemoryEnvironment _environment = MemoryEnvironment.Client;
	private MemorySort _sort = MemorySort.None;
	
	private float _lastRefreshTime;
	private Dictionary<LuauContext, List<LuauPlugin.LuauMemoryCategoryDumpItem>> _dumps;

	private AirshipLuauDebugger _luauDebugger;
	private bool _hasLuauDebugger = false;

	private string _searchTerm = "";

	private readonly List<TMP_InputField> _logItems = new();
	private readonly List<TMP_InputField> _logItemsPool = new();

	private enum MemoryEnvironment {
		Client,
		Server,
	}

	private enum MemorySort {
		None,
		Bytes,
		Alphabetical,
	}
		
	private static string FormatBytes(ulong bytes) {
		if (bytes < 1024) {
			return $"{bytes} B";
		}

		if (bytes < 1024L * 1024L) {
			return $"{(bytes / 1024f):F2} KB";
		}

		if (bytes < 1024 * 1024L * 1024L) {
			return $"{(bytes / (1024f * 1024f)):F2} MB";
		}

		return $"{(bytes / (1024f * 1024f * 1024f)):F2} GB";
	}

	private void Start() {
		_lastRefreshTime = 0;
		
		_dumps = new Dictionary<LuauContext, List<LuauPlugin.LuauMemoryCategoryDumpItem>> {
			[LuauContext.Game] = new(),
			[LuauContext.Protected] = new(),
		};
		
		_luauDebugger = FindAnyObjectByType<AirshipLuauDebugger>();
		_hasLuauDebugger = _luauDebugger != null;
		
		UpdateSortedButtonAppearance();

		if (_hasLuauDebugger) {
			_luauDebugger.ServerMemDump.OnChange += OnServerMemDumpChanged;
		}
	}

	private void OnDestroy() {
		if (_hasLuauDebugger) {
			_luauDebugger.ServerMemDump.OnChange -= OnServerMemDumpChanged;
		}
	}

	private void OnEnable() {
		LuauCore.onResetInstance += OnLuauContextReset;
		SceneManager.activeSceneChanged += OnActiveSceneChanged;
	}

	private void OnDisable() {
		LuauCore.onResetInstance -= OnLuauContextReset;
		SceneManager.activeSceneChanged -= OnActiveSceneChanged;
	}

	private void OnActiveSceneChanged(Scene current, Scene next) {
		_hasLuauDebugger = false;
		_luauDebugger = null;
	}

	private void OnLuauContextReset(LuauContext ctx) {
		_dumps[ctx].Clear();
	}

	public void OnContextDropdownSelected(int n) {
		var newContext = n switch {
			0 => LuauContext.Game,
			1 => LuauContext.Protected,
			_ => LuauContext.Game,
		};

		if (newContext != _context) {
			_context = newContext;
			_lastRefreshTime = 0;
		}
	}

	public void OnEnvironmentDropdownSelected(int n) {
		var newEnvironment = n switch {
			0 => MemoryEnvironment.Client,
			1 => MemoryEnvironment.Server,
			_ => MemoryEnvironment.Client,
		};

		if (newEnvironment != _environment) {
			_environment = newEnvironment;
			_lastRefreshTime = 0;

			if (newEnvironment == MemoryEnvironment.Server && !_hasLuauDebugger) {
				_luauDebugger = FindAnyObjectByType<AirshipLuauDebugger>();
				_hasLuauDebugger = _luauDebugger != null;
			}
		}
	}

	public void OnSearchFieldChanged(string text) {
		_searchTerm = text.Trim();
		RefreshUI();
	}

	private void OnServerMemDumpChanged(SyncDictionary<LuauContext, List<LuauPlugin.LuauMemoryCategoryDumpItem>>.Operation op, LuauContext ctx, List<LuauPlugin.LuauMemoryCategoryDumpItem> dump) {
		if (_environment == MemoryEnvironment.Client || ctx != _context) {
			return;
		}
		
		Refresh();
	}

	private void UpdateSortedButtonAppearance() {
		var sorted = _sort != MemorySort.None;
		
		var image = sortedButton.GetComponent<Image>();
		image.color = sorted ? sortedButtonBackgroundColorActive : sortedButtonBackgroundColorInactive;

		var text = sortedButton.GetComponentInChildren<TMP_Text>();
		text.color = sorted ? sortedButtonTextColorActive : sortedButtonTextColorInactive;

		text.text = _sort switch {
			MemorySort.Bytes => "Sort [Size]",
			MemorySort.Alphabetical => "Sort [A-Z]",
			_ => "Sort"
		};
	}

	public void OnSortedButtonClicked() {
		// Cursed enum increment:
		_sort = (MemorySort)(((int)_sort + 1) % 3);
		
		_lastRefreshTime = 0;
		UpdateSortedButtonAppearance();
	}

	private void Update() {
		var now = Time.unscaledTime;
		if (now - _lastRefreshTime < refreshRate) {
			return;
		}

		_lastRefreshTime = now;

		if (_environment == MemoryEnvironment.Server && _hasLuauDebugger) {
			_luauDebugger.FetchServerMemoryDump(_context);
		}
		
		Refresh();
	}

	private void RefreshUI() {
		List<LuauPlugin.LuauMemoryCategoryDumpItem> dump = null;
		switch (_environment) {
			case MemoryEnvironment.Client:
				dump = _dumps[_context];
				break;
			case MemoryEnvironment.Server:
				if (_hasLuauDebugger) {
					_luauDebugger.ServerMemDump.TryGetValue(_context, out dump);
				}
				dump ??= new List<LuauPlugin.LuauMemoryCategoryDumpItem>();
				break;
			default:
				throw new Exception("unknown environment");
		}
		
		if (_sort == MemorySort.Bytes) {
			var sortedDump = new List<LuauPlugin.LuauMemoryCategoryDumpItem>(dump);
			sortedDump.Sort(((itemA, itemB) => itemA.Bytes == itemB.Bytes ? 0 : itemA.Bytes < itemB.Bytes ? 1 : -1));
			dump = sortedDump;
		} else if (_sort == MemorySort.Alphabetical) {
			var sortedDump = new List<LuauPlugin.LuauMemoryCategoryDumpItem>(dump);
			sortedDump.Sort(((itemA, itemB) => string.Compare(itemA.ShortName, itemB.ShortName, StringComparison.OrdinalIgnoreCase)));
			dump = sortedDump;
		}

		var totalBytes = 0UL;

		var itemsShown = 0;
		var itemsCreated = false;
		foreach (var item in dump) {
			totalBytes += item.Bytes;
			
			if (!string.IsNullOrEmpty(_searchTerm) && !item.ShortName.Contains(_searchTerm, StringComparison.OrdinalIgnoreCase)) continue;
			
			TMP_InputField instance;
			if (itemsShown < _logItems.Count) {
				instance = _logItems[itemsShown];
			} else if (_logItemsPool.Count > 0) {
				instance = _logItemsPool[^1];
				_logItemsPool.RemoveAt(_logItemsPool.Count - 1);
				instance.transform.SetParent(contentFrame);
				_logItems.Add(instance);
			} else {
				var go = Instantiate(logInstance, contentFrame);
				go.GetComponent<LogFieldInstance>().redirectTarget = scrollRect;
				instance = go.GetComponent<TMP_InputField>();
				_logItems.Add(instance);
				itemsCreated = true;
			}
			instance.text = $"{item.ShortName}: <b><color=\"green\">{FormatBytes(item.Bytes)}</color></b>";
			itemsShown++;
		}

		// Trim off unused labels:
		while (_logItems.Count > itemsShown) {
			var item = _logItems[^1];
			item.transform.SetParent(null);
			_logItemsPool.Add(item);
			_logItems.RemoveAt(_logItems.Count - 1);
		}
		
		totalBytesLabel.text = $"Total: <b><color=\"green\">{FormatBytes(totalBytes)}</color></b>";

		var unityObjects = 0UL;
		if (_environment == MemoryEnvironment.Client) {
			unityObjects = LuauPlugin.LuauGetUnityObjectCount();
		} else if (_hasLuauDebugger) {
			unityObjects = _luauDebugger.ServerUnityObjects;
		}
		unityObjectsLabel.text = $"Unity Objects: <b><color=\"green\">{unityObjects}</color></b>";

		if (itemsCreated) {
			Bridge.UpdateLayout(contentFrame, true);
		}
	}

	private void Refresh() {
		switch (_environment) {
			case MemoryEnvironment.Client:
				LuauPlugin.LuauGetMemoryCategoryDump(_context, _dumps[_context]);
				break;
			case MemoryEnvironment.Server:
				break;
			default:
				throw new Exception("unknown environment");
		}
		
		RefreshUI();
	}
}
