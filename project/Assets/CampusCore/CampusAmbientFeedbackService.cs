using System.Collections;
using UnityEngine;

public sealed class CampusAmbientFeedbackService : MonoBehaviour
{
    public static CampusAmbientFeedbackService Instance { get; private set; }

    private Transform head;
    private GameObject stateRoot;
    private LineRenderer stateRing;
    private Transform[] stateDots;
    private AdmissionLetterState lastState = AdmissionLetterState.Following;
    private Coroutine scanRoutine;
    private Coroutine navigationCueRoutine;
    private Material cyanMaterial;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        cyanMaterial = CreateMaterial(new Color(0.1f, 0.9f, 1f, 1f));
        BuildStateVisual();
    }

    private void Start()
    {
        if (AISpatialScanner.Instance != null)
            AISpatialScanner.Instance.ScanPerformed += OnScanPerformed;
        if (CampusNavigationService.Instance != null)
        {
            CampusNavigationService.Instance.NavigationStarted += OnNavigationStarted;
            CampusNavigationService.Instance.NavigationCompleted += OnNavigationCompleted;
            CampusNavigationService.Instance.NavigationCancelled += OnNavigationCancelled;
        }
    }

    private void OnDestroy()
    {
        if (AISpatialScanner.Instance != null)
            AISpatialScanner.Instance.ScanPerformed -= OnScanPerformed;
        if (CampusNavigationService.Instance != null)
        {
            CampusNavigationService.Instance.NavigationStarted -= OnNavigationStarted;
            CampusNavigationService.Instance.NavigationCompleted -= OnNavigationCompleted;
            CampusNavigationService.Instance.NavigationCancelled -= OnNavigationCancelled;
        }
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (head == null)
            head = Camera.main != null ? Camera.main.transform : null;
        var guide = AdmissionLetterGuide.Instance;
        var state = guide != null ? guide.State : AdmissionLetterState.Following;
        if (state != lastState)
        {
            lastState = state;
            ApplyState(state);
        }
        UpdateStateVisual(state);
    }

    private void BuildStateVisual()
    {
        stateRoot = new GameObject("AI Ambient State Light");
        stateRoot.transform.SetParent(transform, false);
        stateRing = stateRoot.AddComponent<LineRenderer>();
        stateRing.useWorldSpace = false;
        stateRing.loop = true;
        stateRing.positionCount = 48;
        stateRing.startWidth = 0.008f;
        stateRing.endWidth = 0.008f;
        stateRing.material = new Material(Shader.Find("Sprites/Default"));
        for (var i = 0; i < stateRing.positionCount; i++)
        {
            var angle = i / (float)stateRing.positionCount * Mathf.PI * 2f;
            stateRing.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.12f);
        }
        stateDots = new Transform[8];
        for (var i = 0; i < stateDots.Length; i++)
        {
            var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            dot.name = "AI State Particle " + i;
            dot.transform.SetParent(stateRoot.transform, false);
            dot.transform.localScale = Vector3.one * 0.018f;
            Destroy(dot.GetComponent<Collider>());
            dot.GetComponent<Renderer>().sharedMaterial = cyanMaterial;
            stateDots[i] = dot.transform;
        }
        stateRoot.SetActive(false);
    }

    private void ApplyState(AdmissionLetterState state)
    {
        var visible = state == AdmissionLetterState.Listening ||
                      state == AdmissionLetterState.Thinking ||
                      state == AdmissionLetterState.Speaking;
        stateRoot.SetActive(visible);
        if (!visible) return;
        var color = state == AdmissionLetterState.Listening
            ? new Color(0.1f, 0.9f, 1f, 1f)
            : state == AdmissionLetterState.Thinking
                ? new Color(1f, 0.65f, 0.12f, 1f)
                : new Color(0.2f, 1f, 0.55f, 1f);
        stateRing.startColor = color;
        stateRing.endColor = new Color(color.r, color.g, color.b, 0.25f);
        foreach (var dot in stateDots)
        {
            var renderer = dot.GetComponent<Renderer>();
            var material = renderer.material;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 2f);
        }
    }

    private void UpdateStateVisual(AdmissionLetterState state)
    {
        if (!stateRoot.activeSelf || head == null) return;
        var target = head.position + head.forward * 0.92f + head.right * 0.42f - Vector3.up * 0.02f;
        stateRoot.transform.position = Vector3.Lerp(stateRoot.transform.position, target,
            Time.unscaledDeltaTime * 9f);
        stateRoot.transform.rotation = Quaternion.LookRotation(stateRoot.transform.position - head.position, Vector3.up);
        var speed = state == AdmissionLetterState.Thinking ? 135f : 55f;
        stateRoot.transform.Rotate(0f, 0f, speed * Time.unscaledDeltaTime, Space.Self);
        var pulse = 1f + Mathf.Sin(Time.unscaledTime *
            (state == AdmissionLetterState.Listening ? 7f : 4f)) * 0.12f;
        stateRoot.transform.localScale = Vector3.one * pulse;
        for (var i = 0; i < stateDots.Length; i++)
        {
            var angle = i / (float)stateDots.Length * Mathf.PI * 2f + Time.unscaledTime *
                (state == AdmissionLetterState.Thinking ? 3.2f : 1.4f);
            var radius = state == AdmissionLetterState.Speaking
                ? 0.13f + Mathf.Sin(Time.unscaledTime * 9f + i) * 0.035f : 0.16f;
            stateDots[i].localPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
        }
    }

    private void OnScanPerformed(Transform viewer)
    {
        if (scanRoutine != null) StopCoroutine(scanRoutine);
        scanRoutine = StartCoroutine(PlayScanWave(viewer));
    }

    private IEnumerator PlayScanWave(Transform viewer)
    {
        var wave = new GameObject("AI Spatial Scan Wave");
        var line = wave.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = 72;
        line.startWidth = 0.045f;
        line.endWidth = 0.045f;
        line.material = new Material(Shader.Find("Sprites/Default"));
        var elapsed = 0f;
        const float duration = 1.4f;
        while (elapsed < duration && viewer != null)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(elapsed / duration);
            var radius = Mathf.Lerp(0.25f, 11f, Mathf.SmoothStep(0f, 1f, t));
            var center = new Vector3(viewer.position.x, viewer.position.y - 1.25f, viewer.position.z);
            for (var i = 0; i < line.positionCount; i++)
            {
                var angle = i / (float)line.positionCount * Mathf.PI * 2f;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
            }
            var color = new Color(0.05f, 0.9f, 1f, 1f - t);
            line.startColor = color;
            line.endColor = color;
            yield return null;
        }
        Destroy(wave);
        scanRoutine = null;
    }

    private void OnNavigationStarted(string targetId)
    {
        if (CampusLocationRegistry.Instance == null ||
            !CampusLocationRegistry.Instance.TryGet(targetId, out var location)) return;
        if (navigationCueRoutine != null) StopCoroutine(navigationCueRoutine);
        navigationCueRoutine = StartCoroutine(PlayNavigationCue(location));
    }

    private IEnumerator PlayNavigationCue(CampusLocationDefinition location)
    {
        var root = new GameObject("Navigation Direction Confirmation");
        var markers = new Transform[5];
        for (var i = 0; i < markers.Length; i++)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Direction Light " + i;
            marker.transform.SetParent(root.transform, false);
            marker.transform.localScale = Vector3.one * (0.05f + i * 0.01f);
            Destroy(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().sharedMaterial = cyanMaterial;
            markers[i] = marker.transform;
        }
        var elapsed = 0f;
        while (elapsed < 4f && head != null)
        {
            elapsed += Time.unscaledDeltaTime;
            var direction = Vector3.ProjectOnPlane(location.navigationTarget - head.position, Vector3.up).normalized;
            if (direction.sqrMagnitude < 0.1f) break;
            for (var i = 0; i < markers.Length; i++)
            {
                var travel = Mathf.Repeat(Time.unscaledTime * 0.7f + i * 0.15f, 1f);
                markers[i].position = head.position - Vector3.up * 0.65f + direction * Mathf.Lerp(0.45f, 2.6f, travel);
            }
            yield return null;
        }
        Destroy(root);
        navigationCueRoutine = null;
    }

    private void OnNavigationCompleted(string id) => StopNavigationCue();
    private void OnNavigationCancelled() => StopNavigationCue();

    private void StopNavigationCue()
    {
        if (navigationCueRoutine == null) return;
        StopCoroutine(navigationCueRoutine);
        navigationCueRoutine = null;
        var existing = GameObject.Find("Navigation Direction Confirmation");
        if (existing != null) Destroy(existing);
    }

    private static Material CreateMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        var material = new Material(shader);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 2f);
        return material;
    }
}
