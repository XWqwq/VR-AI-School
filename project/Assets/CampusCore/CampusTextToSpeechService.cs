using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

/// <summary>
/// Uses the TTS engine installed on the Android/PICO device. It needs no cloud
/// account or API key; the installed Chinese voice determines the final sound.
/// </summary>
public sealed class CampusTextToSpeechService : MonoBehaviour
{
    private static CampusTextToSpeechService instance;
    private string pendingText;
    private AudioSource edgeAudioSource;
    private Coroutine edgeSpeechRoutine;

#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject textToSpeech;
    private bool initialized;
    private const int Success = 0;
    private const int QueueFlush = 0;
#endif

    public static CampusTextToSpeechService Ensure()
    {
        if (instance != null) return instance;
        var serviceObject = new GameObject("Campus Text To Speech Service");
        instance = serviceObject.AddComponent<CampusTextToSpeechService>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
        edgeAudioSource = gameObject.AddComponent<AudioSource>();
        edgeAudioSource.playOnAwake = false;
        edgeAudioSource.spatialBlend = 0f;

#if UNITY_ANDROID && !UNITY_EDITOR
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            textToSpeech = new AndroidJavaObject(
                "android.speech.tts.TextToSpeech", activity, new TtsInitListener(this));
        }
#endif
    }

    public void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (edgeSpeechRoutine != null) StopCoroutine(edgeSpeechRoutine);
        edgeSpeechRoutine = StartCoroutine(SpeakWithEdgeThenFallback(text.Trim()));
    }

    private IEnumerator SpeakWithEdgeThenFallback(string text)
    {
        var endpoints = CampusNetworkConfig.Endpoints;
        var endpoint = CampusNetworkConfig.ResolveEdgeTtsUrl();
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            var separator = endpoint.Contains("?") ? "&" : "?";
            var url = endpoint + separator + "voice=" + UnityWebRequest.EscapeURL(endpoints.edge_tts_voice) +
                      "&text=" + UnityWebRequest.EscapeURL(text);
            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
            {
                request.timeout = 25;
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    var clip = DownloadHandlerAudioClip.GetContent(request);
                    if (clip != null && clip.length > 0.01f)
                    {
                        edgeAudioSource.Stop();
                        edgeAudioSource.clip = clip;
                        edgeAudioSource.Play();
                        edgeSpeechRoutine = null;
                        yield break;
                    }
                }
                Debug.LogWarning("Edge TTS 不可用，回退到 PICO 系统 TTS：" + request.error);
            }
        }
        SpeakWithSystem(text);
        edgeSpeechRoutine = null;
    }

    private void SpeakWithSystem(string text)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        pendingText = text;
        if (initialized) SpeakPendingText();
#else
        Debug.Log("Edge TTS 与 PICO 系统 TTS 都不可用：" + text);
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private void OnTtsInitialized(int status)
    {
        initialized = status == Success && textToSpeech != null;
        if (!initialized)
        {
            Debug.LogWarning("系统 TTS 初始化失败，请在设备设置中启用中文文字转语音引擎。");
            return;
        }

        using (var localeClass = new AndroidJavaClass("java.util.Locale"))
        {
            var chineseLocale = localeClass.GetStatic<AndroidJavaObject>("CHINA");
            textToSpeech.Call<int>("setLanguage", chineseLocale);
        }
        textToSpeech.Call<int>("setSpeechRate", 1.0f);
        SpeakPendingText();
    }

    private void SpeakPendingText()
    {
        if (!initialized || string.IsNullOrWhiteSpace(pendingText)) return;
        using (var parameters = new AndroidJavaObject("android.os.Bundle"))
            textToSpeech.Call<int>("speak", pendingText, QueueFlush, parameters, "campus_reply");
        pendingText = null;
    }

    private sealed class TtsInitListener : AndroidJavaProxy
    {
        private readonly CampusTextToSpeechService service;

        public TtsInitListener(CampusTextToSpeechService service)
            : base("android.speech.tts.TextToSpeech$OnInitListener")
        {
            this.service = service;
        }

        public void onInit(int status) => service.OnTtsInitialized(status);
    }
#endif

    private void OnDestroy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (textToSpeech != null)
        {
            textToSpeech.Call("stop");
            textToSpeech.Call("shutdown");
            textToSpeech.Dispose();
        }
#endif
        if (instance == this) instance = null;
    }
}
