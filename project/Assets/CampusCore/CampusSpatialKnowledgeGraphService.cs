using System.Collections;
using UnityEngine;

public sealed class CampusSpatialKnowledgeGraphService : MonoBehaviour
{
    public static CampusSpatialKnowledgeGraphService Instance { get; private set; }
    private GameObject activeGraph;
    private Coroutine hideRoutine;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Show(BuildingInfo building)
    {
        if (building == null || Camera.main == null) return;
        if (activeGraph != null) Destroy(activeGraph);
        activeGraph = new GameObject("Spatial Knowledge Graph - " + building.buildingName);
        var head = Camera.main.transform;
        activeGraph.transform.position = head.position + head.forward * 1.7f;
        activeGraph.transform.rotation = Quaternion.LookRotation(activeGraph.transform.position - head.position, Vector3.up);
        var labels = new[]
        {
            building.buildingName,
            "地点介绍\n" + Shorten(building.buildingDescription, 24),
            "类别\n" + building.category,
            "可用能力\nAI 问答",
            "探索方式\n观察 · 导航"
        };
        var positions = new[]
        {
            Vector3.zero, new Vector3(-0.62f, 0.34f, 0f), new Vector3(0.62f, 0.34f, 0f),
            new Vector3(-0.62f, -0.34f, 0f), new Vector3(0.62f, -0.34f, 0f)
        };
        var nodes = new Transform[labels.Length];
        for (var i = 0; i < labels.Length; i++)
        {
            var node = new GameObject("Knowledge Node " + i);
            node.transform.SetParent(activeGraph.transform, false);
            node.transform.localPosition = positions[i];
            var text = node.AddComponent<TextMesh>();
            text.text = labels[i];
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = i == 0 ? 52 : 38;
            text.characterSize = 0.009f;
            text.color = i == 0 ? new Color(1f, 0.78f, 0.2f) : new Color(0.3f, 0.95f, 1f);
            nodes[i] = node.transform;
        }
        for (var i = 1; i < nodes.Length; i++)
        {
            var lineObject = new GameObject("Knowledge Link " + i);
            lineObject.transform.SetParent(activeGraph.transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, positions[i]);
            line.startWidth = 0.006f;
            line.endWidth = 0.003f;
            line.startColor = new Color(1f, 0.75f, 0.2f, 0.9f);
            line.endColor = new Color(0.1f, 0.9f, 1f, 0.5f);
            line.material = new Material(Shader.Find("Sprites/Default"));
        }
        if (hideRoutine != null) StopCoroutine(hideRoutine);
        hideRoutine = StartCoroutine(HideLater());
        CampusFeedbackService.Instance?.TaskComplete();
    }

    private IEnumerator HideLater()
    {
        yield return new WaitForSecondsRealtime(12f);
        if (activeGraph != null) Destroy(activeGraph);
        activeGraph = null;
        hideRoutine = null;
    }

    private static string Shorten(string value, int length)
    {
        if (string.IsNullOrWhiteSpace(value)) return "等待补充资料";
        return value.Length <= length ? value : value.Substring(0, length) + "…";
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
