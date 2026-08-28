using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using Unity.XR.CoreUtils;
using Unity.XR.PXR;

public static class PicoPdcSetup
{
    private const string MainScene = "Assets/Scenes/Main.unity";

    [InitializeOnLoadMethod]
    private static void RepairTrackingAfterReload()
    {
        EditorApplication.update -= RepairLivePreviewWhenEditorIsReady;
        EditorApplication.update += RepairLivePreviewWhenEditorIsReady;
    }

    private static void RepairLivePreviewWhenEditorIsReady()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying ||
            Application.isPlaying)
            return;

        EditorApplication.update -= RepairLivePreviewWhenEditorIsReady;

        RegisterXRSettingsAsset();
        EnsureControllerMaterials();

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
        {
            Debug.Log("PICO Live Preview requires Windows Standalone; switching active build target.");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                Debug.LogError("PICO setup: failed to switch active build target to Windows Standalone.");
                return;
            }
        }

        EnsureMainSceneCameraTracking();
    }

    private static void EnsureControllerMaterials()
    {
        const string folder = "Assets/Resources/PICO";
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Resources", "PICO");

        CreateControllerMaterial(
            folder + "/ControllerLeft.mat",
            new Color(0.12f, 0.45f, 0.95f, 1f));
        CreateControllerMaterial(
            folder + "/ControllerRight.mat",
            new Color(0.95f, 0.35f, 0.18f, 1f));
        CreateControllerMaterial(
            folder + "/ControllerRay.mat",
            new Color(0.15f, 0.85f, 1f, 0.95f));
    }

    private static void CreateControllerMaterial(string path, Color color)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogError("PICO controller material: no compatible URP shader found.");
                return;
            }

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
    }

    private static void RegisterXRSettingsAsset()
    {
        var perTarget = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>)
            .FirstOrDefault();

        if (perTarget != null)
        {
            EditorBuildSettings.AddConfigObject(
                XRGeneralSettings.k_SettingsKey, perTarget, true);
        }
    }

    public static void Configure()
    {
        ConfigureLoaders();
        ConfigurePlayer();
        ConfigureMainScene();
        AssetDatabase.SaveAssets();
        Debug.Log("PICO_PDC_SETUP_COMPLETE");
    }

    private static void ConfigureLoaders()
    {
        var perTarget = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>)
            .FirstOrDefault();

        if (perTarget == null)
        {
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XRGeneralSettingsPerBuildTarget.asset");
        }

        ConfigureLoader(perTarget, BuildTargetGroup.Standalone,
            "Unity.XR.PICO.LivePreview.PXR_PTLoader");
        ConfigureLoader(perTarget, BuildTargetGroup.Android,
            "Unity.XR.PXR.PXR_Loader");

        EditorBuildSettings.AddConfigObject(
            XRGeneralSettings.k_SettingsKey, perTarget, true);
        EditorUtility.SetDirty(perTarget);
    }

    private static void ConfigureLoader(
        XRGeneralSettingsPerBuildTarget perTarget,
        BuildTargetGroup group,
        string loaderType)
    {
        var general = perTarget.SettingsForBuildTarget(group);
        if (general == null)
        {
            general = ScriptableObject.CreateInstance<XRGeneralSettings>();
            general.name = group + " Settings";
            AssetDatabase.AddObjectToAsset(general, perTarget);
            perTarget.SetSettingsForBuildTarget(group, general);
        }

        if (general.Manager == null)
        {
            var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
            manager.name = group + " Providers";
            AssetDatabase.AddObjectToAsset(manager, perTarget);
            general.Manager = manager;
        }

        foreach (var loader in general.Manager.activeLoaders.ToArray())
            XRPackageMetadataStore.RemoveLoader(general.Manager, loader.GetType().FullName, group);

        if (!XRPackageMetadataStore.AssignLoader(general.Manager, loaderType, group))
            throw new System.InvalidOperationException("Unable to assign XR loader: " + loaderType);

        general.Manager.automaticLoading = true;
        general.Manager.automaticRunning = true;
        general.InitManagerOnStart = true;
        EditorUtility.SetDirty(general);
        EditorUtility.SetDirty(general.Manager);
    }

    private static void ConfigurePlayer()
    {
        PlayerSettings.runInBackground = true;
        PlayerSettings.virtualRealitySplashScreen = null;
        var playerSettingsAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (playerSettingsAssets.Length > 0)
        {
            var serializedSettings = new SerializedObject(playerSettingsAssets[0]);
            var inputHandler = serializedSettings.FindProperty("activeInputHandler");
            if (inputHandler != null)
            {
                // Both: retain compatibility with the project's legacy Input API while
                // enabling Live Preview's Input System HMD/controller layouts.
                inputHandler.intValue = 2;
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        PlayerSettings.SetScriptingBackend(
            UnityEditor.Build.NamedBuildTarget.Android,
            ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
    }

    private static void ConfigureMainScene()
    {
        var scene = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);

        foreach (var oldPlayer in Object.FindObjectsOfType<PlayerMovement>(true))
            Object.DestroyImmediate(oldPlayer.gameObject);
        foreach (var oldLook in Object.FindObjectsOfType<MouseLook>(true))
            Object.DestroyImmediate(oldLook);

        foreach (var oldCamera in Object.FindObjectsOfType<Camera>(true))
        {
            if (oldCamera.GetComponentInParent<XROrigin>() == null)
                Object.DestroyImmediate(oldCamera.gameObject);
        }

        foreach (var origin in Object.FindObjectsOfType<XROrigin>(true))
            Object.DestroyImmediate(origin.gameObject);

        var rig = new GameObject("PICO XR Origin (PDC)");
        rig.transform.SetPositionAndRotation(new Vector3(6.73f, 0f, -0.48f), Quaternion.Euler(0f, 185f, 0f));

        var cameraOffset = new GameObject("Camera Offset");
        cameraOffset.transform.SetParent(rig.transform, false);

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(cameraOffset.transform, false);
        var camera = cameraObject.AddComponent<Camera>();
        camera.nearClipPlane = 0.05f;
        cameraObject.AddComponent<AudioListener>();
        ConfigureTrackedPoseDriver(cameraObject);

        var originComponent = rig.AddComponent<XROrigin>();
        originComponent.Camera = camera;
        originComponent.CameraFloorOffsetObject = cameraOffset;
        originComponent.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        rig.AddComponent<PXR_Manager>();
        rig.AddComponent<CharacterController>();
        rig.AddComponent<PicoLocomotion>();
        rig.AddComponent<PicoControllerVisuals>();
        rig.AddComponent<PicoSceneNavigator>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static void EnsureMainSceneCameraTracking()
    {
        var scene = SceneManager.GetActiveScene().path == MainScene
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);

        var origin = Object.FindObjectOfType<XROrigin>(true);
        var camera = origin != null ? origin.Camera : Camera.main;
        if (camera == null)
        {
            Debug.LogError("PICO setup: Main scene has no XR camera to configure.");
            return;
        }

        ConfigureTrackedPoseDriver(camera.gameObject);
        if (origin.GetComponent<CharacterController>() == null)
            origin.gameObject.AddComponent<CharacterController>();
        if (origin.GetComponent<PicoLocomotion>() == null)
            origin.gameObject.AddComponent<PicoLocomotion>();
        if (origin.GetComponent<PicoControllerVisuals>() == null)
            origin.gameObject.AddComponent<PicoControllerVisuals>();
        if (origin.GetComponent<PicoSceneNavigator>() == null)
            origin.gameObject.AddComponent<PicoSceneNavigator>();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("PICO_TRACKED_POSE_DRIVER_CONFIGURED");
    }

    private static void ConfigureTrackedPoseDriver(GameObject cameraObject)
    {
        var trackedPoseDriver = cameraObject.GetComponent<TrackedPoseDriver>();
        if (trackedPoseDriver == null)
            trackedPoseDriver = cameraObject.AddComponent<TrackedPoseDriver>();

        trackedPoseDriver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        trackedPoseDriver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;

        var position = new InputAction(
            "HMD Position",
            InputActionType.Value,
            "<XRHMD>/centerEyePosition",
            expectedControlType: "Vector3");
        var rotation = new InputAction(
            "HMD Rotation",
            InputActionType.Value,
            "<XRHMD>/centerEyeRotation",
            expectedControlType: "Quaternion");
        var trackingState = new InputAction(
            "HMD Tracking State",
            InputActionType.Value,
            "<XRHMD>/trackingState",
            expectedControlType: "Integer");

        trackedPoseDriver.positionInput = new InputActionProperty(position);
        trackedPoseDriver.rotationInput = new InputActionProperty(rotation);
        trackedPoseDriver.trackingStateInput = new InputActionProperty(trackingState);
        trackedPoseDriver.ignoreTrackingState = false;
        EditorUtility.SetDirty(trackedPoseDriver);
    }
}
