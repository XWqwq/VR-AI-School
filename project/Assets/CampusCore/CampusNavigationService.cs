using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CampusNavigationService : MonoBehaviour
{
    public static CampusNavigationService Instance { get; private set; }

    public event Action<string> NavigationStarted;
    public event Action<string> NavigationCompleted;
    public event Action NavigationCancelled;

    public bool IsNavigating { get; private set; }
    public string TargetId { get; private set; }
    public bool HasActiveWaypoint => IsNavigating && routePoints != null && waypointIndex < routePoints.Length;
    public Vector3 CurrentWaypoint => HasActiveWaypoint ? routePoints[waypointIndex] : Vector3.zero;

    [SerializeField] private float arrivalDistance = 4f;
    private Transform player;
    private Vector3 targetPosition;
    private Vector3[] routePoints;
    private int waypointIndex;
    private LineRenderer routeLine;
    private string pendingTarget;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        CreateRouteLine();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Instance = null;
    }

    private void Update()
    {
        if (!IsNavigating) return;
        if (player == null && Camera.main != null) player = Camera.main.transform;
        if (player == null) return;

        targetPosition = routePoints[waypointIndex];
        var flatPlayer = new Vector3(player.position.x, 0f, player.position.z);
        var flatTarget = new Vector3(targetPosition.x, 0f, targetPosition.z);
        UpdateRouteLine(flatPlayer);
        if (Vector3.Distance(flatPlayer, flatTarget) <= arrivalDistance)
        {
            if (waypointIndex < routePoints.Length - 1)
                waypointIndex++;
            else
                CompleteNavigation();
        }
    }

    public bool StartNavigation(string locationId)
    {
        if (string.IsNullOrWhiteSpace(locationId)) return false;
        if (CampusLocationRegistry.Instance != null &&
            CampusLocationRegistry.Instance.TryGet(locationId, out var location))
        {
            if (location.sceneIndex != SceneManager.GetActiveScene().buildIndex)
            {
                pendingTarget = locationId;
                if (CampusPortalService.Instance == null ||
                    !CampusPortalService.Instance.OpenPortal(location.sceneIndex, location.displayName))
                    PicoSceneNavigator.Instance?.LoadSceneByIndex(location.sceneIndex);
                return true;
            }
            ActivateTarget(location, true);
            return true;
        }

        var inferredScene = InferScene(locationId);
        if (inferredScene >= 0 && inferredScene != SceneManager.GetActiveScene().buildIndex)
        {
            pendingTarget = locationId;
            if (CampusPortalService.Instance == null ||
                !CampusPortalService.Instance.OpenPortal(inferredScene, "目标校园场景"))
                PicoSceneNavigator.Instance?.LoadSceneByIndex(inferredScene);
            return true;
        }
        return false;
    }

    public void CancelNavigation()
    {
        if (!IsNavigating && string.IsNullOrEmpty(pendingTarget)) return;
        IsNavigating = false;
        CampusPortalService.Instance?.CancelPortal();
        pendingTarget = null;
        TargetId = null;
        routeLine.enabled = false;
        CampusInteractionCoordinator.Instance?.ForceSetMode(CampusInteractionMode.Exploration);
        NavigationCancelled?.Invoke();
    }

    private void ActivateTarget(CampusLocationDefinition location, bool showStarGuidance)
    {
        TargetId = location.id;
        routePoints = ResolveRoute(location);
        waypointIndex = 0;
        targetPosition = routePoints[0];
        IsNavigating = true;
        routeLine.enabled = true;
        CampusInteractionCoordinator.Instance?.ForceSetMode(CampusInteractionMode.Guiding);
        NavigationStarted?.Invoke(TargetId);
    }

    private void CompleteNavigation()
    {
        var completedId = TargetId;
        IsNavigating = false;
        TargetId = null;
        routeLine.enabled = false;
        CampusFeedbackService.Instance?.TaskComplete();
        CampusInteractionCoordinator.Instance?.ForceSetMode(CampusInteractionMode.Exploration);
        NavigationCompleted?.Invoke(completedId);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        routeLine.enabled = false;
        if (!string.IsNullOrEmpty(pendingTarget))
            StartCoroutine(ResolvePendingTarget());
    }

    private IEnumerator ResolvePendingTarget()
    {
        yield return null;
        var id = pendingTarget;
        pendingTarget = null;
        if (CampusLocationRegistry.Instance != null &&
            CampusLocationRegistry.Instance.TryGet(id, out var location))
            ActivateTarget(location, false);
    }

    private void CreateRouteLine()
    {
        routeLine = gameObject.AddComponent<LineRenderer>();
        routeLine.useWorldSpace = true;
        routeLine.positionCount = 2;
        routeLine.startWidth = 0.045f;
        routeLine.endWidth = 0.095f;
        routeLine.startColor = new Color(1f, 0.45f, 0.04f, 0.3f);
        routeLine.endColor = new Color(1f, 0.9f, 0.2f, 0.95f);
        routeLine.material = new Material(Shader.Find("Sprites/Default"));
        routeLine.enabled = false;
    }

    private static Vector3 SampleRoute(Vector3[] points, float distance)
    {
        for (var i = 1; i < points.Length; i++)
        {
            var segment = Vector3.Distance(points[i - 1], points[i]);
            if (distance <= segment)
                return Vector3.Lerp(points[i - 1], points[i], segment > 0f ? distance / segment : 0f);
            distance -= segment;
        }
        return points[points.Length - 1];
    }

    private Vector3[] ResolveRoute(CampusLocationDefinition location)
    {
        foreach (var path in FindObjectsOfType<CampusNavigationPath>(true))
        {
            if (path.targetLocationId == location.id)
                return path.GetWorldPoints(location.navigationTarget);
        }
        return new[] { location.navigationTarget };
    }

    private void UpdateRouteLine(Vector3 flatPlayer)
    {
        var remaining = routePoints.Length - waypointIndex;
        routeLine.positionCount = remaining + 1;
        routeLine.SetPosition(0, flatPlayer + Vector3.up * 0.05f);
        for (var i = 0; i < remaining; i++)
            routeLine.SetPosition(i + 1, routePoints[waypointIndex + i] + Vector3.up * 0.05f);
    }

    private static int InferScene(string id)
    {
        if (id.StartsWith("main_", StringComparison.OrdinalIgnoreCase)) return 0;
        if (id.StartsWith("classroom_", StringComparison.OrdinalIgnoreCase)) return 1;
        if (id.StartsWith("exhibition_", StringComparison.OrdinalIgnoreCase)) return 2;
        return -1;
    }
}
