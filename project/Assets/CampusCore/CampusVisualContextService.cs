using System;
using System.Linq;
using UnityEngine;

public sealed class CampusVisualContextService : MonoBehaviour
{
    public static CampusVisualContextService Instance { get; private set; }

    private static readonly string[] VisualWords =
        { "这里", "这个", "那个", "它", "眼前", "我看的", "前面" };

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public string EnrichQuestion(string question, Transform viewer)
    {
        if (string.IsNullOrWhiteSpace(question) || viewer == null ||
            !VisualWords.Any(word => question.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0))
            return question;

        var best = FindObjectsOfType<BuildingInfo>(true)
            .Where(item => item != null && item.gameObject.activeInHierarchy)
            .Select(item => new
            {
                Building = item,
                Direction = (item.transform.position - viewer.position).normalized,
                Distance = Vector3.Distance(viewer.position, item.transform.position)
            })
            .Where(item => item.Distance <= 80f && Vector3.Dot(viewer.forward, item.Direction) >= 0.72f)
            .OrderByDescending(item => Vector3.Dot(viewer.forward, item.Direction) - item.Distance / 400f)
            .FirstOrDefault();
        if (best == null)
            return question;

        var building = best.Building;
        CampusContextService.Instance?.FocusLocation(
            building.locationId, building.buildingName, building.buildingDescription);
        return question + "\n【视线理解】用户当前正看向：" + building.buildingName +
               "。请优先结合该地点回答；如果问题中的“这里/这个/它”有歧义，先说明你理解的目标名称。";
    }
}
