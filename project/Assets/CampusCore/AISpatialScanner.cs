using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class AISpatialScanner : MonoBehaviour
{
    public static AISpatialScanner Instance { get; private set; }
    public event Action<Transform> ScanPerformed;

    public IReadOnlyList<BuildingInfo> LastResults => lastResults;
    public BuildingInfo NearestResult => lastResults.Count > 0 ? lastResults[0] : null;

    [SerializeField] private float scanRadius = 250f;
    [SerializeField] private float effectDuration = 12f;
    [SerializeField] private int maxResults = 8;

    private readonly List<BuildingInfo> lastResults = new List<BuildingInfo>();
    private readonly List<GameObject> effects = new List<GameObject>();
    private readonly List<Coroutine> ringRoutines = new List<Coroutine>();
    private Transform viewer;
    private Coroutine cleanupRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public string Scan(Transform viewerTransform)
    {
        viewer = viewerTransform != null ? viewerTransform : Camera.main?.transform;
        ClearEffects();
        lastResults.Clear();
        if (viewer == null)
            return "AI 扫描失败：未找到玩家视点。";
        ScanPerformed?.Invoke(viewer);

        var candidates = FindObjectsOfType<BuildingInfo>(true)
            .Where(item => item != null && item.gameObject.scene == SceneManager.GetActiveScene())
            .Select(item => new
            {
                Building = item,
                Distance = Vector3.Distance(viewer.position, item.transform.position),
                Facing = Vector3.Dot(viewer.forward,
                    (item.transform.position - viewer.position).normalized)
            })
            .Where(item => item.Distance <= scanRadius && item.Facing > -0.3f)
            .OrderByDescending(item => item.Facing * 2f - item.Distance / scanRadius)
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Building.locationId)
                ? item.Building.buildingName : item.Building.locationId)
            .Select(group => group.First())
            .Take(maxResults)
            .ToList();

        foreach (var candidate in candidates)
        {
            lastResults.Add(candidate.Building);
            CreateHologram(candidate.Building, candidate.Distance);
        }

        CreateScanRings();
        CampusFeedbackService.Instance?.Confirm(false);
        if (cleanupRoutine != null) StopCoroutine(cleanupRoutine);
        cleanupRoutine = StartCoroutine(ClearAfterDelay());

        if (lastResults.Count == 0)
            return "AI 扫描完成：当前视野附近未发现已注册建筑。";

        var lines = new List<string> { $"AI 已识别 {lastResults.Count} 个附近地点：" };
        for (var index = 0; index < lastResults.Count; index++)
        {
            var building = lastResults[index];
            var distance = Vector3.Distance(viewer.position, building.transform.position);
            lines.Add($"{index + 1}. {building.buildingName} · 约 {distance:0}m");
        }
        return string.Join("\n", lines);
    }

    private void CreateScanRings()
    {
        if (viewer == null) return;
        foreach (var routine in ringRoutines)
            if (routine != null) StopCoroutine(routine);
        ringRoutines.Clear();
        for (var i = 0; i < 3; i++)
            ringRoutines.Add(StartCoroutine(ExpandRing(i * 0.6f)));
    }

    private IEnumerator ExpandRing(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        var ring = new GameObject("AI Scan Ring");
        ring.transform.position = viewer.position + Vector3.up * 0.1f;
        ring.transform.rotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);
        
        var line = ring.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 120;
        line.startWidth = 0.15f;
        line.endWidth = 0.15f;
        line.startColor = new Color(0.1f, 0.8f, 1f, 0.9f);
        line.endColor = new Color(0.3f, 1f, 1f, 0f);
        line.material = new Material(Shader.Find("Sprites/Default"));
        
        effects.Add(ring);
        var startTime = Time.unscaledTime;
        var duration = 3f;
        
        while (Time.unscaledTime - startTime < duration)
        {
            var progress = (Time.unscaledTime - startTime) / duration;
            var currentRadius = scanRadius * progress;
            for (var i = 0; i < line.positionCount; i++)
            {
                var angle = (float)i / line.positionCount * Mathf.PI * 2f;
                var x = Mathf.Cos(angle) * currentRadius;
                var z = Mathf.Sin(angle) * currentRadius;
                line.SetPosition(i, viewer.position + new Vector3(x, 0.1f, z));
            }
            yield return null;
        }
    }

    private void CreateHologram(BuildingInfo building, float distance)
    {
        var root = new GameObject("AI Scan · " + building.buildingName);
        root.transform.position = building.transform.position + Vector3.up * 2.6f;

        var label = root.AddComponent<TextMesh>();
        label.text = $"◈ {building.buildingName}\n{distance:0}m · AI 已识别";
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.fontSize = 42;
        label.characterSize = 0.035f;
        label.color = new Color(0.15f, 0.95f, 1f, 1f);

        var line = root.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.SetPosition(0, building.transform.position + Vector3.up * 0.2f);
        line.SetPosition(1, root.transform.position - Vector3.up * 0.25f);
        line.startWidth = 0.025f;
        line.endWidth = 0.008f;
        line.startColor = new Color(0.1f, 0.8f, 1f, 0.15f);
        line.endColor = new Color(0.2f, 1f, 1f, 0.9f);
        line.material = new Material(Shader.Find("Sprites/Default"));
        effects.Add(root);
    }

    private void LateUpdate()
    {
        if (viewer == null) return;
        foreach (var effect in effects)
        {
            if (effect == null) continue;
            var direction = effect.transform.position - viewer.position;
            if (direction.sqrMagnitude > 0.01f)
                effect.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            var pulse = 1f + Mathf.Sin(Time.unscaledTime * 4f) * 0.06f;
            effect.transform.localScale = Vector3.one * pulse;
        }
    }

    private IEnumerator ClearAfterDelay()
    {
        yield return new WaitForSecondsRealtime(effectDuration);
        ClearEffects();
        cleanupRoutine = null;
    }

    private void ClearEffects()
    {
        foreach (var routine in ringRoutines)
            if (routine != null) StopCoroutine(routine);
        ringRoutines.Clear();
        foreach (var effect in effects)
            if (effect != null) Destroy(effect);
        effects.Clear();
    }
}
