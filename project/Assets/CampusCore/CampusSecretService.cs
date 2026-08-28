using System;
using UnityEngine;

public sealed class CampusSecretService : MonoBehaviour
{
    public static CampusSecretService Instance { get; private set; }

    public const string PatientObserver = "secret_patient_observer";
    public const string CampusExplorer = "secret_campus_explorer";
    public const string SmartLearningPath = "secret_smart_learning_path";

    public event Action<string, string> SecretRevealed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (CampusProgressService.Instance != null)
            CampusProgressService.Instance.LocationStateChanged += OnLocationStateChanged;
        EvaluateProgressSecrets();
    }

    private void OnDestroy()
    {
        if (CampusProgressService.Instance != null)
            CampusProgressService.Instance.LocationStateChanged -= OnLocationStateChanged;
        if (Instance == this)
            Instance = null;
    }

    public void ObserveLocation(string locationId, string locationName)
    {
        Unlock(PatientObserver, "细心观察者", "你持续观察了“" + locationName + "”，发现了隐藏介绍。\n试着询问 AI：这里有哪些容易错过的细节？");
    }

    public static string GetDisplayName(string secretId)
    {
        switch (secretId)
        {
            case PatientObserver: return "细心观察者";
            case CampusExplorer: return "校园探索家";
            case SmartLearningPath: return "智慧学习路线";
            default: return "隐藏彩蛋";
        }
    }

    private void OnLocationStateChanged(string locationId, CampusDiscoveryState state)
    {
        if (state == CampusDiscoveryState.Completed)
            EvaluateProgressSecrets();
    }

    private void EvaluateProgressSecrets()
    {
        var progress = CampusProgressService.Instance;
        var history = progress?.Data.completedLocationIds;
        if (history == null)
            return;

        if (history.Count >= 3)
            Unlock(CampusExplorer, "校园探索家", "你已经深入了解三个校园地点，探索图鉴新增隐藏记录。");

        var teaching = history.IndexOf("classroom_teaching_area");
        var terminal = history.IndexOf("classroom_ai_terminal");
        if (teaching >= 0 && terminal > teaching)
            Unlock(SmartLearningPath, "智慧学习路线", "你按“教学区 → AI 学习终端”的顺序完成探索，解锁智慧学习路线。");
    }

    private void Unlock(string id, string title, string description)
    {
        var progress = CampusProgressService.Instance;
        if (progress == null || progress.HasSecret(id))
            return;
        progress.UnlockSecret(id);
        CampusFeedbackService.Instance?.TaskComplete();
        SecretRevealed?.Invoke(title, description);
    }
}
