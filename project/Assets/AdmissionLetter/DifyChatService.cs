using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public sealed class DifyRuntimeConfig
{
    public bool enabled;
    public string base_url = "http://localhost/v1";
    public string api_key = "";
    public int timeout_seconds = 30;
}

public sealed class DifyChatService : MonoBehaviour, IDifyChatService
{
    public bool IsBusy { get; private set; }
    public bool IsConfigured => config != null && config.enabled &&
                                !string.IsNullOrWhiteSpace(config.api_key) &&
                                !config.api_key.Contains("请填写");
    public string ConfigPath => Path.Combine(Application.persistentDataPath, "dify_config.json");

    private DifyRuntimeConfig config;

    private void Awake()
    {
        LoadConfig();
    }

    public void Send(DifyChatRequest request, Action<DifyChatResponse> onSuccess, Action<string> onError)
    {
        if (IsBusy)
        {
            onError?.Invoke("AI 正在处理上一条问题，请稍候。");
            return;
        }
        if (!IsConfigured)
        {
            onError?.Invoke("Dify 尚未配置。请填写：" + ConfigPath);
            return;
        }
        StartCoroutine(SendRequest(request, onSuccess, onError));
    }

    private IEnumerator SendRequest(DifyChatRequest request, Action<DifyChatResponse> onSuccess,
        Action<string> onError)
    {
        IsBusy = true;
        var endpoint = config.base_url.TrimEnd('/') + "/chat-messages";
        var json = JsonUtility.ToJson(request);
        using (var webRequest = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
        {
            webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            // A stopped demo backend used to leave the headset waiting for two
            // minutes. Thirty seconds is enough for a cold 7B response, and a
            // hard cap keeps a network failure recoverable during a demo.
            webRequest.timeout = Mathf.Clamp(config.timeout_seconds, 5, 30);
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", "Bearer " + config.api_key.Trim());
            UnityWebRequestAsyncOperation operation;
            try
            {
                operation = webRequest.SendWebRequest();
            }
            catch (Exception exception)
            {
                // SendWebRequest can throw synchronously (for example when an
                // Android build blocks local HTTP). Always release IsBusy so
                // the UI cannot remain in the Thinking state forever.
                IsBusy = false;
                onError?.Invoke("Dify 请求无法启动：" + exception.Message);
                yield break;
            }
            yield return operation;

            if (webRequest.result != UnityWebRequest.Result.Success)
            {
                var detail = string.IsNullOrWhiteSpace(webRequest.downloadHandler.text)
                    ? webRequest.error
                    : webRequest.downloadHandler.text;
                IsBusy = false;
                onError?.Invoke("Dify 请求失败：" + detail);
                yield break;
            }

            DifyChatResponse response;
            try
            {
                response = JsonUtility.FromJson<DifyChatResponse>(webRequest.downloadHandler.text);
                response = ApplyCampusSafetyPolicy(response);
            }
            catch (Exception exception)
            {
                IsBusy = false;
                onError?.Invoke("Dify 返回格式错误：" + exception.Message);
                yield break;
            }

            IsBusy = false;
            onSuccess?.Invoke(response);
        }
    }

    private static DifyChatResponse ApplyCampusSafetyPolicy(DifyChatResponse response)
    {
        if (response == null)
            throw new InvalidOperationException("响应为空");

        var payload = StripCodeFence(response.answer);
        CampusAiEnvelope envelope = null;
        try
        {
            envelope = JsonUtility.FromJson<CampusAiEnvelope>(payload);
        }
        catch
        {
            // The strict fallback below intentionally handles invalid model output.
        }

        if (envelope == null || string.IsNullOrWhiteSpace(envelope.intent))
        {
            // A normal Dify Chatflow (including an imported knowledge-base DSL)
            // commonly returns plain text instead of the optional campus JSON
            // envelope. Display that answer, but never derive a Unity action
            // from unstructured model output.
            var plainAnswer = string.IsNullOrWhiteSpace(response.answer)
                ? "校园知识库暂时没有返回内容，请换一种说法再试。"
                : response.answer.Trim();
            response.answer = plainAnswer;
            response.command = MakeCommand(plainAnswer, "neutral", "none", null, null);
            return response;
        }

        var intent = envelope.intent.Trim().ToLowerInvariant();
        string safeAnswer;
        if (intent == "unrelated")
        {
            safeAnswer = string.IsNullOrWhiteSpace(envelope.answer)
                ? "这个问题与校园导览无关。我们聊聊学校、专业、校园设施或新生生活吧。"
                : envelope.answer;
        }
        else if (intent == "campus" && !envelope.grounded)
        {
            safeAnswer = "校园知识库中暂时没有足够资料，我不能确定答案。你可以换一个学校相关问题，或咨询现场老师。";
        }
        else if ((intent == "campus" || intent == "casual") &&
                 !string.IsNullOrWhiteSpace(envelope.answer))
        {
            safeAnswer = envelope.answer;
        }
        else
        {
            safeAnswer = "我暂时不能确定答案。你可以询问学校、专业、校园地点或新生生活相关内容。";
        }

        response.answer = safeAnswer;
        response.command = MakeCommand(safeAnswer, envelope.emotion, envelope.action,
            envelope.target_id, envelope.suggestions);
        return response;
    }

    private static DifyActionPayload MakeCommand(string speech, string emotion, string action,
        string targetId, string[] suggestions)
    {
        return new DifyActionPayload
        {
            speech = speech,
            emotion = string.IsNullOrWhiteSpace(emotion) ? "neutral" : emotion,
            action = string.IsNullOrWhiteSpace(action) ? "none" : action,
            target_id = targetId ?? string.Empty,
            suggestions = suggestions ?? new string[0]
        };
    }

    private void LoadConfig()
    {
        var environmentKey = Environment.GetEnvironmentVariable("DIFY_API_KEY");
        var environmentUrl = Environment.GetEnvironmentVariable("DIFY_BASE_URL");
        if (!string.IsNullOrWhiteSpace(environmentKey))
        {
            config = new DifyRuntimeConfig
            {
                enabled = true,
                api_key = environmentKey,
                base_url = string.IsNullOrWhiteSpace(environmentUrl)
                    ? "http://localhost/v1"
                    : environmentUrl
            };
            return;
        }

        try
        {
            if (File.Exists(ConfigPath))
            {
                config = JsonUtility.FromJson<DifyRuntimeConfig>(File.ReadAllText(ConfigPath));
                ApplyLanEndpointIfNeeded();
                return;
            }
            config = new DifyRuntimeConfig();
            ApplyLanEndpointIfNeeded();
            File.WriteAllText(ConfigPath, JsonUtility.ToJson(config, true));
            Debug.Log("已创建 Dify 配置模板：" + ConfigPath);
        }
        catch (Exception exception)
        {
            config = new DifyRuntimeConfig();
            Debug.LogWarning("Dify 配置读取失败：" + exception.Message);
        }
    }

    private void ApplyLanEndpointIfNeeded()
    {
        if (config != null && CampusNetworkConfig.IsLocalhost(config.base_url))
            config.base_url = CampusNetworkConfig.ResolveDifyBaseUrl();
    }

    private static string StripCodeFence(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```"))
            return trimmed;
        var firstLine = trimmed.IndexOf('\n');
        var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
        return firstLine >= 0 && lastFence > firstLine
            ? trimmed.Substring(firstLine + 1, lastFence - firstLine - 1).Trim()
            : trimmed;
    }
}
