using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CampusJourneyService : MonoBehaviour
{
    public static CampusJourneyService Instance { get; private set; }
    public event Action JourneyChanged;

    public bool IsEnabled { get; private set; } = true;
    public int StepCount => StepTitles.Length;
    public int CompletedStepCount { get; private set; }
    public bool IsComplete => CompletedStepCount >= StepCount;
    public string CurrentStepTitle => IsComplete ? "新生第一日已完成" : StepTitles[CompletedStepCount];
    public string CurrentStepDuration => IsComplete ? "可以自由探索或生成纪念总结" : StepDurations[CompletedStepCount];

    private static readonly string[] StepTitles =
    {
        "起点：东吴门前完成操作熟悉",
        "第一站：参观科创文化连廊展板",
        "第二站：进入科技展厅并完成展项",
        "第三站：进入智慧教室并完成互动点"
    };

    private static readonly string[] StepDurations =
    {
        "约 2 分钟", "约 2 分钟", "约 2 分钟", "约 2 分钟"
    };

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        if (CampusTaskService.Instance != null) CampusTaskService.Instance.TaskCompleted += OnTaskChanged;
        if (CampusUserProfileService.Instance != null) CampusUserProfileService.Instance.ProfileChanged += Evaluate;
        if (CampusProgressService.Instance != null)
        {
            CampusProgressService.Instance.ProgressChanged += Evaluate;
            CampusProgressService.Instance.LocationStateChanged += OnLocationChanged;
        }
        Evaluate();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (CampusTaskService.Instance != null) CampusTaskService.Instance.TaskCompleted -= OnTaskChanged;
        if (CampusUserProfileService.Instance != null) CampusUserProfileService.Instance.ProfileChanged -= Evaluate;
        if (CampusProgressService.Instance != null)
        {
            CampusProgressService.Instance.ProgressChanged -= Evaluate;
            CampusProgressService.Instance.LocationStateChanged -= OnLocationChanged;
        }
        if (Instance == this) Instance = null;
    }

    public void ToggleJourney() { IsEnabled = !IsEnabled; JourneyChanged?.Invoke(); }
    public void ResetJourney() { IsEnabled = true; Evaluate(); }
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Evaluate();
    private void OnTaskChanged(CampusTaskDefinition task) => Evaluate();
    private void OnLocationChanged(string id, CampusDiscoveryState state) => Evaluate();

    private void Evaluate()
    {
        var previous = CompletedStepCount;
        CompletedStepCount = 0;
        while (CompletedStepCount < StepCount && IsStepComplete(CompletedStepCount)) CompletedStepCount++;
        if (previous != CompletedStepCount) JourneyChanged?.Invoke();
    }

    private bool IsStepComplete(int step)
    {
        var progress = CampusProgressService.Instance;
        if (progress == null) return false;
        switch (step)
        {
            case 0: return progress.IsTaskCompleted("tutorial_start_navigation");
            case 1: return HasCompletedLocation(progress, "main_innovation_corridor");
            case 2: return progress.Data.visitedScenes.Contains(2) && HasCompletedLocation(progress, "exhibition_");
            case 3: return progress.Data.visitedScenes.Contains(1) && HasCompletedLocation(progress, "classroom_");
            default: return false;
        }
    }

    private static bool HasCompletedLocation(CampusProgressService progress, string prefix)
    {
        foreach (var location in progress.Data.locations)
            if (location.locationId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                location.state == CampusDiscoveryState.Completed) return true;
        return false;
    }
}
