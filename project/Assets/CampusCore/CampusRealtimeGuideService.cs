using UnityEngine;

public sealed class CampusRealtimeGuideService : MonoBehaviour
{
    public static CampusRealtimeGuideService Instance { get; private set; }
    private TextMesh hint;
    private Transform head;
    private float wrongDirectionTime;
    private float previousDistance = float.MaxValue;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        var root = new GameObject("Realtime Guide Hint");
        root.transform.SetParent(transform, false);
        hint = root.AddComponent<TextMesh>();
        hint.anchor = TextAnchor.MiddleCenter;
        hint.alignment = TextAlignment.Center;
        hint.fontSize = 46;
        hint.characterSize = 0.012f;
        hint.color = new Color(1f, 0.8f, 0.25f, 1f);
        hint.gameObject.SetActive(false);
    }

    private void Update()
    {
        var navigation = CampusNavigationService.Instance;
        if (navigation == null || !navigation.IsNavigating || !navigation.HasActiveWaypoint)
        {
            hint.gameObject.SetActive(false);
            wrongDirectionTime = 0f;
            previousDistance = float.MaxValue;
            return;
        }
        if (head == null) head = Camera.main != null ? Camera.main.transform : null;
        if (head == null) return;

        var toTarget = Vector3.ProjectOnPlane(navigation.CurrentWaypoint - head.position, Vector3.up);
        var distance = toTarget.magnitude;
        var facing = distance > 0.1f
            ? Vector3.Dot(Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized, toTarget.normalized) : 1f;
        if (facing < -0.15f || distance > previousDistance + 0.12f)
            wrongDirectionTime += Time.unscaledDeltaTime;
        else
            wrongDirectionTime = Mathf.Max(0f, wrongDirectionTime - Time.unscaledDeltaTime * 2f);
        previousDistance = distance;

        if (wrongDirectionTime >= 4f)
        {
            hint.text = "路线提醒\n你正在远离目标，请停下并寻找地面的金色星轨。\n用右手摇杆转向，让星轨出现在正前方，再向前移动。";
            hint.gameObject.SetActive(true);
            var target = head.position + head.forward * 1.3f + Vector3.up * 0.28f;
            hint.transform.position = Vector3.Lerp(hint.transform.position, target, Time.unscaledDeltaTime * 7f);
            hint.transform.rotation = Quaternion.LookRotation(hint.transform.position - head.position, Vector3.up);
        }
        else
            hint.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
