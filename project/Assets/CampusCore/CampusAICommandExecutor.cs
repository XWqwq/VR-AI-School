using System;
using UnityEngine;

public sealed class CampusAICommandExecutor : MonoBehaviour
{
    public static CampusAICommandExecutor Instance { get; private set; }
    public event Action<string> ExhibitRequested;

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
        Subscribe();
    }

    private void OnDestroy()
    {
        if (AdmissionLetterGuide.Instance != null)
            AdmissionLetterGuide.Instance.CommandReceived -= OnCommandReceived;
        if (Instance == this)
            Instance = null;
    }

    private void Subscribe()
    {
        if (AdmissionLetterGuide.Instance == null)
            return;
        AdmissionLetterGuide.Instance.CommandReceived -= OnCommandReceived;
        AdmissionLetterGuide.Instance.CommandReceived += OnCommandReceived;
    }

    private void OnCommandReceived(DifyActionPayload command) => Execute(command);

    public bool Execute(DifyActionPayload command)
    {
        if (command == null || string.IsNullOrWhiteSpace(command.action) || command.action == "none")
            return true;

        var normalized = command.action.Trim().ToLowerInvariant();
        if (RequiresConfirmation(normalized))
        {
            var confirmation = CampusAIActionConfirmationService.Instance;
            if (confirmation == null || !confirmation.Request(
                    DescribeCommand(normalized, command), () => ExecuteImmediate(command)))
            {
                Debug.LogWarning("AI 操作确认面板当前不可用，已取消动作：" + normalized);
                CampusFeedbackService.Instance?.Error();
                return false;
            }
            return true;
        }

        return ExecuteImmediate(command);
    }

    private bool ExecuteImmediate(DifyActionPayload command)
    {
        var normalized = command.action.Trim().ToLowerInvariant();

        var success = false;
        switch (normalized)
        {
            case "navigate":
                success = ExecuteNavigation(command.target_id);
                break;
            case "open_page":
                success = AdmissionLetterGuide.Instance != null &&
                          AdmissionLetterGuide.Instance.ShowPageFromAI(command.page_id);
                break;
            case "switch_scene":
                success = command.scene_index >= 0 && command.scene_index <= 2 &&
                          PicoSceneNavigator.Instance != null;
                if (success)
                    PicoSceneNavigator.Instance.LoadSceneByIndex(command.scene_index);
                break;
            case "set_comfort_mode":
                var locomotion = FindObjectOfType<PicoLocomotion>();
                success = locomotion != null;
                if (success)
                    locomotion.SetComfortMode(command.comfort_mode);
                break;
            case "start_exhibit":
                success = !string.IsNullOrWhiteSpace(command.exhibit_id);
                if (success)
                    ExhibitRequested?.Invoke(command.exhibit_id);
                break;
            case "reset_experience":
                success = CampusShowcaseService.Instance != null &&
                          CampusShowcaseService.Instance.IsEnabled;
                if (success)
                    CampusShowcaseService.Instance.ResetExperience();
                break;
            default:
                Debug.LogWarning("已拦截非白名单 AI 动作：" + command.action);
                break;
        }

        if (success)
            CampusFeedbackService.Instance?.Confirm();
        else
            CampusFeedbackService.Instance?.Error();
        return success;
    }

    private static bool RequiresConfirmation(string action)
    {
        return action == "navigate" || action == "switch_scene" ||
               action == "set_comfort_mode" || action == "start_exhibit" ||
               action == "reset_experience";
    }

    private static string DescribeCommand(string action, DifyActionPayload command)
    {
        switch (action)
        {
            case "navigate":
                var destination = command.target_id;
                if (CampusLocationRegistry.Instance != null &&
                    CampusLocationRegistry.Instance.TryGet(command.target_id, out var location))
                    destination = location.displayName;
                return "开始导航到“" + (string.IsNullOrWhiteSpace(destination) ? "未指定地点" : destination) +
                       "”。跨场景时会先生成传送门，同场景会显示金色星轨。";
            case "switch_scene":
                return "切换到“" + GetSceneDisplayName(command.scene_index) +
                       "”。确认后画面会短暂变暗，再进入新场景。";
            case "set_comfort_mode":
                return command.comfort_mode
                    ? "开启舒适移动模式，转向和移动会更平稳，适合第一次体验 VR。"
                    : "关闭舒适移动模式，移动会更直接；如果容易眩晕，建议取消。";
            case "start_exhibit":
                return "启动 AI 推荐的展项“" + command.exhibit_id + "”。";
            case "reset_experience":
                return "重新开始本轮展示体验。当前临时探索进度会被清空并返回欢迎场景。";
            default:
                return "执行 AI 建议操作。";
        }
    }

    private static string GetSceneDisplayName(int index)
    {
        switch (index)
        {
            case 0: return "主校园";
            case 1: return "智慧教室";
            case 2: return "AI 展厅";
            default: return "未知场景";
        }
    }

    private static bool ExecuteNavigation(string targetId)
    {
        if (string.IsNullOrWhiteSpace(targetId) || CampusLocationRegistry.Instance == null ||
            !CampusLocationRegistry.Instance.TryGet(targetId, out _))
        {
            Debug.LogWarning("已拦截无效 AI 导航目标：" + targetId);
            return false;
        }
        return CampusNavigationService.Instance != null &&
               CampusNavigationService.Instance.StartNavigation(targetId);
    }
}
