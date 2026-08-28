using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class PicoReleaseBuilder
{
    private const string PackageName = "com.suda.futurecampusvr";
    private const string ProductName = "苏州大学未来校区VR导览";

    public static void Build()
    {
        var configuredOutputRoot = Environment.GetEnvironmentVariable("PICO_BUILD_OUTPUT");
        var outputRoot = string.IsNullOrWhiteSpace(configuredOutputRoot)
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "交付"))
            : Path.GetFullPath(configuredOutputRoot);
        Directory.CreateDirectory(outputRoot);
        var apkPath = Path.Combine(outputRoot, "FutureCampusVR-PICO.apk");

        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            throw new InvalidOperationException("无法切换到 Android 构建目标。");

        PlayerSettings.productName = ProductName;
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, PackageName);
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        // PICO talks to trusted services hosted by the demo PC over the local
        // hotspot. Unity blocks these HTTP requests before Android's manifest
        // policy is consulted unless the player setting is also enabled.
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.development = false;
        AssetDatabase.SaveAssets();

        var scenes = new[]
        {
            "Assets/Scenes/Main.unity",
            "Assets/Classroom/Classroom_Scenes/Classroom.unity",
            "Assets/Scenes/ExhibitionHall.unity"
        };
        foreach (var scene in scenes)
            if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), scene)))
                throw new FileNotFoundException("构建场景不存在", scene);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = apkPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.CompressWithLz4HC
        });

        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException(
                $"PICO APK 构建失败：{report.summary.result}，错误 {report.summary.totalErrors} 个。");

        Debug.Log($"PICO_RELEASE_APK={apkPath}");
        Debug.Log($"PICO_RELEASE_SIZE={report.summary.totalSize}");
    }
}
