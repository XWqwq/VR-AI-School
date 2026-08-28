
// using UnityEngine;

// public class BuildingInfo : MonoBehaviour
// {
//     [Header("建筑物信息")]
//     public string buildingName = "建筑物名称";
//     [TextArea(3, 10)]
//     public string buildingDescription = "建筑物的详细介绍...";
    
//     [Header("交互设置")]
//     public float gazeTimeToShow = 1.0f; // 注视1秒后显示
//     public float closeDistance = 5.0f;  // 近距离阈值
    
//     [Header("UI引用")]
//     public GameObject infoPanel; // 信息面板（可选，如果每个建筑有自己的面板）
    
//     // 内部状态
//     private float gazeTimer = 0f;
//     private bool isBeingGazed = false;
    
//     // 当玩家开始注视这个建筑物时调用
//     public void OnGazeStart()
//     {
//         isBeingGazed = true;
//         gazeTimer = 0f;
//     }
    
//     // 当玩家停止注视这个建筑物时调用
//     public void OnGazeEnd()
//     {
//         isBeingGazed = false;
//         gazeTimer = 0f;
//         BuildingUIManager.Instance.HideInfo();
//     }
    
//     // 每帧更新注视时间
//     public void UpdateGaze(float deltaTime)
//     {
//         if (isBeingGazed)
//         {
//             gazeTimer += deltaTime;
            
//             // 如果注视时间达到阈值，显示信息
//             if (gazeTimer >= gazeTimeToShow)
//             {
//                 float distance = Vector3.Distance(transform.position, Camera.main.transform.position);
//                 bool isClose = distance <= closeDistance;
                
//                 BuildingUIManager.Instance.ShowBuildingInfo(this, isClose);
//             }
//         }
//     }
// }
// 在 BuildingInfo.cs 中添加光柱相关代码
using UnityEngine;

public class BuildingInfo : MonoBehaviour
{
    [Header("AI Context")]
    [Tooltip("Stable location ID for Dify and navigation. Uses building name when empty.")]
    public string locationId;
    public string category = "building";
    public bool grantsStamp = true;

    [Header("建筑物信息")]
    public string buildingName = "建筑物名称";
    [TextArea(3, 10)]
    public string buildingDescription = "建筑物的详细介绍...";
    
    [Header("交互设置")]
    public float gazeTimeToShow = 1.0f;
    public float closeDistance = 5.0f;
    public float highlightDelay = 0.3f;
    public float contextConfirmTime = 0.8f;
    public float discoverTime = 1.5f;
    public float completeTime = 2.5f;
    
    [Header("UI引用")]
    public GameObject infoPanel;
    
    [Header("光柱设置")]
    public GameObject beamPrefab; // 光柱预制体
    public float beamHeight = 10f; // 光柱高度
    public Color beamColor = Color.cyan; // 光柱颜色
    
    // 内部状态
    private float gazeTimer = 0f;
    private bool isBeingGazed = false;
    private GameObject currentBeam; // 当前光柱实例
    private bool beamActive = false;
    private bool contextConfirmed;
    private bool discoveredThisGaze;
    private bool completedThisGaze;
    private bool secretObservedThisGaze;
    
    void Start()
    {
        if (string.IsNullOrWhiteSpace(locationId))
            locationId = string.IsNullOrWhiteSpace(buildingName)
                ? gameObject.scene.name + "_" + gameObject.name
                : buildingName.Trim().ToLowerInvariant().Replace(" ", "_");
        CampusLocationRegistry.Instance?.Register(new CampusLocationDefinition
        {
            id = locationId,
            displayName = buildingName,
            description = buildingDescription,
            sceneIndex = gameObject.scene.buildIndex,
            navigationTarget = category == "test_hotspot"
                ? new Vector3(transform.position.x, 0f, transform.position.z)
                : transform.position,
            category = category,
            grantsStamp = grantsStamp
        });
        CreateBeamEffect();
    }
    
    void CreateBeamEffect()
    {
        if (beamPrefab != null)
        {
            // 使用预制体创建光柱
            currentBeam = Instantiate(beamPrefab, transform.position, Quaternion.identity, transform);
        }
        else
        {
            // 动态创建光柱
            currentBeam = new GameObject("BuildingBeam");
            currentBeam.transform.SetParent(transform);
            currentBeam.transform.localPosition = Vector3.zero;
            
            // 添加LineRenderer组件
            LineRenderer lineRenderer = currentBeam.AddComponent<LineRenderer>();
            SetupBeamRenderer(lineRenderer);
        }
        
        SetBeamActive(false);
    }
    
    void SetupBeamRenderer(LineRenderer lineRenderer)
    {
        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = 0.5f;
        lineRenderer.endWidth = 1.5f;
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
        lineRenderer.material = new Material(shader);
        if (lineRenderer.material.HasProperty("_BaseColor"))
            lineRenderer.material.SetColor("_BaseColor", beamColor);
        if (lineRenderer.material.HasProperty("_Color"))
            lineRenderer.material.SetColor("_Color", beamColor);
        if (lineRenderer.material.HasProperty("_EmissionColor"))
            lineRenderer.material.SetColor("_EmissionColor", beamColor * 2f);
        
        // 更新光柱位置
        UpdateBeamPosition();
    }
    
    void UpdateBeamPosition()
    {
        if (currentBeam == null) return;
        
        LineRenderer lineRenderer = currentBeam.GetComponent<LineRenderer>();
        if (lineRenderer != null)
        {
            Vector3 startPos = transform.position;
            Vector3 endPos = startPos + Vector3.up * beamHeight;
            
            lineRenderer.SetPosition(0, startPos);
            lineRenderer.SetPosition(1, endPos);
        }
    }
    
    public void OnGazeStart()
    {
        isBeingGazed = true;
        gazeTimer = 0f;
        contextConfirmed = false;
        discoveredThisGaze = false;
        completedThisGaze = false;
        secretObservedThisGaze = false;
        
        // 立即显示光柱
        SetBeamActive(false);
    }
    
    public void OnGazeEnd()
    {
        isBeingGazed = false;
        gazeTimer = 0f;
        if (BuildingUIManager.Instance != null)
            BuildingUIManager.Instance.HideInfo();
        AdmissionLetterGuide.Instance?.HideLocationInfo();
        var gazeDetector = Camera.main?.GetComponent<GazeDetector>();
        gazeDetector?.HideBuildingInfo();
        
        SetBeamActive(false);
    }
    
    public void UpdateGaze(float deltaTime)
    {
        if (isBeingGazed)
        {
            gazeTimer += deltaTime;

            if (!beamActive && gazeTimer >= highlightDelay)
                SetBeamActive(true);

            if (!contextConfirmed && gazeTimer >= contextConfirmTime)
            {
                contextConfirmed = true;
                CampusContextService.Instance?.FocusLocation(
                    locationId, buildingName, buildingDescription);
                var gazeDetector = Camera.main?.GetComponent<GazeDetector>();
                gazeDetector?.ShowBuildingInfo(this);
            }

            if (!discoveredThisGaze && gazeTimer >= discoverTime)
            {
                discoveredThisGaze = true;
                CampusProgressService.Instance?.DiscoverLocation(locationId);
                CampusTaskService.Instance?.Report(CampusTaskTrigger.FocusLocation, locationId);
            }

            if (!completedThisGaze && gazeTimer >= completeTime)
            {
                completedThisGaze = true;
                CampusProgressService.Instance?.CompleteLocation(locationId);
            }

            if (!secretObservedThisGaze && gazeTimer >= 5f)
            {
                secretObservedThisGaze = true;
                CampusSecretService.Instance?.ObserveLocation(locationId, buildingName);
            }
            
            // 更新光柱效果（例如根据注视时间改变颜色）
            UpdateBeamEffect();
            
            if (gazeTimer >= gazeTimeToShow)
            {
                float distance = Vector3.Distance(transform.position, Camera.main.transform.position);
                bool isClose = distance <= closeDistance;
                
                if (BuildingUIManager.Instance != null)
                    BuildingUIManager.Instance.ShowBuildingInfo(this, isClose);
            }
        }
    }
    
    void SetBeamActive(bool active)
    {
        if (currentBeam != null)
        {
            currentBeam.SetActive(active);
            beamActive = active;
        }
    }
    
    void UpdateBeamEffect()
    {
        if (!beamActive || currentBeam == null) return;
        
        LineRenderer lineRenderer = currentBeam.GetComponent<LineRenderer>();
        if (lineRenderer != null)
        {
            // 根据注视时间改变光柱颜色（从白色渐变到目标颜色）
            float progress = Mathf.Clamp01(gazeTimer / gazeTimeToShow);
            Color currentColor = Color.Lerp(Color.white, beamColor, progress);
            
            if (lineRenderer.material.HasProperty("_BaseColor"))
                lineRenderer.material.SetColor("_BaseColor", currentColor);
            if (lineRenderer.material.HasProperty("_Color"))
                lineRenderer.material.SetColor("_Color", currentColor);
            if (lineRenderer.material.HasProperty("_EmissionColor"))
                lineRenderer.material.SetColor("_EmissionColor", currentColor * (1f + progress));
            
            // 添加脉动效果
            float pulse = Mathf.PingPong(Time.time * 2f, 0.3f) + 0.7f;
            lineRenderer.startWidth = 0.3f * pulse;
            lineRenderer.endWidth = 1.2f * pulse;
        }
    }
    
    void Update()
    {
        // 确保光柱位置正确
        if (beamActive)
        {
            UpdateBeamPosition();
        }
    }
}
