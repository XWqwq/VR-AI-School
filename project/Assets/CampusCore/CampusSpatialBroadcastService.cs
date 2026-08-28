using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class CampusSpatialBroadcastService : MonoBehaviour
{
    public static CampusSpatialBroadcastService Instance { get; private set; }

    [SerializeField] private float activationDistance = 2.8f;
    [SerializeField] private float globalCooldown = 35f;
    [SerializeField] private float displayDuration = 7f;

    private readonly HashSet<string> announcedThisSession = new HashSet<string>();
    private Transform head;
    private float lastAnnouncementTime = -999f;
    private TextMesh messageText;
    private AudioSource audioSource;
    private AudioClip chime;
    private Coroutine displayRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildDisplay();
        BuildChime();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (head == null)
            head = Camera.main != null ? Camera.main.transform : null;
        if (head == null || Time.unscaledTime - lastAnnouncementTime < globalCooldown)
            return;
        var coordinator = CampusInteractionCoordinator.Instance;
        if (coordinator != null && !coordinator.AllowsWorldInteraction)
            return;

        var registry = CampusLocationRegistry.Instance;
        if (registry == null)
            return;
        foreach (var location in registry.All)
        {
            if (location == null || location.sceneIndex != SceneManager.GetActiveScene().buildIndex ||
                announcedThisSession.Contains(location.id))
                continue;
            var horizontal = location.navigationTarget - head.position;
            horizontal.y = 0f;
            if (horizontal.sqrMagnitude <= activationDistance * activationDistance)
            {
                Announce(location);
                break;
            }
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        head = null;
        announcedThisSession.Clear();
    }

    private void Announce(CampusLocationDefinition location)
    {
        announcedThisSession.Add(location.id);
        lastAnnouncementTime = Time.unscaledTime;
        var message = BuildMessage(location);
        if (displayRoutine != null)
            StopCoroutine(displayRoutine);
        displayRoutine = StartCoroutine(ShowMessage(message));
        if (audioSource != null && chime != null)
            audioSource.PlayOneShot(chime, 0.32f);
        CampusFeedbackService.Instance?.Confirm();
        Debug.Log("空间校园广播：" + message.Replace("\n", " "));
    }

    private static string BuildMessage(CampusLocationDefinition location)
    {
        var progress = CampusProgressService.Instance;
        if (progress != null && progress.GetLocationState(location.id) == CampusDiscoveryState.Completed)
            return "◈ 校园智能播报\n欢迎再次来到“" + location.displayName + "”\n你已完成这里的探索，可向 AI 追问更多细节";
        if (location.id == "classroom_ai_terminal")
            return "◈ 校园智能播报\n已抵达 AI 学习终端\n完成观察后，将解锁个性化学习线索";
        if (location.id == "exhibition_ai_history")
            return "◈ 校园智能播报\n已抵达 AI 发展展项\n建议持续观察，并询问 AI 它与校园的联系";
        return "◈ 校园智能播报\n你正在接近“" + location.displayName + "”\n抬头观察，或打开通知书询问 AI";
    }

    private IEnumerator ShowMessage(string message)
    {
        messageText.text = message;
        messageText.gameObject.SetActive(true);
        var elapsed = 0f;
        while (elapsed < displayDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (head != null)
            {
                var target = head.position + head.forward * 1.35f + Vector3.up * 0.38f;
                messageText.transform.position = Vector3.Lerp(messageText.transform.position, target,
                    Time.unscaledDeltaTime * 8f);
                messageText.transform.rotation = Quaternion.LookRotation(
                    messageText.transform.position - head.position, Vector3.up);
            }
            var fade = Mathf.Clamp01((displayDuration - elapsed) / 1.2f);
            messageText.color = new Color(0.35f, 0.95f, 1f, fade);
            yield return null;
        }
        messageText.gameObject.SetActive(false);
        displayRoutine = null;
    }

    private void BuildDisplay()
    {
        var display = new GameObject("Spatial Broadcast Hologram");
        display.transform.SetParent(transform, false);
        messageText = display.AddComponent<TextMesh>();
        messageText.anchor = TextAnchor.MiddleCenter;
        messageText.alignment = TextAlignment.Center;
        messageText.fontSize = 54;
        messageText.characterSize = 0.012f;
        messageText.color = new Color(0.35f, 0.95f, 1f, 1f);
        display.SetActive(false);
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
    }

    private void BuildChime()
    {
        const int sampleRate = 22050;
        const float duration = 0.34f;
        var samples = new float[Mathf.CeilToInt(sampleRate * duration)];
        for (var i = 0; i < samples.Length; i++)
        {
            var t = i / (float)sampleRate;
            var envelope = Mathf.Sin(Mathf.PI * t / duration) * Mathf.Exp(-3.5f * t);
            samples[i] = (Mathf.Sin(2f * Mathf.PI * 660f * t) +
                          0.45f * Mathf.Sin(2f * Mathf.PI * 990f * t)) * envelope * 0.25f;
        }
        chime = AudioClip.Create("Campus Broadcast Chime", samples.Length, 1, sampleRate, false);
        chime.SetData(samples, 0);
    }
}
