using UnityEngine;

public sealed class CampusMemoryService : MonoBehaviour
{
    public static CampusMemoryService Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public string BuildSummary()
    {
        var progress = CampusProgressService.Instance;
        var profile = CampusUserProfileService.Instance;
        if (progress == null) return "AI 记忆尚未初始化。";
        return "AI 当前记住的内容：\n\n" +
               "兴趣方向：" + (profile != null ? profile.InterestName : "尚未选择") + "\n" +
               "去过的场景：" + progress.Data.visitedScenes.Count + " 个\n" +
               "完成的地点：" + progress.Data.completedLocationIds.Count + " 个\n" +
               "提问次数：" + progress.Data.questionCount + " 次\n" +
               "隐藏记录：" + progress.Data.unlockedSecretIds.Count + " 个\n\n" +
               "这些信息只保存在本机进度文件中，用于续接任务和个性化推荐。";
    }

    public void Clear()
    {
        CampusProgressService.Instance?.ClearAllPersonalProgress();
        CampusUserProfileService.Instance?.ResetProfile();
        CampusJourneyService.Instance?.ResetJourney();
        CampusNavigationService.Instance?.CancelNavigation();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }
}
