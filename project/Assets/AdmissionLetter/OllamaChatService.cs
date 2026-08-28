using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public sealed class OllamaRuntimeConfig
{
    public bool enabled = true;
    public string base_url = "http://localhost:11434";
    public string model = "qwen2.5:7b";
    public int timeout_seconds = 120;
    public int max_history_turns = 4;
}

[Serializable]
internal sealed class OllamaMessage
{
    public string role;
    public string content;

    public OllamaMessage(string messageRole, string messageContent)
    {
        role = messageRole;
        content = messageContent;
    }
}

[Serializable]
internal sealed class OllamaChatPayload
{
    public string model;
    public OllamaMessage[] messages;
    public bool stream;
    public string format = "json";
}

[Serializable]
internal sealed class OllamaChatResult
{
    public OllamaMessage message;
}

public sealed class OllamaChatService : MonoBehaviour, IDifyChatService
{
    private readonly List<OllamaMessage> history = new List<OllamaMessage>();
    private OllamaRuntimeConfig config;

    public bool IsBusy { get; private set; }
    public bool IsConfigured => config != null && config.enabled &&
                                !string.IsNullOrWhiteSpace(config.base_url) &&
                                !string.IsNullOrWhiteSpace(config.model);
    public string ConfigPath => Path.Combine(Application.persistentDataPath, "ollama_config.json");

    private void Awake() => LoadConfig();

    public void Send(DifyChatRequest request, Action<DifyChatResponse> onSuccess, Action<string> onError)
    {
        if (IsBusy)
        {
            onError?.Invoke("AI 正在处理上一条问题，请稍候。");
            return;
        }
        if (!IsConfigured)
        {
            onError?.Invoke("Ollama 尚未配置，请检查：" + ConfigPath);
            return;
        }
        StartCoroutine(SendRequest(request, onSuccess, onError));
    }

    private IEnumerator SendRequest(DifyChatRequest request, Action<DifyChatResponse> onSuccess,
        Action<string> onError)
    {
        IsBusy = true;
        var messages = new List<OllamaMessage>
        {
            new OllamaMessage("system", BuildSystemPrompt(request != null ? request.inputs : null))
        };
        messages.AddRange(history);
        messages.Add(new OllamaMessage("user", request == null ? string.Empty : request.query));
        var payload = new OllamaChatPayload
        {
            model = config.model,
            messages = messages.ToArray(),
            stream = false
        };

        using (var webRequest = new UnityWebRequest(
                   config.base_url.TrimEnd('/') + "/api/chat", UnityWebRequest.kHttpVerbPOST))
        {
            webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload)));
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.timeout = Mathf.Clamp(config.timeout_seconds, 10, 180);
            webRequest.SetRequestHeader("Content-Type", "application/json");
            UnityWebRequestAsyncOperation operation;
            try
            {
                operation = webRequest.SendWebRequest();
            }
            catch (Exception exception)
            {
                IsBusy = false;
                onError?.Invoke("本机 Ollama 请求无法启动：" + exception.Message);
                yield break;
            }
            yield return operation;

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                var detail = string.IsNullOrWhiteSpace(webRequest.downloadHandler.text)
                    ? webRequest.error
                    : webRequest.downloadHandler.text;
                IsBusy = false;
                onError?.Invoke("本机 Ollama 请求失败：" + detail);
                yield break;
            }

            try
            {
                var result = JsonUtility.FromJson<OllamaChatResult>(webRequest.downloadHandler.text);
                if (result == null || result.message == null || string.IsNullOrWhiteSpace(result.message.content))
                    throw new InvalidOperationException("返回内容为空");
                var response = ParseResponse(result.message.content, request);
                Remember(request != null ? request.query : string.Empty, response.answer);
                IsBusy = false;
                onSuccess?.Invoke(response);
            }
            catch (Exception exception)
            {
                IsBusy = false;
                onError?.Invoke("Ollama 返回格式错误：" + exception.Message);
            }
        }
    }

    private static string BuildSystemPrompt(DifyChatInputs inputs)
    {
        var scene = inputs != null ? inputs.scene_name : "未知场景";
        var location = inputs != null ? inputs.location_name : "无明确关注地点";
        var description = inputs != null ? inputs.location_description : string.Empty;
        var major = inputs != null ? inputs.student_major : "尚未录入";
        return "你是AI增强VR虚拟校园的新生引导精灵，回答简洁自然，适合语音播报。\n" +
               "区分campus校园问题、casual问候闲聊、unrelated明显无关问题。无关问题不直接回答，要引回校园。\n" +
               "校园事实只允许使用下方已知信息；资料不足必须说目前资料中无法确定，禁止编造。\n" +
               "可确认专业只有：数据科学与大数据技术、人工智能、机器人工程、机电工程。\n" +
               "当前场景：" + scene + "\n当前注视地点：" + location +
               "\n地点已知说明：" + (string.IsNullOrWhiteSpace(description) ? "暂无可靠说明" : description) +
               "\n用户专业：" + major +
               "\n请结合该专业调整举例、课程关注点和参观提示，但所有用户仍需完整游览全部场景。\n" +
               "只输出JSON，不要Markdown：{\"intent\":\"campus|casual|unrelated\"," +
               "\"grounded\":true或false,\"answer\":\"中文回答\",\"emotion\":\"neutral|happy|curious\"," +
               "\"action\":\"none\",\"target_id\":\"\",\"suggestions\":[\"建议1\",\"建议2\"]}";
    }

    private static DifyChatResponse ParseResponse(string output, DifyChatRequest request)
    {
        CampusAiEnvelope envelope = null;
        try { envelope = JsonUtility.FromJson<CampusAiEnvelope>(StripCodeFence(output)); }
        catch { }

        var fallback = "我暂时无法确认这个问题。你可以询问当前建筑、四个专业或新生参观相关内容。";
        var answer = fallback;
        if (envelope != null && !string.IsNullOrWhiteSpace(envelope.intent))
        {
            var intent = envelope.intent.Trim().ToLowerInvariant();
            if (intent == "unrelated")
                answer = string.IsNullOrWhiteSpace(envelope.answer)
                    ? "这个问题与校园导览无关，我们聊聊学校、专业或当前参观地点吧。"
                    : envelope.answer;
            else if (intent == "campus" && !envelope.grounded)
                answer = "目前资料中无法确定这个问题的答案。你可以询问当前建筑或四个专业，也可以咨询现场老师。";
            else if ((intent == "campus" || intent == "casual") && !string.IsNullOrWhiteSpace(envelope.answer))
                answer = envelope.answer;
        }

        return new DifyChatResponse
        {
            answer = answer,
            conversation_id = request != null ? request.conversation_id : string.Empty,
            message_id = Guid.NewGuid().ToString("N"),
            command = new DifyActionPayload
            {
                speech = answer,
                emotion = envelope == null || string.IsNullOrWhiteSpace(envelope.emotion) ? "neutral" : envelope.emotion,
                action = "none",
                target_id = envelope != null ? envelope.target_id ?? string.Empty : string.Empty,
                suggestions = envelope != null ? envelope.suggestions ?? new string[0] : new string[0]
            }
        };
    }

    private void Remember(string userText, string assistantText)
    {
        history.Add(new OllamaMessage("user", userText ?? string.Empty));
        history.Add(new OllamaMessage("assistant", assistantText ?? string.Empty));
        var limit = Mathf.Clamp(config.max_history_turns, 0, 10) * 2;
        while (history.Count > limit)
            history.RemoveAt(0);
    }

    private void LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                config = JsonUtility.FromJson<OllamaRuntimeConfig>(File.ReadAllText(ConfigPath));
                ApplyLanEndpointIfNeeded();
                return;
            }
            config = new OllamaRuntimeConfig();
            ApplyLanEndpointIfNeeded();
            File.WriteAllText(ConfigPath, JsonUtility.ToJson(config, true));
            Debug.Log("已创建 Ollama 配置：" + ConfigPath);
        }
        catch (Exception exception)
        {
            config = new OllamaRuntimeConfig { enabled = false };
            Debug.LogWarning("Ollama 配置读取失败：" + exception.Message);
        }
    }

    private void ApplyLanEndpointIfNeeded()
    {
        if (config != null && CampusNetworkConfig.IsLocalhost(config.base_url))
            config.base_url = CampusNetworkConfig.ResolveOllamaBaseUrl();
    }

    private static string StripCodeFence(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal)) return trimmed;
        var firstLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLine >= 0 && lastFence > firstLine
            ? trimmed.Substring(firstLine + 1, lastFence - firstLine - 1).Trim()
            : trimmed;
    }
}
