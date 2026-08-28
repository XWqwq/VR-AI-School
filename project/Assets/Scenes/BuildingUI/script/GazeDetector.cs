using UnityEngine;
using UnityEngine.UI;

public class GazeDetector : MonoBehaviour
{
    [Header("视线检测设置")]
    public float maxDetectionDistance = 5000f;
    public LayerMask buildingLayerMask = -1;

    [Header("调试（正式打包前关闭）")]
    public bool showDebugRay = false;
    public bool showDebugLabel = true;
    
    private Camera playerCamera;
    private BuildingInfo currentGazedBuilding;
    private BuildingInfo previousGazedBuilding;
    private LineRenderer debugRay;
    private Text debugLabel;
    private RectTransform debugCardRect;
    
    private GameObject buildingInfoPanel;
    private RectTransform buildingInfoRect;
    private Text buildingTitleText;
    private Text buildingDescText;
    private Canvas buildingInfoCanvas;
    
    void Start()
    {
        showDebugRay = false;
        showDebugLabel = false;
        playerCamera = GetComponent<Camera>();
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }

        CreateGazeReticle();
        CreateBuildingInfoPanel();
    }
    
    void Update()
    {
        UpdateBuildingInfoPanelPosition();
        if (AdmissionLetterGuide.Instance != null && AdmissionLetterGuide.Instance.IsOpen)
        {
            SuspendGazeDetection();
            return;
        }

        DetectGazedBuilding();
        UpdateGazedBuilding();
    }

    private void SuspendGazeDetection()
    {
        if (previousGazedBuilding != null)
            previousGazedBuilding.OnGazeEnd();

        currentGazedBuilding = null;
        previousGazedBuilding = null;

        if (debugRay != null)
            debugRay.enabled = false;
        if (debugLabel != null)
            debugLabel.gameObject.SetActive(false);
        HideBuildingInfo();
    }
    
    void DetectGazedBuilding()
    {
        if (playerCamera == null)
            return;

        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        RaycastHit hit;
        
        if (Physics.Raycast(ray, out hit, maxDetectionDistance, buildingLayerMask))
        {
            BuildingInfo building = hit.collider.GetComponentInParent<BuildingInfo>();
            UpdateDebugVisuals(ray, hit.point, hit.collider.name, building);
            
            if (building != null)
            {
                currentGazedBuilding = building;
                return;
            }
        }
        else
        {
            UpdateDebugVisuals(ray, ray.GetPoint(maxDetectionDistance), "未命中 Collider", null);
        }
        
        // 如果没有击中建筑物
        currentGazedBuilding = null;
    }

    void CreateDebugVisuals()
    {
        debugRay = gameObject.AddComponent<LineRenderer>();
        debugRay.useWorldSpace = true;
        debugRay.positionCount = 2;
        debugRay.startWidth = 0.014f;
        debugRay.endWidth = 0.007f;
        debugRay.material = new Material(Shader.Find("Sprites/Default"));
        debugRay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        debugRay.receiveShadows = false;

        var labelObject = new GameObject("Bottom Right Gaze Information", typeof(RectTransform),
            typeof(Canvas), typeof(Image));
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = new Vector3(0.50f, -0.27f, 1.08f);
        labelObject.transform.localRotation = Quaternion.identity;
        labelObject.transform.localScale = Vector3.one * 0.001f;
        var rect = (RectTransform)labelObject.transform;
        debugCardRect = rect;
        rect.sizeDelta = new Vector2(390f, 122f);
        var canvas = labelObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 470;
        var background = labelObject.GetComponent<Image>();
        background.color = new Color(0.02f, 0.16f, 0.25f, 0.92f);
        if (AdmissionLetterGuide.ApplyOptionalSprite(background, "AdmissionLetterUI/gaze_card"))
            background.color = Color.white;

        var textObject = new GameObject("Gaze Information Text", typeof(RectTransform), typeof(Text));
        var textRect = (RectTransform)textObject.transform;
        textRect.SetParent(rect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(20f, 12f);
        textRect.offsetMax = new Vector2(-20f, -12f);
        debugLabel = textObject.GetComponent<Text>();
        debugLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ??
                          Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 17);
        debugLabel.fontSize = 17;
        debugLabel.alignment = TextAnchor.MiddleRight;
        debugLabel.horizontalOverflow = HorizontalWrapMode.Wrap;
        debugLabel.verticalOverflow = VerticalWrapMode.Truncate;
        debugLabel.resizeTextForBestFit = true;
        debugLabel.resizeTextMinSize = 13;
        debugLabel.resizeTextMaxSize = 17;
        debugLabel.color = new Color(0.72f, 0.96f, 1f, 1f);
        debugLabel.text = "当前注视：校园环境\n看向发光地点可查看介绍";
    }

    private void CreateGazeReticle()
    {
        if (transform.Find("Gaze Center Landing Dot") != null)
            return;
        var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dot.name = "Gaze Center Landing Dot";
        dot.transform.SetParent(transform, false);
        dot.transform.localPosition = new Vector3(0f, 0f, 1.15f);
        dot.transform.localScale = Vector3.one * 0.009f;
        Destroy(dot.GetComponent<Collider>());
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                     Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
        var material = new Material(shader) { name = "Gaze Center Dot Material" };
        var color = new Color(0.42f, 0.96f, 1f, 0.92f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 1.6f);
        dot.GetComponent<Renderer>().sharedMaterial = material;
    }

    void UpdateDebugVisuals(Ray ray, Vector3 endPoint, string hitName, BuildingInfo building)
    {
        if (debugRay != null)
        {
            debugRay.enabled = showDebugRay;
            debugRay.SetPosition(0, ray.origin + ray.direction * 0.08f);
            debugRay.SetPosition(1, endPoint);
            Color rayColor = building != null
                ? new Color(0.1f, 1f, 0.25f, 1f)
                : (hitName == "未命中 Collider" ? Color.cyan : new Color(1f, 0.25f, 0.1f, 1f));
            debugRay.startColor = rayColor;
            debugRay.endColor = rayColor;
        }

        if (debugLabel != null)
        {
            debugLabel.gameObject.SetActive(showDebugLabel);
            if (!showDebugLabel)
                return;

            if (building != null)
            {
                debugLabel.color = new Color(0.45f, 1f, 0.75f, 1f);
                debugLabel.text = "当前注视：" + building.buildingName +
                                  "\n请保持视线稳定，进度完成后打开介绍" +
                                  "\n按住右手侧边握持键可以向 AI 提问";
            }
            else
            {
                debugLabel.color = new Color(0.62f, 0.88f, 1f, 0.9f);
                debugLabel.text = "当前注视：校园环境\n看向发光地点可查看介绍";
            }
        }
    }

    private void AlignGazeCardBelowTutorial()
    {
        if (debugCardRect == null)
            return;
        var tutorial = GameObject.Find("VR New User Tutorial Second Page");
        if (tutorial == null || !tutorial.activeInHierarchy)
            return;
        var tutorialRect = tutorial.transform as RectTransform;
        if (tutorialRect == null)
            return;
        debugCardRect.position = tutorialRect.TransformPoint(new Vector3(0f, -326f, 0f));
        debugCardRect.rotation = tutorialRect.rotation;
    }
    
    private void UpdateBuildingInfoPanelPosition()
    {
        if (buildingInfoRect == null || buildingInfoCanvas == null || playerCamera == null)
            return;
        
        var tutorial = GameObject.Find("VR New User Tutorial Second Page");
        if (tutorial != null && tutorial.activeInHierarchy)
        {
            var tutorialRect = tutorial.transform as RectTransform;
            if (tutorialRect != null)
            {
                buildingInfoRect.position = tutorialRect.TransformPoint(new Vector3(-420f, 0f, 0f));
                buildingInfoRect.rotation = tutorialRect.rotation;
            }
        }
        else
        {
            var forward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            buildingInfoCanvas.transform.position = playerCamera.transform.position +
                playerCamera.transform.right * 0.48f +
                Vector3.up * 0.08f +
                forward * 1.05f;
            buildingInfoCanvas.transform.rotation = Quaternion.LookRotation(
                buildingInfoCanvas.transform.position - playerCamera.transform.position, Vector3.up);
        }
    }
    
    private void CreateBuildingInfoPanel()
    {
        var anchor = new GameObject("Building Info Anchor", typeof(RectTransform), typeof(Canvas));
        anchor.transform.SetParent(transform, false);
        anchor.transform.localPosition = new Vector3(0.45f, 0.05f, 1.1f);
        anchor.transform.localRotation = Quaternion.Euler(0f, -7f, 0f);
        anchor.transform.localScale = Vector3.one * 0.0012f;
        var anchorRect = (RectTransform)anchor.transform;
        anchorRect.sizeDelta = new Vector2(560f, 330f);
        buildingInfoCanvas = anchor.GetComponent<Canvas>();
        buildingInfoCanvas.renderMode = RenderMode.WorldSpace;
        buildingInfoCanvas.sortingOrder = 475;
        
        var root = new GameObject("Building Info Panel", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(anchorRect, false);
        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(560f, 330f);
        rect.anchoredPosition = Vector2.zero;
        buildingInfoPanel = root;
        buildingInfoRect = rect;
        var background = root.GetComponent<Image>();
        // Reuse the newcomer-tutorial base visual for the gaze-triggered
        // building introduction panel.
        background.color = AdmissionLetterGuide.ApplyOptionalSprite(
            background, "AdmissionLetterUI/tutorial_background")
            ? Color.white : new Color(0.02f, 0.1f, 0.18f, 0.96f);

        var borderTop = new GameObject("Border Top", typeof(RectTransform), typeof(Image));
        var borderTopRect = (RectTransform)borderTop.transform;
        borderTopRect.SetParent(rect, false);
        borderTopRect.anchoredPosition = new Vector2(0f, 163f);
        borderTopRect.sizeDelta = new Vector2(540f, 2f);
        borderTop.GetComponent<Image>().color = new Color(0.25f, 0.65f, 0.88f, 0.9f);

        var borderBottom = new GameObject("Border Bottom", typeof(RectTransform), typeof(Image));
        var borderBottomRect = (RectTransform)borderBottom.transform;
        borderBottomRect.SetParent(rect, false);
        borderBottomRect.anchoredPosition = new Vector2(0f, -163f);
        borderBottomRect.sizeDelta = new Vector2(540f, 2f);
        borderBottom.GetComponent<Image>().color = new Color(0.25f, 0.65f, 0.88f, 0.9f);

        var borderLeft = new GameObject("Border Left", typeof(RectTransform), typeof(Image));
        var borderLeftRect = (RectTransform)borderLeft.transform;
        borderLeftRect.SetParent(rect, false);
        borderLeftRect.anchoredPosition = new Vector2(-278f, 0f);
        borderLeftRect.sizeDelta = new Vector2(2f, 310f);
        borderLeft.GetComponent<Image>().color = new Color(0.25f, 0.65f, 0.88f, 0.9f);

        var borderRight = new GameObject("Border Right", typeof(RectTransform), typeof(Image));
        var borderRightRect = (RectTransform)borderRight.transform;
        borderRightRect.SetParent(rect, false);
        borderRightRect.anchoredPosition = new Vector2(278f, 0f);
        borderRightRect.sizeDelta = new Vector2(2f, 310f);
        borderRight.GetComponent<Image>().color = new Color(0.25f, 0.65f, 0.88f, 0.9f);

        var titleBg = new GameObject("Title Background", typeof(RectTransform), typeof(Image));
        var titleBgRect = (RectTransform)titleBg.transform;
        titleBgRect.SetParent(rect, false);
        titleBgRect.anchoredPosition = new Vector2(0f, 126f);
        titleBgRect.sizeDelta = new Vector2(500f, 44f);
        var titleBgImage = titleBg.GetComponent<Image>();
        titleBgImage.color = new Color(0.06f, 0.3f, 0.5f, 0.9f);
        if (AdmissionLetterGuide.ApplyOptionalSprite(titleBgImage, "AdmissionLetterUI/title_decoration"))
            titleBgImage.color = Color.white;
        
        buildingTitleText = CreateTextElement(rect, "建筑名称", 25,
            new Vector2(0f, 126f), new Vector2(490f, 42f), TextAnchor.MiddleCenter);
        buildingTitleText.color = new Color(1f, 0.85f, 0.35f, 1f);
        
        var separator = new GameObject("Separator", typeof(RectTransform), typeof(Image));
        var separatorRect = (RectTransform)separator.transform;
        separatorRect.SetParent(rect, false);
        separatorRect.anchoredPosition = new Vector2(0f, 101f);
        separatorRect.sizeDelta = new Vector2(490f, 2f);
        separator.GetComponent<Image>().color = new Color(0.2f, 0.55f, 0.8f, 0.7f);
        
        var subtitle = CreateTextElement(rect, "建筑介绍", 16,
            new Vector2(-190f, 82f), new Vector2(110f, 28f), TextAnchor.MiddleLeft);
        subtitle.color = new Color(0.55f, 0.9f, 1f, 0.9f);
        
        buildingDescText = CreateTextElement(rect, "注视建筑后显示介绍...", 19,
            new Vector2(0f, -7f), new Vector2(490f, 150f), TextAnchor.UpperLeft);
        buildingDescText.color = new Color(0.92f, 0.97f, 1f, 1f);
        buildingDescText.fontStyle = FontStyle.Normal;
        
        var footerBg = new GameObject("Footer Background", typeof(RectTransform), typeof(Image));
        var footerBgRect = (RectTransform)footerBg.transform;
        footerBgRect.SetParent(rect, false);
        footerBgRect.anchoredPosition = new Vector2(0f, -137f);
        footerBgRect.sizeDelta = new Vector2(500f, 26f);
        var footerBgImage = footerBg.GetComponent<Image>();
        footerBgImage.color = new Color(0.04f, 0.22f, 0.38f, 0.85f);
        if (AdmissionLetterGuide.ApplyOptionalSprite(footerBgImage, "AdmissionLetterUI/bottom_decoration"))
            footerBgImage.color = Color.white;
        
        var footerText = CreateTextElement(rect, "按住右手侧边握持键向AI提问", 14,
            new Vector2(0f, -137f), new Vector2(490f, 24f), TextAnchor.MiddleCenter);
        footerText.color = new Color(0.5f, 0.85f, 1f, 0.9f);
        
        root.SetActive(false);
    }
    
    private static Text CreateTextElement(RectTransform parent, string value, int size,
        Vector2 position, Vector2 dimensions, TextAnchor alignment)
    {
        var item = new GameObject("Text", typeof(RectTransform), typeof(Text));
        var rect = (RectTransform)item.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        var text = item.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ??
                    Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, size);
        text.fontSize = size;
        text.color = new Color(0.9f, 0.96f, 1f, 1f);
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = 13;
        text.resizeTextMaxSize = size;
        text.text = value;
        return text;
    }
    
    public void ShowBuildingInfo(BuildingInfo building)
    {
        if (buildingInfoPanel == null || building == null)
            return;
        buildingTitleText.text = building.buildingName;
        buildingDescText.text = string.IsNullOrWhiteSpace(building.buildingDescription)
            ? "该地点暂未录入详细说明，可通过校园知识库了解相关信息。"
            : building.buildingDescription;
        buildingInfoPanel.SetActive(true);
    }
    
    public void HideBuildingInfo()
    {
        if (buildingInfoPanel != null)
            buildingInfoPanel.SetActive(false);
    }
    
    void UpdateGazedBuilding()
    {
        // 如果注视的建筑物发生变化
        if (currentGazedBuilding != previousGazedBuilding)
        {
            // 停止注视之前的建筑物
            if (previousGazedBuilding != null)
            {
                previousGazedBuilding.OnGazeEnd();
            }
            
            // 开始注视新的建筑物
            if (currentGazedBuilding != null)
            {
                currentGazedBuilding.OnGazeStart();
            }
            
            previousGazedBuilding = currentGazedBuilding;
        }
        
        // 更新当前注视的建筑物的计时器
        if (currentGazedBuilding != null)
        {
            currentGazedBuilding.UpdateGaze(Time.deltaTime);
        }
    }
    
    // 在Scene视图中显示视线（用于调试）
    void OnDrawGizmos()
    {
        if (playerCamera != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawRay(playerCamera.transform.position, playerCamera.transform.forward * maxDetectionDistance);
        }
    }
}
