using System;

[Serializable]
public sealed class DifyChatInputs
{
    public string scene_name;
    public string location_id;
    public string location_name;
    public string location_description;
    public string interaction_type;
    public string user_interest;
    public string student_major;
}

[Serializable]
public sealed class DifyChatRequest
{
    public DifyChatInputs inputs;
    public string query;
    public string response_mode = "blocking";
    public string conversation_id;
    public string user;
}

[Serializable]
public sealed class DifyActionPayload
{
    public string speech;
    public string emotion = "neutral";
    public string action = "none";
    public string target_id;
    public string page_id;
    public int scene_index = -1;
    public bool comfort_mode;
    public string exhibit_id;
    public string[] suggestions;
}

[Serializable]
public sealed class DifyChatResponse
{
    public string answer;
    public string conversation_id;
    public string message_id;
    public DifyActionPayload command;
}

[Serializable]
public sealed class CampusAiEnvelope
{
    public string intent;
    public bool grounded;
    public string answer;
    public string emotion = "neutral";
    public string action = "none";
    public string target_id;
    public string[] suggestions;
}

public interface IDifyChatService
{
    bool IsBusy { get; }
    void Send(DifyChatRequest request, Action<DifyChatResponse> onSuccess, Action<string> onError);
}
