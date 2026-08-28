using System;
using UnityEngine;

public sealed class CampusUserProfileService : MonoBehaviour
{
    public static CampusUserProfileService Instance { get; private set; }
    public event Action ProfileChanged;

    public string InterestId { get; private set; } = string.Empty;
    public string InterestName { get; private set; } = "尚未选择";
    public bool HasInterest => !string.IsNullOrEmpty(InterestId);
    public string StudentName => CampusProgressService.Instance != null
        ? CampusProgressService.Instance.Data.studentName : string.Empty;
    public string StudentMajor => CampusProgressService.Instance != null
        ? CampusProgressService.Instance.Data.studentMajor : string.Empty;
    public bool HasIdentity => !string.IsNullOrWhiteSpace(StudentName) &&
                               !string.IsNullOrWhiteSpace(StudentMajor);

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

    public bool SelectInterest(string interestId)
    {
        if (!TryResolveInterest(interestId, out var displayName))
            return false;
        InterestId = interestId;
        InterestName = displayName;
        ProfileChanged?.Invoke();
        return true;
    }

    public void ResetProfile()
    {
        InterestId = string.Empty;
        InterestName = "尚未选择";
        ProfileChanged?.Invoke();
    }

    public bool SetIdentity(string studentName, string studentMajor)
    {
        studentName = CleanIdentityValue(studentName, 12);
        studentMajor = CleanIdentityValue(studentMajor, 24);
        if (string.IsNullOrEmpty(studentName) || string.IsNullOrEmpty(studentMajor) ||
            CampusProgressService.Instance == null)
            return false;
        CampusProgressService.Instance.Data.studentName = studentName;
        CampusProgressService.Instance.Data.studentMajor = studentMajor;
        CampusProgressService.Instance.Save();
        ProfileChanged?.Invoke();
        return true;
    }

    private static string CleanIdentityValue(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        value = value.Trim().Trim('，', ',', '。', '.', '！', '!', '？', '?');
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    public static bool TryResolveInterest(string id, out string displayName)
    {
        switch (id)
        {
            case "data_science_big_data": displayName = "数据科学与大数据技术"; return true;
            case "artificial_intelligence": displayName = "人工智能"; return true;
            case "robotics_engineering": displayName = "机器人工程"; return true;
            case "mechatronic_engineering": displayName = "机电工程"; return true;
            default: displayName = string.Empty; return false;
        }
    }
}
