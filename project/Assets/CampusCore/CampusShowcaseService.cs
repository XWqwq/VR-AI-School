using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

public sealed class CampusShowcaseService : MonoBehaviour
{
    public static CampusShowcaseService Instance { get; private set; }

    [Header("新生展示模式")]
    [SerializeField] private bool enableShowcaseMode = true;
    [SerializeField] private float idleResetSeconds = 90f;
    [SerializeField] private int welcomeSceneBuildIndex;

    public bool IsEnabled => enableShowcaseMode;
    public float IdleSeconds => Time.unscaledTime - lastActivityTime;

    private float lastActivityTime;
    private Vector3 lastHeadPosition;
    private Quaternion lastHeadRotation;
    private bool hasHeadPose;
    private bool isResetting;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        lastActivityTime = Time.unscaledTime;
    }

    private void Start()
    {
        if (enableShowcaseMode)
            CampusProgressService.Instance?.BeginShowcaseMode();
    }

    private void Update()
    {
        if (!enableShowcaseMode || isResetting)
            return;
        DetectActivity();
        if (IdleSeconds >= idleResetSeconds)
            ResetExperience();
    }

    public void ReportActivity()
    {
        lastActivityTime = Time.unscaledTime;
    }

    public void ResetExperience()
    {
        if (isResetting)
            return;
        isResetting = true;
        CampusProgressService.Instance?.ResetShowcaseRound();
        CampusUserProfileService.Instance?.ResetProfile();
        CampusJourneyService.Instance?.ResetJourney();
        CampusNavigationService.Instance?.CancelNavigation();
        AdmissionLetterGuide.Instance?.ResetForShowcase();
        CampusInteractionCoordinator.Instance?.TrySetMode(CampusInteractionMode.Exploration);
        lastActivityTime = Time.unscaledTime;

        if (SceneManager.GetActiveScene().buildIndex != welcomeSceneBuildIndex)
            PicoSceneNavigator.Instance?.LoadSceneByIndex(welcomeSceneBuildIndex);
        isResetting = false;
    }

    private void DetectActivity()
    {
        var camera = Camera.main;
        if (camera != null)
        {
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            if (!hasHeadPose || Vector3.Distance(position, lastHeadPosition) > 0.012f ||
                Quaternion.Angle(rotation, lastHeadRotation) > 1.2f)
                ReportActivity();
            lastHeadPosition = position;
            lastHeadRotation = rotation;
            hasHeadPose = true;
        }

        if (HasControllerActivity(XRNode.LeftHand) || HasControllerActivity(XRNode.RightHand))
            ReportActivity();
    }

    private static bool HasControllerActivity(XRNode node)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid)
            return false;
        if (device.TryGetFeatureValue(CommonUsages.primaryButton, out var primary) && primary)
            return true;
        if (device.TryGetFeatureValue(CommonUsages.secondaryButton, out var secondary) && secondary)
            return true;
        if (device.TryGetFeatureValue(CommonUsages.trigger, out var trigger) && trigger > 0.1f)
            return true;
        if (device.TryGetFeatureValue(CommonUsages.grip, out var grip) && grip > 0.1f)
            return true;
        return device.TryGetFeatureValue(CommonUsages.primary2DAxis, out var axis) &&
               axis.sqrMagnitude > 0.02f;
    }

    private void OnApplicationQuit()
    {
        CampusProgressService.Instance?.EndShowcaseMode();
    }
}
