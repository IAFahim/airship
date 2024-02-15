using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Code.Bootstrap;
using Luau;
using Proyecto26;
using RSG;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

public class MainMenuSceneManager : MonoBehaviour {
    public static string cdnUrl = "https://gcdn-staging.easy.gg";
    public static string deploymentUrl = "https://deployment-service-fxy2zritya-uc.a.run.app";
    public AirshipEditorConfig editorConfig;

    private void Start() {
        var savedAccount = AuthManager.GetSavedAccount();
        if (savedAccount == null) {
            SceneManager.LoadScene("Login");
            return;
        }
        StartCoroutine(this.StartLoadingCoroutine());
    }

    private IEnumerator RetryAfterSeconds(float seconds) {
        yield return new WaitForSeconds(seconds);
        yield return this.StartLoadingCoroutine();
    }

    private IEnumerator StartLoadingCoroutine() {
        var isUsingBundles = false;
        new Promise((resolve, reject) => {
            isUsingBundles = SystemRoot.Instance.IsUsingBundles(this.editorConfig);
            resolve();
        }).Then(() => {
            Promise<List<string>> promise = new Promise<List<string>>();
            if (isUsingBundles) {
                List<IPromise<PackageLatestVersionResponse>> promises = new();
                promises.Add(GetLatestPackageVersion("@Easy/Core"));
                promises.Add(GetLatestPackageVersion("@Easy/CoreMaterials"));
                PromiseHelpers.All(promises[0], promises[1]).Then((results) => {
                    promise.Resolve(new List<string>() {
                        results.Item1.package.assetVersionNumber + "",
                        results.Item2.package.assetVersionNumber + ""
                    });
                }).Catch((err) => {
                    promise.Reject(err);
                });
            } else {
                promise.Resolve(new List<string>() {
                    "LocalBuild",
                    "LocalBuild"
                });
            }

            return promise;
        }).Then((versions) => {
            var corePackageVersion = versions[0];
            var coreMaterialsPackageVersion = versions[1];
            Debug.Log($"@Easy/Core: {versions[0]}, @Easy/CoreMaterials: {versions[1]}");
            List<AirshipPackage> packages = new();
            packages.Add(new AirshipPackage("@Easy/Core", corePackageVersion, AirshipPackageType.Package));
            packages.Add(new AirshipPackage("@Easy/CoreMaterials", coreMaterialsPackageVersion, AirshipPackageType.Package));
            if (isUsingBundles) {
                StartCoroutine(this.StartPackageDownload(packages));
            } else {
                StartCoroutine(this.StartPackageLoad(packages, isUsingBundles));
            }
        }).Catch((err) => {
            Debug.LogError("Failed to load core packages: " + err);
            Debug.Log("Retrying in 0.5s...");
            StartCoroutine(this.RetryAfterSeconds(0.5f));
        });
        yield break;
    }

    private IEnumerator StartPackageDownload(List<AirshipPackage> packages) {
        var loadingScreen = FindAnyObjectByType<MainMenuLoadingScreen>();
        yield return BundleDownloader.Instance.DownloadBundles(cdnUrl, packages.ToArray(), null, loadingScreen);
        yield return StartPackageLoad(packages, true);
    }

    private IEnumerator StartPackageLoad(List<AirshipPackage> packages, bool usingBundles) {
        var st = Stopwatch.StartNew();
        yield return SystemRoot.Instance.LoadPackages(packages, usingBundles);
        Debug.Log($"Finished loading main menu packages in {st.ElapsedMilliseconds} ms.");

        Application.targetFrameRate = 140;

        var coreLuauBindingGO = new GameObject("CoreLuauBinding");
        var coreLuauBinding = coreLuauBindingGO.AddComponent<ScriptBinding>();
        coreLuauBinding.SetScriptFromPath("@Easy/Core/shared/resources/ts/mainmenu.lua", LuauSecurityContext.Core);
        coreLuauBinding.Init();
    }

    public static IPromise<PackageLatestVersionResponse> GetLatestPackageVersion(string packageId) {
        var url = $"{deploymentUrl}/package-versions/packageSlug/{packageId}";

        return RestClient.Get<PackageLatestVersionResponse>(new RequestHelper() {
            Uri = url
        });
    }
}