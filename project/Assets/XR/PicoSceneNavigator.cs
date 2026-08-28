using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

[RequireComponent(typeof(XROrigin))]
public class PicoSceneNavigator : MonoBehaviour
{
    public static PicoSceneNavigator Instance { get; private set; }

    private bool loading;
    private float originalCameraYOffset;
    private Vector3 initialSpawnPosition;
    private float fixedPlayerY;

    private static readonly Vector3[] SpawnPositions =
    {
        new Vector3(6.73f, 0f, -0.48f),  // Main
        new Vector3(4.65f, 0f, 16.92f),  // Classroom
        new Vector3(3.809f, 0f, -1.813f) // Exhibition Hall
    };

    private static readonly float[] SpawnYaws = { 185f, -90f, 135f };

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        originalCameraYOffset = GetComponent<XROrigin>().CameraYOffset;
        // The position authored on the active XR Origin is the game's start.
        initialSpawnPosition = transform.position;
        fixedPlayerY = transform.position.y;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    public void LoadSceneByIndex(int buildIndex)
    {
        if (loading || buildIndex < 0 ||
            buildIndex >= SceneManager.sceneCountInBuildSettings)
            return;

        StartCoroutine(LoadAbsolute(buildIndex));
    }

    private IEnumerator LoadAbsolute(int buildIndex)
    {
        loading = true;
        CampusInteractionCoordinator.Instance?.ForceSetMode(CampusInteractionMode.Loading);
        yield return SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
        loading = false;
        CampusInteractionCoordinator.Instance?.ForceSetMode(CampusInteractionMode.Exploration);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RemoveDuplicateRigs();
        DisableSceneCameras();
        ResetRigPose(scene.buildIndex);
        EnsureSceneLighting(scene.buildIndex);
    }

    private void RemoveDuplicateRigs()
    {
        foreach (var otherOrigin in FindObjectsOfType<XROrigin>(true))
        {
            if (otherOrigin.gameObject != gameObject)
                Destroy(otherOrigin.gameObject);
        }
    }

    private void DisableSceneCameras()
    {
        foreach (var camera in FindObjectsOfType<Camera>(true))
        {
            if (!camera.transform.IsChildOf(transform))
                camera.gameObject.SetActive(false);
        }

        foreach (var listener in FindObjectsOfType<AudioListener>(true))
        {
            if (!listener.transform.IsChildOf(transform))
                listener.enabled = false;
        }
    }

    private void ResetRigPose(int buildIndex)
    {
        if (buildIndex < 0 || buildIndex >= SpawnPositions.Length)
            return;

        var character = GetComponent<CharacterController>();
        if (character != null)
            character.enabled = false;

        var position = buildIndex == 0 ? initialSpawnPosition : SpawnPositions[buildIndex];
        // Classroom and exhibition keep their original authored height. The
        // locomotion service then locks that scene-specific Y value.
        if (buildIndex == 0) position.y = fixedPlayerY;
        var locomotion = GetComponent<PicoLocomotion>();
        locomotion?.SetFixedHeight(position.y);
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, SpawnYaws[buildIndex], 0f));

        // The outdoor main scene was authored from the avatar's eye line. Raise
        // only that scene; classroom and exhibition retain their original height.
        GetComponent<XROrigin>().CameraYOffset = buildIndex == 0 ? 1.82f : originalCameraYOffset;

        if (character != null)
            character.enabled = true;
    }

    private void EnsureSceneLighting(int buildIndex)
    {
        if (buildIndex != 2)
            return;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.60f, 0.68f);
        RenderSettings.ambientEquatorColor = new Color(0.32f, 0.34f, 0.38f);
        RenderSettings.ambientGroundColor = new Color(0.16f, 0.16f, 0.18f);
        RenderSettings.ambientIntensity = 1.15f;

        bool hasDirectionalLight = false;
        foreach (var light in FindObjectsOfType<Light>(true))
        {
            if (light.type == LightType.Directional && light.isActiveAndEnabled)
            {
                hasDirectionalLight = true;
                break;
            }
        }

        if (!hasDirectionalLight)
        {
            var lightObject = new GameObject("Exhibition Hall Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.15f;
            light.color = new Color(1f, 0.96f, 0.90f);
            light.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        }
    }
}
