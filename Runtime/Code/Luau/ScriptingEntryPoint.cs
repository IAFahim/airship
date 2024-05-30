﻿using Luau;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Assets.Code.Luau {
	public class ScriptingEntryPoint : MonoBehaviour {
		private const string CoreEntryScript = "airshippackages/@easy/core/shared/corebootstrap.ts";
		private const string MainMenuEntryScript = "airshippackages/@easy/core/shared/mainmenuingame.ts";
		
		private void Awake() {
			var gameBindings = GetComponentsInChildren<ScriptBinding>();

			// Main Menu
			{
				var go = new GameObject("MainMenuInGame");
				go.transform.parent = this.transform;
				var binding = go.AddComponent<ScriptBinding>();

				binding.SetScriptFromPath(MainMenuEntryScript, LuauContext.Protected);
				binding.contextOverwritten = true;
				binding.InitEarly();
			}

			// Core
			{
				var go = new GameObject("@Easy/Core");
				go.transform.parent = this.transform;
				var binding = go.AddComponent<ScriptBinding>();

				binding.SetScriptFromPath(CoreEntryScript, LuauContext.Game);
				binding.contextOverwritten = true;
				binding.InitEarly();
			}

			foreach (var binding in gameBindings) {
				binding.context = LuauContext.Game;
				binding.contextOverwritten = true;
				binding.InitEarly();
			}
		}
	}
}
