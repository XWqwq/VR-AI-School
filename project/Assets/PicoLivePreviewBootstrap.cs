using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

public static class PicoLivePreviewBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= ConfigureScene;
        SceneManager.sceneLoaded += ConfigureScene;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ConfigureInitialScene()
    {
        ConfigureScene(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private static void ConfigureScene(Scene scene, LoadSceneMode mode)
    {
        var camera = Camera.main;
        if (camera == null)
            return;

        var driver = camera.GetComponent<TrackedPoseDriver>();
        if (driver == null)
            driver = camera.gameObject.AddComponent<TrackedPoseDriver>();

        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        driver.positionInput = new InputActionProperty(new InputAction(
            "HMD Position", InputActionType.Value, "<XRHMD>/centerEyePosition",
            expectedControlType: "Vector3"));
        driver.rotationInput = new InputActionProperty(new InputAction(
            "HMD Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation",
            expectedControlType: "Quaternion"));
        driver.trackingStateInput = new InputActionProperty(new InputAction(
            "HMD Tracking State", InputActionType.Value, "<XRHMD>/trackingState",
            expectedControlType: "Integer"));
        driver.ignoreTrackingState = false;

        if (camera.GetComponent<GazeDetector>() == null)
            camera.gameObject.AddComponent<GazeDetector>();

        var origin = camera.GetComponentInParent<XROrigin>();
        if (origin != null)
        {
            if (origin.GetComponent<CharacterController>() == null)
                origin.gameObject.AddComponent<CharacterController>();
            if (origin.GetComponent<PicoLocomotion>() == null)
                origin.gameObject.AddComponent<PicoLocomotion>();
            if (origin.GetComponent<PicoControllerVisuals>() == null)
                origin.gameObject.AddComponent<PicoControllerVisuals>();
            if (origin.GetComponent<ExhibitGrabService>() == null)
                origin.gameObject.AddComponent<ExhibitGrabService>();
            if (origin.GetComponent<PanelGrabService>() == null)
                origin.gameObject.AddComponent<PanelGrabService>();
            if (PicoSceneNavigator.Instance == null &&
                origin.GetComponent<PicoSceneNavigator>() == null)
                origin.gameObject.AddComponent<PicoSceneNavigator>();

            EnsureAdmissionLetter(camera.transform);
            EnsureTeleportFloor(origin.transform);
        }
    }

    private static void EnsureTeleportFloor(Transform origin)
    {
        if (GameObject.Find("PICO Teleport Floor Collider") != null)
            return;

        var floor = new GameObject("PICO Teleport Floor Collider");
        floor.transform.position = new Vector3(
            origin.position.x, origin.position.y - 0.08f, origin.position.z);
        var collider = floor.AddComponent<BoxCollider>();
        collider.size = new Vector3(5000f, 0.12f, 5000f);
        collider.center = Vector3.zero;
    }

    private static void EnsureAdmissionLetter(Transform head)
    {
        if (CampusInteractionCoordinator.Instance == null)
        {
            var coordinator = new GameObject("Campus Interaction Coordinator");
            coordinator.AddComponent<CampusInteractionCoordinator>();
        }

        if (CampusLocationRegistry.Instance == null)
        {
            var registry = new GameObject("Campus Location Registry");
            registry.AddComponent<CampusLocationRegistry>();
        }

        if (CampusProgressService.Instance == null)
        {
            var progress = new GameObject("Campus Progress Service");
            progress.AddComponent<CampusProgressService>();
        }

        if (CampusTaskService.Instance == null)
        {
            var tasks = new GameObject("Campus Task Service");
            tasks.AddComponent<CampusTaskService>();
        }

        if (CampusSecretService.Instance == null)
        {
            var secrets = new GameObject("Campus Secret Service");
            secrets.AddComponent<CampusSecretService>();
        }

        if (CampusSpatialBroadcastService.Instance == null)
        {
            var broadcast = new GameObject("Campus Spatial Broadcast Service");
            broadcast.AddComponent<CampusSpatialBroadcastService>();
        }

        if (CampusPortalService.Instance == null)
        {
            var portal = new GameObject("Campus Portal Service");
            portal.AddComponent<CampusPortalService>();
        }

        if (CampusAICompanionService.Instance == null)
        {
            var companion = new GameObject("Campus AI Companion Service");
            companion.AddComponent<CampusAICompanionService>();
        }

        if (CampusExperienceReportService.Instance == null)
        {
            var report = new GameObject("Campus Experience Report Service");
            report.AddComponent<CampusExperienceReportService>();
        }

        if (CampusPhotoService.Instance == null)
        {
            var photo = new GameObject("Campus Photo Service");
            photo.AddComponent<CampusPhotoService>();
        }

        if (CampusAIActionConfirmationService.Instance == null)
        {
            var confirmation = new GameObject("Campus AI Action Confirmation Service");
            confirmation.AddComponent<CampusAIActionConfirmationService>();
        }

        if (CampusAmbientFeedbackService.Instance == null)
        {
            var ambient = new GameObject("Campus Ambient Feedback Service");
            ambient.AddComponent<CampusAmbientFeedbackService>();
        }

        if (CampusVisualContextService.Instance == null)
        {
            var visualContext = new GameObject("Campus Visual Context Service");
            visualContext.AddComponent<CampusVisualContextService>();
        }

        if (CampusRealtimeGuideService.Instance == null)
        {
            var realtimeGuide = new GameObject("Campus Realtime Guide Service");
            realtimeGuide.AddComponent<CampusRealtimeGuideService>();
        }

        if (CampusHolographicAvatarPresenter.Instance == null)
        {
            var avatar = new GameObject("Campus Holographic Avatar Presenter");
            avatar.AddComponent<CampusHolographicAvatarPresenter>();
        }

        if (CampusClassroomLessonService.Instance == null)
        {
            var lesson = new GameObject("Campus Classroom Lesson Service");
            lesson.AddComponent<CampusClassroomLessonService>();
        }

        if (CampusSpatialKnowledgeGraphService.Instance == null)
        {
            var knowledgeGraph = new GameObject("Campus Spatial Knowledge Graph Service");
            knowledgeGraph.AddComponent<CampusSpatialKnowledgeGraphService>();
        }

        if (CampusGenerativeExhibitService.Instance == null)
        {
            var exhibit = new GameObject("Campus Generative Exhibit Service");
            exhibit.AddComponent<CampusGenerativeExhibitService>();
        }
        if (CampusMemoryService.Instance == null)
        {
            var memory = new GameObject("Campus Memory Service");
            memory.AddComponent<CampusMemoryService>();
        }

        if (CampusNavigationService.Instance == null)
        {
            var navigation = new GameObject("Campus Navigation Service");
            navigation.AddComponent<CampusNavigationService>();
        }

        if (AISpatialScanner.Instance == null)
        {
            var scanner = new GameObject("AI Spatial Scanner");
            scanner.AddComponent<AISpatialScanner>();
        }

        if (CampusFeedbackService.Instance == null)
        {
            var feedback = new GameObject("Campus Feedback Service");
            feedback.AddComponent<CampusFeedbackService>();
        }

        if (CampusTutorialService.Instance == null)
        {
            var tutorial = new GameObject("Campus Tutorial Service");
            tutorial.AddComponent<CampusTutorialService>();
        }

        if (UnityEngine.Object.FindObjectOfType<CampusRecoveryService>() == null)
        {
            var recovery = new GameObject("Campus Recovery Service");
            recovery.AddComponent<CampusRecoveryService>();
        }

        if (CampusTestHotspotBootstrap.Instance == null)
        {
            var testData = new GameObject("Campus Test Hotspot Bootstrap");
            testData.AddComponent<CampusTestHotspotBootstrap>();
        }

        if (CampusContextService.Instance == null)
        {
            var context = new GameObject("Campus Context Service");
            context.AddComponent<CampusContextService>();
        }

        if (CampusUserProfileService.Instance == null)
        {
            var profile = new GameObject("Campus User Profile Service");
            profile.AddComponent<CampusUserProfileService>();
        }

        if (CampusJourneyService.Instance == null)
        {
            var journey = new GameObject("Campus Journey Service");
            journey.AddComponent<CampusJourneyService>();
        }

        if (AdmissionLetterGuide.Instance == null)
            AdmissionLetterGuide.Create(head);

        // The avatar prefab includes a legacy VoiceChatClient. It is not the
        // persistent notification-book voice client, so locate only the
        // dedicated bootstrap object by name.
        if (GameObject.Find("Voice Chat Client") == null)
        {
            var voice = new GameObject("Voice Chat Client");
            voice.AddComponent<VoiceChatClient>();
        }

        if (CampusShowcaseService.Instance == null)
        {
            var showcase = new GameObject("Campus Showcase Service");
            showcase.AddComponent<CampusShowcaseService>();
        }

        if (CampusAICommandExecutor.Instance == null)
        {
            var executor = new GameObject("Campus AI Command Executor");
            executor.AddComponent<CampusAICommandExecutor>();
        }
    }
}
