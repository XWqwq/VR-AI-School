using System;
using System.Collections;
using UnityEngine;

public sealed class MockDifyChatService : MonoBehaviour, IDifyChatService
{
    [SerializeField] private float simulatedDelay = 1.1f;

    public bool IsBusy { get; private set; }

    public void Send(DifyChatRequest request, Action<DifyChatResponse> onSuccess, Action<string> onError)
    {
        if (IsBusy)
        {
            onError?.Invoke("通知书正在思考，请稍候。 ");
            return;
        }

        StartCoroutine(Reply(request, onSuccess));
    }

    private IEnumerator Reply(DifyChatRequest request, Action<DifyChatResponse> onSuccess)
    {
        IsBusy = true;
        yield return new WaitForSecondsRealtime(simulatedDelay);

        var location = request.inputs != null ? request.inputs.location_name : "当前地点";
        var question = string.IsNullOrWhiteSpace(request.query) ? "请介绍这里" : request.query;
        var command = BuildMockCommand(question, location,
            request.inputs != null ? request.inputs.location_id : string.Empty);
        onSuccess?.Invoke(new DifyChatResponse
        {
            answer = command.speech,
            conversation_id = string.IsNullOrEmpty(request.conversation_id)
                ? Guid.NewGuid().ToString("N")
                : request.conversation_id,
            message_id = Guid.NewGuid().ToString("N"),
            command = command
        });

        IsBusy = false;
    }

    private static DifyActionPayload BuildMockCommand(string question, string location, string locationId)
    {
        var action = "none";
        var target = string.Empty;
        var page = string.Empty;
        var speech = string.Empty;
        var emotion = "neutral";

        if (ContainsAny(question, "你好", "您好", "嗨", "谢谢", "再见"))
        {
            speech = "你好，我是录取通知书校园向导。你可以看向建筑询问介绍，也可以让我打开地图或规划路线。";
            emotion = "happy";
        }
        else if (ContainsAny(question, "股票", "投资", "电影剧情", "做饭", "彩票", "医疗诊断", "写代码"))
        {
            speech = "这个问题与校园导览无关。我们聊聊学校、专业、校园设施或新生生活吧。";
        }
        else if (ContainsAny(question, "明天有多少人", "一定会", "实时人数", "最新排名", "明年新增"))
        {
            speech = "当前测试知识库没有足够资料，我不能确定答案。你可以换一个校园问题，或咨询现场老师。";
            emotion = "thinking";
        }
        else if (ContainsAny(question, "打开地图", "校园地图", "看看地图"))
        {
            speech = "好的，已经为你打开校园地图。";
            action = "open_page";
            page = "map";
            emotion = "happy";
        }
        else if (ContainsAny(question, "容易晕", "舒适模式", "不要平动", "只用瞬移"))
        {
            speech = "已切换为舒适模式，关闭平滑移动，保留瞬移和分段转向。";
            action = "set_comfort_mode";
            emotion = "happy";
        }
        else if (ContainsAny(question, "带我去", "导航到", "怎么去", "前往"))
        {
            target = ResolveMockTarget(question, locationId);
            if (string.IsNullOrEmpty(target))
            {
                speech = "我还不能确定你想去哪个地点，请说出建筑或展项名称。";
                emotion = "thinking";
            }
            else
            {
                speech = "好的，正在为你生成测试导航路线。";
                action = "navigate";
                emotion = "happy";
            }
        }
        else
        {
            speech = $"你当前关注的是{location}。这里显示的是已标注测试介绍，正式内容之后会替换为学校审核过的知识库资料。";
            emotion = "happy";
        }

        return new DifyActionPayload
        {
            speech = speech,
            emotion = emotion,
            action = action,
            target_id = target,
            page_id = page,
            scene_index = -1,
            suggestions = new[] { "介绍一下这里", "打开校园地图" }
        };
    }

    private static string ResolveMockTarget(string question, string currentLocationId)
    {
        if (CampusLocationRegistry.Instance != null)
        {
            foreach (var location in CampusLocationRegistry.Instance.All)
                if (!string.IsNullOrWhiteSpace(location.displayName) &&
                    question.IndexOf(location.displayName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return location.id;
        }
        if (question.Contains("教室")) return "classroom_teaching_area";
        if (question.Contains("展厅")) return "exhibition_entrance";
        if (question.Contains("教学中心")) return "main_teaching_center";
        return currentLocationId;
    }

    private static bool ContainsAny(string value, params string[] keywords)
    {
        foreach (var keyword in keywords)
            if (value.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }
}
