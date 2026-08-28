using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using WebSocketSharp;

public class VoiceChatClient : MonoBehaviour
{
    public static VoiceChatClient Instance { get; private set; }
    [Header("UI References")]
    public Text statusText;
    public Text userText;
    public Text aiText;
    public AudioSource audioSource;

    // ================== 主线程调度器 ==================
    private readonly ConcurrentQueue<System.Action> _mainThreadActions = new ConcurrentQueue<System.Action>();
    private void ExecuteOnMainThread(System.Action action)
    {
        if (action == null) return;
        _mainThreadActions.Enqueue(action);
    }

    void Update()
    {
        while (_mainThreadActions.TryDequeue(out var action))
            action?.Invoke();

        if (Input.GetKeyDown(KeyCode.Q))
            StartRecording();
        if (Input.GetKeyDown(KeyCode.E))
            StopRecordingAndSend();

        if (!isRecording && !isAsrConnecting &&
            (ws == null || ws.ReadyState != WebSocketState.Open) &&
            Time.unscaledTime >= nextAsrReconnectTime)
            ConnectToASR();
    }

    // ================== WebSocket (ASR) ==================
    private WebSocket ws;
    private bool isRecording = false;
    private AudioClip recordedClip;
    private string microphoneDevice;
    private int sampleRate = 16000;
    private float maxRecordTime = 10f;
    public string LastCaptureError { get; private set; }
    private bool isAsrConnecting;
    private float nextAsrReconnectTime;

    private string ollamaUrl = "http://localhost:11434/api/generate";
    private string ollamaModel = "qwen2.5:7b";

    private string baiduTtsUrl = "https://aip.baidubce.com/rest/2.0/tts/v1";
    private string baiduAccessToken = "";

    private System.Collections.Generic.List<string> conversationHistory = new System.Collections.Generic.List<string>();
    private int maxHistory = 5;

    void Awake()
    {
        // The teacher/avatar prefab also carries this legacy component. Only
        // the dedicated bootstrap object may own notification-book ASR.
        if (gameObject.name != "Voice Chat Client")
        {
            enabled = false;
            return;
        }
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    void Start()
    {
        Debug.Log("通知书语音客户端启动");
        baiduAccessToken = System.Environment.GetEnvironmentVariable("BAIDU_TTS_ACCESS_TOKEN") ?? "";
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.Microphone))
        {
            UnityEngine.Android.Permission.RequestUserPermission(
                UnityEngine.Android.Permission.Microphone);
            StartCoroutine(InitializePicoAudioAfterPermission());
            return;
        }
#endif
        InitializePicoAudio();
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private IEnumerator InitializePicoAudioAfterPermission()
    {
        var timeout = 4f;
        while (timeout > 0f && !UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                   UnityEngine.Android.Permission.Microphone))
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }
        InitializePicoAudio();
    }
#endif

    private void InitializePicoAudio()
    {
        SelectPicoMicrophone();
        ConnectToASR();
    }

    void SelectPicoMicrophone()
    {
        microphoneDevice = Microphone.devices.FirstOrDefault(device =>
            device.IndexOf("PicoStreamingMicrophone",
                System.StringComparison.OrdinalIgnoreCase) >= 0) ??
            Microphone.devices.FirstOrDefault(device =>
                device.IndexOf("pico", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                device.IndexOf("speaker", System.StringComparison.OrdinalIgnoreCase) < 0) ??
            Microphone.devices.FirstOrDefault(device =>
                device.IndexOf("streaming", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                device.IndexOf("speaker", System.StringComparison.OrdinalIgnoreCase) < 0) ??
            // PICO exposes its physical headset microphone through Android's
            // voice-recognition source instead of including "PICO" in the name.
            Microphone.devices.FirstOrDefault(device =>
                device.IndexOf("voice recognition", System.StringComparison.OrdinalIgnoreCase) >= 0) ??
            Microphone.devices.FirstOrDefault(device =>
                device.IndexOf("Android audio input", System.StringComparison.OrdinalIgnoreCase) >= 0);

        if (string.IsNullOrEmpty(microphoneDevice))
        {
            Debug.LogWarning(
                "未找到可识别的 PICO/Android 头显麦克风，将使用系统默认输入。可用设备：" +
                string.Join(", ", Microphone.devices));
        }
        else
        {
            Debug.Log("通知书语音麦克风已切换为：" + microphoneDevice);
        }

        if (audioSource != null)
        {
            audioSource.playOnAwake = false;
            // Unity routes Android media audio through the PICO headset's selected
            // system speaker/output device; do not bind a PC mixer.
            audioSource.outputAudioMixerGroup = null;
            audioSource.spatialBlend = 0f;
        }
    }

    void ConnectToASR()
    {
        if (isAsrConnecting || (ws != null && ws.ReadyState == WebSocketState.Open))
            return;
        if (ws != null)
        {
            try { ws.Close(); } catch { }
        }
        isAsrConnecting = true;
        var endpoint = CampusNetworkConfig.ResolveAsrUrl();
        Debug.Log("正在连接 FunASR：" + endpoint);
        ws = new WebSocket(endpoint);
        ws.OnOpen += (sender, e) =>
        {
            ExecuteOnMainThread(() =>
            {
                Debug.Log("FunASR WebSocket 连接成功：" + endpoint);
                LastCaptureError = string.Empty;
                isAsrConnecting = false;
                if (statusText) statusText.text = "已连接";
            });
        };
        ws.OnError += (sender, e) =>
        {
            ExecuteOnMainThread(() =>
            {
                Debug.LogError($"FunASR WebSocket 错误 ({endpoint}): {e.Message}");
                LastCaptureError = "无法连接 FunASR：" + e.Message;
                isAsrConnecting = false;
                nextAsrReconnectTime = Time.unscaledTime + 3f;
                if (statusText) statusText.text = "连接错误";
            });
        };
        ws.OnClose += (sender, e) =>
        {
            ExecuteOnMainThread(() =>
            {
                Debug.Log("WebSocket 关闭");
                if (string.IsNullOrWhiteSpace(LastCaptureError))
                    LastCaptureError = "FunASR 连接已关闭";
                isAsrConnecting = false;
                nextAsrReconnectTime = Time.unscaledTime + 3f;
                if (statusText) statusText.text = "已断开";
            });
        };
        ws.OnMessage += (sender, e) =>
        {
            ExecuteOnMainThread(() => HandleASRMessage(e.Data));
        };
        ws.ConnectAsync();
    }

    void HandleASRMessage(string json)
    {
        var data = JsonUtility.FromJson<ASRResponse>(json);
        switch (data.type)
        {
            case "started":
                if (statusText) statusText.text = "录音中...";
                break;
            case "final":
                string recognizedText = NormalizeRecognizedText(data.text);
                if (string.IsNullOrEmpty(recognizedText))
                {
                    if (statusText) statusText.text = "未识别到语音";
                    return;
                }
                Debug.Log("ASR→LLM 实际发送文本：" + recognizedText);
                if (userText) userText.text = recognizedText;
                if (statusText) statusText.text = "识别完成，调用 AI...";
                if (AdmissionLetterGuide.Instance != null)
                    AdmissionLetterGuide.Instance.SubmitRecognizedText(recognizedText);
                else
                    StartCoroutine(CallOllama(recognizedText));
                break;
            case "error":
                Debug.LogError($"ASR 错误: {data.message}");
                if (statusText) statusText.text = $"ASR 错误: {data.message}";
                break;
        }
    }

    private static string NormalizeRecognizedText(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Trim();
        normalized = Regex.Replace(normalized,
            @"(?<=[\u3400-\u9fff])\s+(?=[\u3400-\u9fff])", string.Empty);
        return normalized.Trim();
    }

    // ================== 录音 ==================
    public bool BeginVoiceCapture() => StartRecording();
    public bool EndVoiceCapture() => StopRecordingAndSend();

    bool StartRecording()
    {
        if (isRecording) return true;
        if (ws == null || ws.ReadyState != WebSocketState.Open)
        {
            Debug.LogError("WebSocket 未连接");
            LastCaptureError = Application.isEditor
                ? "FunASR 尚未连接。请确认一键启动完成后，重新进入 PDC Play。"
                : "FunASR 尚未连接。请确认 PICO 与电脑在同一 Wi-Fi，且电脑防火墙已放行 8765 端口。";
            return false;
        }
        recordedClip = Microphone.Start(
            string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice,
            false,
            (int)maxRecordTime,
            sampleRate);
        if (recordedClip == null)
        {
            Debug.LogError("无法启动 PICO 头显麦克风，请检查 PDC 音频串流设置。");
            LastCaptureError = "无法启动麦克风。请在 PDC 开启麦克风串流并授权 Unity 麦克风权限。";
            return false;
        }
        isRecording = true;
        ws.Send("{\"type\":\"start\"}");
        ExecuteOnMainThread(() =>
        {
            if (statusText) statusText.text = "录音中...";
        });
        Debug.Log("开始录音");
        return true;
    }

    bool StopRecordingAndSend()
    {
        if (!isRecording) return false;
        int recordedSampleCount = Microphone.GetPosition(
            string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice);
        Microphone.End(string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice);
        isRecording = false;

        if (recordedSampleCount <= 0)
        {
            Debug.LogError("PICO 麦克风没有采集到音频数据。");
            LastCaptureError = "没有采集到麦克风音频。请检查 PDC 麦克风串流。";
            return false;
        }

        float[] samples = new float[recordedSampleCount * recordedClip.channels];
        recordedClip.GetData(samples, 0);
        // PICO Streaming commonly exposes 48 kHz stereo to Unity even when
        // Microphone.Start asks for 16 kHz. FunASR expects 16 kHz mono PCM;
        // sending the driver-native interleaved samples made speech play at
        // the wrong rate and produced short garbled recognitions.
        var asrSamples = ConvertToAsrMono16k(samples, recordedClip.channels,
            recordedClip.frequency);
        byte[] pcmBytes = ConvertFloatToPCM16(asrSamples);

        ws.Send(pcmBytes);
        ws.Send("{\"type\":\"stop\"}");

        ExecuteOnMainThread(() =>
        {
            if (statusText) statusText.text = "处理中...";
        });
        Debug.Log($"停止录音，原始格式 {recordedClip.frequency}Hz / " +
                  $"{recordedClip.channels} 声道，已转换为 16000Hz 单声道，" +
                  $"发送 {pcmBytes.Length} 字节");
        return true;
    }

    private float[] ConvertToAsrMono16k(float[] interleavedSamples, int channels,
        int sourceSampleRate)
    {
        channels = Mathf.Max(1, channels);
        sourceSampleRate = Mathf.Max(1, sourceSampleRate);
        var frameCount = interleavedSamples.Length / channels;
        if (frameCount == 0) return System.Array.Empty<float>();

        var mono = new float[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var sum = 0f;
            var offset = frame * channels;
            for (var channel = 0; channel < channels; channel++)
                sum += interleavedSamples[offset + channel];
            mono[frame] = sum / channels;
        }

        if (sourceSampleRate == sampleRate) return mono;

        var targetFrameCount = Mathf.Max(1,
            Mathf.RoundToInt(frameCount * (float)sampleRate / sourceSampleRate));
        var resampled = new float[targetFrameCount];
        for (var target = 0; target < targetFrameCount; target++)
        {
            var sourcePosition = target * (float)sourceSampleRate / sampleRate;
            var left = Mathf.Min(frameCount - 1, Mathf.FloorToInt(sourcePosition));
            var right = Mathf.Min(frameCount - 1, left + 1);
            resampled[target] = Mathf.Lerp(mono[left], mono[right], sourcePosition - left);
        }
        return resampled;
    }

    byte[] ConvertFloatToPCM16(float[] floatSamples)
    {
        short[] intSamples = new short[floatSamples.Length];
        for (int i = 0; i < floatSamples.Length; i++)
            intSamples[i] = (short)(floatSamples[i] * 32767f);
        byte[] bytes = new byte[intSamples.Length * 2];
        System.Buffer.BlockCopy(intSamples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    // ================== Ollama ==================
    [System.Serializable]
    public class OllamaRequest
    {
        public string model;
        public string prompt;
        public bool stream;
    }

    IEnumerator CallOllama(string userInput)
    {
        string prompt = BuildPrompt(userInput);
        OllamaRequest req = new OllamaRequest
        {
            model = ollamaModel,
            prompt = prompt,
            stream = false
        };
        string jsonData = JsonUtility.ToJson(req);

        using (var request = new UnityEngine.Networking.UnityWebRequest(ollamaUrl, "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);
            request.uploadHandler = new UnityEngine.Networking.UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();

            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                var response = JsonUtility.FromJson<OllamaResponse>(request.downloadHandler.text);
                string aiReply = response.response;
                Debug.Log($"Ollama 返回：{aiReply}");
                if (!string.IsNullOrEmpty(aiReply))
                {
                    if (aiText) aiText.text = aiReply;
                    if (statusText) statusText.text = "合成语音...";

                    conversationHistory.Add(userInput);
                    conversationHistory.Add(aiReply);
                    if (conversationHistory.Count > maxHistory * 2)
                    {
                        conversationHistory.RemoveAt(0);
                        conversationHistory.RemoveAt(0);
                    }

                    StartCoroutine(BaiduTTS(aiReply));
                }
                else
                {
                    if (statusText) statusText.text = "AI 回复为空";
                }
            }
            else
            {
                Debug.LogError($"Ollama 请求失败: {request.error}");
                Debug.LogError($"响应内容: {request.downloadHandler.text}");
                if (statusText) statusText.text = "Ollama 错误";
            }
        }
    }

    string BuildPrompt(string userInput)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < conversationHistory.Count; i += 2)
        {
            sb.Append($"用户：{conversationHistory[i]}\n");
            if (i + 1 < conversationHistory.Count)
                sb.Append($"助手：{conversationHistory[i + 1]}\n");
        }
        sb.Append($"用户：{userInput}\n助手：");
        return sb.ToString();
    }

    // ================== 百度 TTS ==================
    IEnumerator BaiduTTS(string text)
{
    // 正确的 REST API 地址
    string correctUrl = "https://tsn.baidu.com/text2audio";
    string encodedText = UnityEngine.Networking.UnityWebRequest.EscapeURL(text);
    string encodedToken = UnityEngine.Networking.UnityWebRequest.EscapeURL(baiduAccessToken);

    // 参数 aue=3 表示 MP3，如果希望更流畅可改 aue=6(PCM)，但需要 AudioType.PCM 解码
    string url = $"{correctUrl}?tex={encodedText}&tok={encodedToken}&cuid=unity&ctp=1&lan=zh&spd=5&pit=5&vol=9&per=0&aue=3";

    Debug.Log($"TTS 请求 URL: {url}");

    using (var www = UnityEngine.Networking.UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
    {
        yield return www.SendWebRequest();

        if (www.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
        {
            // 检查 Content-Type 是否为音频
            string contentType = www.GetResponseHeader("Content-Type");
            if (!string.IsNullOrEmpty(contentType) && contentType.Contains("audio/"))
            {
                AudioClip clip = UnityEngine.Networking.DownloadHandlerAudioClip.GetContent(www);
                if (clip != null)
                {
                    if (audioSource != null)
                    {
                        audioSource.clip = clip;
                        audioSource.Play();
                    }
                    if (statusText) statusText.text = "已完成";
                }
                else
                {
                    Debug.LogError("AudioClip 创建失败，可能是音频格式问题");
                    if (statusText) statusText.text = "音频解码失败";
                }
            }
            else
            {
                // 如果返回的不是音频，打印内容（通常是错误 JSON）
                string responseText = www.downloadHandler.text;
                Debug.LogError($"TTS 返回非音频数据: {responseText}");
                if (statusText) statusText.text = "TTS 返回错误";
            }
        }
        else
        {
            Debug.LogError($"TTS 请求失败: {www.error}");
            if (statusText) statusText.text = "TTS 请求失败";
        }
    }
}

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        if (ws != null && ws.ReadyState == WebSocketState.Open)
            ws.Close();
    }

    [System.Serializable]
    public class ASRResponse
    {
        public string type;
        public string text;
        public string message;
    }

    [System.Serializable]
    public class OllamaResponse
    {
        public string response;
    }
}
