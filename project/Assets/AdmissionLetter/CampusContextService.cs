using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CampusContextService : MonoBehaviour
{
    public static CampusContextService Instance { get; private set; }

    public event Action ContextChanged;

    public string SceneName { get; private set; }
    public string LocationId { get; private set; } = "none";
    public string LocationName { get; private set; } = "尚未识别地点";
    public string LocationDescription { get; private set; } = string.Empty;
    public float LastFocusTime { get; private set; } = -999f;

    public bool HasRecentFocus => Time.unscaledTime - LastFocusTime <= 15f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneName = SceneManager.GetActiveScene().name;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneName = scene.name;
        LocationId = "none";
        LocationName = "尚未识别地点";
        LocationDescription = string.Empty;
        LastFocusTime = -999f;
        ContextChanged?.Invoke();
    }

    public void FocusLocation(string id, string displayName, string description)
    {
        LocationId = string.IsNullOrWhiteSpace(id) ? MakeStableId(displayName) : id;
        LocationName = string.IsNullOrWhiteSpace(displayName) ? "未命名地点" : displayName;
        LocationDescription = description ?? string.Empty;
        LastFocusTime = Time.unscaledTime;
        ContextChanged?.Invoke();
    }

    public DifyChatRequest CreateRequest(string query, string userId, string conversationId)
    {
        return new DifyChatRequest
        {
            query = query,
            user = userId,
            conversation_id = conversationId,
            inputs = new DifyChatInputs
            {
                scene_name = SceneName,
                location_id = HasRecentFocus ? LocationId : "none",
                location_name = HasRecentFocus ? LocationName : "无明确关注地点",
                location_description = HasRecentFocus ? LocationDescription : string.Empty,
                interaction_type = "voice",
                user_interest = CampusUserProfileService.Instance != null
                    ? CampusUserProfileService.Instance.InterestName
                    : "已取消兴趣分流",
                student_major = CampusUserProfileService.Instance != null &&
                                CampusUserProfileService.Instance.HasIdentity
                    ? CampusUserProfileService.Instance.StudentMajor
                    : "尚未录入"
            }
        };
    }

    private static string MakeStableId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "unknown"
            : value.Trim().ToLowerInvariant().Replace(" ", "_");
    }
}
