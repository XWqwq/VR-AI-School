using System;
using UnityEngine;

[Serializable]
public sealed class CampusNetworkEndpoints
{
    public string asr_url = "ws://localhost:8765";
    public string dify_base_url = "http://localhost/v1";
    public string ollama_base_url = "http://localhost:11434";
    public string edge_tts_url = "http://localhost:8766/synthesize";
    public string edge_tts_voice = "zh-CN-XiaoxiaoNeural";
}

/// <summary>Shared LAN endpoints bundled with the PICO build.</summary>
public static class CampusNetworkConfig
{
    private static CampusNetworkEndpoints endpoints;

    public static CampusNetworkEndpoints Endpoints
    {
        get
        {
            if (endpoints != null) return endpoints;
            var asset = Resources.Load<TextAsset>("CampusNetworkConfig");
            try
            {
                endpoints = asset == null
                    ? new CampusNetworkEndpoints()
                    : JsonUtility.FromJson<CampusNetworkEndpoints>(asset.text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("校园网络配置读取失败：" + exception.Message);
                endpoints = new CampusNetworkEndpoints();
            }
            return endpoints;
        }
    }

    public static bool IsLocalhost(string value)
    {
        return string.IsNullOrWhiteSpace(value) || value.IndexOf("localhost", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("127.0.0.1", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static string ResolveAsrUrl()
    {
        return Application.isEditor ? "ws://localhost:8765" : Endpoints.asr_url;
    }

    public static string ResolveDifyBaseUrl()
    {
        return Application.isEditor ? "http://localhost/v1" : Endpoints.dify_base_url;
    }

    public static string ResolveOllamaBaseUrl()
    {
        return Application.isEditor ? "http://localhost:11434" : Endpoints.ollama_base_url;
    }

    public static string ResolveEdgeTtsUrl()
    {
        return Application.isEditor ? "http://localhost:8766/synthesize" : Endpoints.edge_tts_url;
    }
}
