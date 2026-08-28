using System.Collections;
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Unity.XR.CoreUtils;

public enum AdmissionLetterState
{
    Following,
    Notifying,
    Opening,
    Open,
    Listening,
    Thinking,
    Speaking,
    Guiding,
    Held
}

public sealed class AdmissionLetterGuide : MonoBehaviour
{
    // The imported boards were authored with their wide face along local Z.
    // This keeps that face vertical and directed toward the player.
    private static readonly Quaternion BoardVisualRotation = Quaternion.Euler(0f, 90f, 0f);
    // Display the scene-authored boards at half of their original size while
    // retaining their proportions.
    private const float BoardVisualScale = 0.4f;
    // A fully opened letter must be flat. Keeping it at -155 degrees left the
    // cover visibly folded relative to the back page.
    private const float OpenCoverAngle = -180f;

    public static AdmissionLetterGuide Instance { get; private set; }

    [Header("跟随")]
    [SerializeField] private Vector3 viewOffset = new Vector3(-0.44f, -0.34f, 0.88f);
    [SerializeField] private float followSmoothTime = 0.22f;
    [SerializeField] private float openDuration = 0.45f;
    [SerializeField] private float selectionRayDistance = 12f;
    [SerializeField] private float fallbackEyeHeight = 1.5f;
    [SerializeField] private Vector3 leftHandHoldOffset = new Vector3(0.02f, 0.08f, 0.10f);
    [SerializeField] private float projectedMenuExtraDistance = 0.27f;
    [SerializeField] private float projectedMenuHeight = 0.30f;
    [SerializeField] private float projectedLocationExtraHeight = 0.32f;

    public AdmissionLetterState State { get; private set; } = AdmissionLetterState.Following;
    public bool IsHeld => heldHand != 0;
    public bool IsOpen => isOpen;
    public RectTransform MenuUiRoot => uiCanvas != null ? (RectTransform)uiCanvas.transform : null;
    public event Action<DifyActionPayload> CommandReceived;

    private Transform head;
    private Transform trackingOrigin;
    private Transform coverPivot;
    private BoxCollider interactionCollider;
    private Canvas uiCanvas;
    private GameObject projectionBeam;
    private static readonly Vector3 UiFullScale = Vector3.one * 0.00118f;
    private static readonly Vector3 UiCollapsedScale = Vector3.one * 0.00018f;
    private Text contextText;
    private Text answerText;
    private readonly System.Collections.Generic.List<string> answerPages =
        new System.Collections.Generic.List<string>();
    private int answerPageIndex;
    private const int AnswerPageCharacterLimit = 110;
    private Text hintText;
    private Text locationTitleText;
    private Text locationDescriptionText;
    private Text scanResultsText;
    private RectTransform scanPage;
    private readonly System.Collections.Generic.List<GameObject> scanResultRows =
        new System.Collections.Generic.List<GameObject>();
    private Text timetableTitleText;
    private Text thinkingIndicatorText;
    private Text timetableBodyText;
    private Text journeyStatusText;
    private Text collectionStatusText;
    private Text experienceReportText;
    private Text exhibitContentText;
    private Text memoryStatusText;
    private Renderer backPageRenderer;
    private Renderer coverPageRenderer;
    private Material paperMaterial;
    private Material coverMaterial;
    private Light feedbackLight;
    private ParticleSystem feedbackParticles;
    private readonly System.Collections.Generic.Dictionary<string, GameObject> menuPages =
        new System.Collections.Generic.Dictionary<string, GameObject>();
    private string activePageId = "main";
    private BuildingInfo selectedLocationBuilding;
    private AdmissionLetterMenuButton hoveredMenuButton;
    private Vector3 followVelocity;
    private InputAction leftGripAction;
    private InputAction rightGripAction;
    private InputAction leftTriggerAction;
    private InputAction rightTriggerAction;
    private InputAction leftPositionAction;
    private InputAction rightPositionAction;
    private InputAction leftRotationAction;
    private InputAction rightRotationAction;
    private IDifyChatService chatService;
    private string conversationId = string.Empty;
    private string lastNotifiedLocationId = string.Empty;
    private float lastLocationNotificationTime = -999f;
    private bool isOpen;
    private bool leftGripWasPressed;
    private bool rightGripWasPressed;
    private bool leftTriggerWasPressed;
    private bool rightTriggerWasPressed;
    private bool awaitingIdentitySpeech;
    private int heldHand;
    private Coroutine animationRoutine;
    private Coroutine feedbackRoutine;
    private Coroutine listeningTimeoutRoutine;

    public static AdmissionLetterGuide Create(Transform headTransform)
    {
        if (Instance != null)
            return Instance;

        var root = new GameObject("Admission Letter Guide");
        var guide = root.AddComponent<AdmissionLetterGuide>();
        guide.head = headTransform;
        return guide;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildPlaceholderModel();
        var ollama = gameObject.AddComponent<OllamaChatService>();
        var dify = gameObject.AddComponent<DifyChatService>();
        // A configured Dify app owns the campus knowledge base and should be
        // preferred over the generic local Ollama fallback.
        chatService = dify.IsConfigured
            ? (IDifyChatService)dify
            : ollama.IsConfigured
                ? (IDifyChatService)ollama
                : gameObject.AddComponent<MockDifyChatService>();
        SetOpenImmediate(false);
    }

    private void OnEnable()
    {
        leftGripAction = CreateInput("Left Grip", "<XRController>{LeftHand}/grip", "Axis");
        rightGripAction = CreateInput("Right Grip", "<XRController>{RightHand}/grip", "Axis");
        leftTriggerAction = CreateInput("Left Trigger", "<XRController>{LeftHand}/trigger", "Axis");
        rightTriggerAction = CreateInput("Right Trigger", "<XRController>{RightHand}/trigger", "Axis");
        leftPositionAction = CreateInput("Left Hand Position", "<XRController>{LeftHand}/devicePosition", "Vector3");
        rightPositionAction = CreateInput("Right Hand Position", "<XRController>{RightHand}/devicePosition", "Vector3");
        leftRotationAction = CreateInput("Left Hand Rotation", "<XRController>{LeftHand}/deviceRotation", "Quaternion");
        rightRotationAction = CreateInput("Right Hand Rotation", "<XRController>{RightHand}/deviceRotation", "Quaternion");

        leftGripAction.Enable();
        rightGripAction.Enable();
        leftTriggerAction.Enable();
        rightTriggerAction.Enable();
        leftPositionAction.Enable();
        rightPositionAction.Enable();
        leftRotationAction.Enable();
        rightRotationAction.Enable();

        if (CampusContextService.Instance != null)
            CampusContextService.Instance.ContextChanged += RefreshContext;
        if (CampusProgressService.Instance != null)
        {
            CampusProgressService.Instance.LocationStateChanged += OnLocationStateChanged;
            CampusProgressService.Instance.StampAwarded += OnStampAwarded;
        }
        if (CampusJourneyService.Instance != null)
            CampusJourneyService.Instance.JourneyChanged += RefreshJourneyPage;
        if (CampusSecretService.Instance != null)
            CampusSecretService.Instance.SecretRevealed += OnSecretRevealed;
    }

    private void OnDisable()
    {
        if (CampusContextService.Instance != null)
            CampusContextService.Instance.ContextChanged -= RefreshContext;
        if (CampusProgressService.Instance != null)
        {
            CampusProgressService.Instance.LocationStateChanged -= OnLocationStateChanged;
            CampusProgressService.Instance.StampAwarded -= OnStampAwarded;
        }
        if (CampusJourneyService.Instance != null)
            CampusJourneyService.Instance.JourneyChanged -= RefreshJourneyPage;
        if (CampusSecretService.Instance != null)
            CampusSecretService.Instance.SecretRevealed -= OnSecretRevealed;
        leftGripAction?.Dispose();
        rightGripAction?.Dispose();
        leftTriggerAction?.Dispose();
        rightTriggerAction?.Dispose();
        leftPositionAction?.Dispose();
        rightPositionAction?.Dispose();
        leftRotationAction?.Dispose();
        rightRotationAction?.Dispose();
    }

    private void Start()
    {
        if (head == null && Camera.main != null)
            head = Camera.main.transform;
        FindTrackingOrigin();
        if (head != null)
        {
            var flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (flatForward.sqrMagnitude < 0.01f)
                flatForward = Vector3.forward;
            transform.position = GetSafeHeadPosition() + head.right * viewOffset.x +
                                 Vector3.up * viewOffset.y + flatForward * viewOffset.z;
        }
        RefreshContext();
    }

    private void Update()
    {
        if (head == null && Camera.main != null)
        {
            head = Camera.main.transform;
            FindTrackingOrigin();
        }
        if (head == null)
            return;

        UpdateGripInteraction();
        UpdateMenuRayInteraction();
        if (heldHand != 0)
            UpdateHeldPose();
        else
            FollowHead();
        UpdateGrabHighlight();
    }

    private void LateUpdate()
    {
        UpdateProjectedMenuPose();
        UpdateThinkingIndicator();
    }

    private void UpdateProjectedMenuPose()
    {
        if (uiCanvas == null || head == null)
            return;

        var towardHead = Vector3.ProjectOnPlane(
            head.position - transform.position, Vector3.up).normalized;
        if (towardHead.sqrMagnitude < 0.01f)
            towardHead = Vector3.back;

        // The menu follows the letter's position, but never inherits its pitch
        // or roll. Unity UI faces along local -Z, so +Z points away from viewer.
        var pageHeight = projectedMenuHeight +
                         (activePageId == "location" ? projectedLocationExtraHeight : 0f);
        uiCanvas.transform.position = transform.position +
                                      Vector3.up * pageHeight -
                                      towardHead * projectedMenuExtraDistance;
        // World-space UI renders toward local -Z. Keep its +Z axis away from the
        // viewer, and remove all pitch/roll so the menu is always truly vertical.
        var menuForward = heldHand != 0
            ? Vector3.ProjectOnPlane(-transform.forward, Vector3.up).normalized
            : -towardHead;
        if (menuForward.sqrMagnitude < 0.01f)
            menuForward = -towardHead;
        uiCanvas.transform.rotation = Quaternion.LookRotation(menuForward, Vector3.up);
    }

    private void UpdateThinkingIndicator()
    {
        if (thinkingIndicatorText == null)
            return;
        var thinking = State == AdmissionLetterState.Thinking && isOpen;
        thinkingIndicatorText.gameObject.SetActive(thinking);
        if (thinking)
            thinkingIndicatorText.rectTransform.localRotation =
                Quaternion.Euler(0f, 0f, -Time.unscaledTime * 150f);
    }

    private void FollowHead()
    {
        var flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.01f)
            flatForward = transform.forward;
        var target = GetSafeHeadPosition() + head.right * viewOffset.x + Vector3.up * viewOffset.y + flatForward * viewOffset.z;
        var bob = Mathf.Sin(Time.unscaledTime * 1.8f) * 0.018f;
        target += Vector3.up * bob;
        transform.position = Vector3.SmoothDamp(transform.position, target, ref followVelocity, followSmoothTime);
        var look = head.position - transform.position;
        if (look.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look, Vector3.up), Time.unscaledDeltaTime * 8f);
    }

    private void UpdateGripInteraction()
    {
        var leftPressed = leftGripAction.ReadValue<float>() > 0.55f;
        var rightPressed = rightGripAction.ReadValue<float>() > 0.55f;
        var leftTriggerPressed = leftTriggerAction.ReadValue<float>() > 0.55f;

        if (leftPressed && !leftGripWasPressed)
            ToggleOpen();
        if (rightPressed && !rightGripWasPressed)
            BeginListening();

        if (leftTriggerPressed && !leftTriggerWasPressed)
            ToggleLeftHandAttachment();

        if (!rightPressed && rightGripWasPressed)
            SubmitPlaceholderQuestion();
        leftGripWasPressed = leftPressed;
        rightGripWasPressed = rightPressed;
        leftTriggerWasPressed = leftTriggerPressed;
    }

    private void UpdateMenuRayInteraction()
    {
        AdmissionLetterMenuButton target = null;
        if (isOpen && uiCanvas != null && uiCanvas.gameObject.activeInHierarchy)
        {
            GetHandPose(1, out var rayOrigin, out var rayRotation);
            if (Physics.Raycast(
                    rayOrigin, rayRotation * Vector3.forward, out var hit,
                    selectionRayDistance, ~0, QueryTriggerInteraction.Collide))
                target = hit.collider.GetComponent<AdmissionLetterMenuButton>();
        }

        if (target != hoveredMenuButton)
        {
            hoveredMenuButton?.SetHovered(false);
            hoveredMenuButton = target;
            hoveredMenuButton?.SetHovered(true);
        }

        var triggerPressed = rightTriggerAction.ReadValue<float>() > 0.55f;
        if (triggerPressed && !rightTriggerWasPressed)
            hoveredMenuButton?.Activate();
        rightTriggerWasPressed = triggerPressed;
    }

    public void HandleMenuAction(string actionId)
    {
        if (TryHandleScanResultAction(actionId))
            return;
        switch (actionId)
        {
            case "page_guide":
                ShowPage("guide");
                break;
            case "page_suggestions":
                ShowPage("suggestions");
                break;
            case "page_map":
                ShowPage("map");
                break;
            case "page_collection":
                ShowPage("collection");
                break;
            case "page_experience_report":
                ShowPage("report");
                RefreshExperienceReport();
                break;
            case "report_ask_ai":
                AskForAIExperienceReport();
                break;
            case "dongwu_photo":
                CampusPhotoService.Instance?.RequestDongwuGatePhoto();
                break;
            case "page_exhibit":
                ShowPage("exhibit");
                break;
            case "exhibit_history": GenerateExhibit("AI 发展之路"); break;
            case "exhibit_campus": GenerateExhibit("智慧校园一天"); break;
            case "exhibit_classroom": GenerateExhibit("未来课堂"); break;
            case "exhibit_ask_ai": AskForExhibitExpansion(); break;
            case "page_memory":
                ShowPage("memory");
                break;
            case "memory_clear_request": RequestMemoryClear(); break;
            case "page_tasks":
                ShowPage("tasks");
                RefreshJourneyPage();
                break;
            case "toggle_journey":
                CampusJourneyService.Instance?.ToggleJourney();
                RefreshJourneyPage();
                break;
            case "restart_tutorial":
                CampusTutorialService.Instance?.RestartTutorial();
                hintText.text = "新手引导已重新开始";
                break;
            case "page_tutorial":
                CampusTutorialService.Instance?.ShowTutorialPage();
                break;
            case "tutorial_previous":
                CampusTutorialService.Instance?.ShowPreviousInstruction();
                break;
            case "tutorial_next":
                CampusTutorialService.Instance?.ShowNextInstruction();
                break;
            case "page_future_schedule":
                ShowPage(CampusUserProfileService.Instance != null &&
                         CampusUserProfileService.Instance.HasInterest
                    ? "timetable"
                    : "interest");
                RefreshTimetable();
                break;
            case "page_identity":
                ShowPage("identity");
                RefreshIdentityPage();
                break;
            case "identity_listen":
                awaitingIdentitySpeech = true;
                BeginListening();
                SetAnswerText("请清楚地说：我叫小明，专业是人工智能。\n说完后松开右手侧边握持键。");
                break;
            case "identity_quick_fill":
                if (CampusUserProfileService.Instance != null &&
                    CampusUserProfileService.Instance.SetIdentity("新同学", "人工智能"))
                {
                    CampusProgressService.Instance?.CompleteTask("tutorial_identity_recorded");
                    awaitingIdentitySpeech = false;
                    State = AdmissionLetterState.Open;
                    CampusInteractionCoordinator.Instance?.TrySetMode(CampusInteractionMode.Menu);
                    RefreshIdentityPage();
                    ShowPage("identity");
                    hintText.text = "示例身份已输入完成，请继续打开校园地图";
                }
                break;
            case "ai_spatial_scan":
                RunSpatialScan();
                break;
            case "scan_view_nearest":
                ShowNearestScanResult();
                break;
            case "scan_navigate_nearest":
                NavigateToNearestScanResult();
                break;
            case "location_ask_ai":
                AskAboutSelectedLocation();
                break;
            case "location_knowledge_graph":
                if (selectedLocationBuilding != null)
                {
                    CampusSpatialKnowledgeGraphService.Instance?.Show(selectedLocationBuilding);
                    SetOpenImmediate(false);
                }
                break;
            case "ask_here":
                AskSuggestedQuestion(BuildHereQuestion());
                break;
            case "ask_new_student":
                AskSuggestedQuestion("我是第一次来学校的新生，现在最需要了解哪些事情？请给我三条简短建议");
                break;
            case "ask_next_step":
                AskSuggestedQuestion(BuildNextStepQuestion());
                break;
            case "ask_interest":
                AskSuggestedQuestion(BuildInterestQuestion());
                break;
            case "location_navigate":
                if (selectedLocationBuilding != null)
                    StartMapNavigation(selectedLocationBuilding.locationId);
                break;
            case "interest_data_science":
                SelectInterest("data_science_big_data");
                break;
            case "interest_ai":
                SelectInterest("artificial_intelligence");
                break;
            case "interest_robotics":
                SelectInterest("robotics_engineering");
                break;
            case "interest_mechatronic":
                SelectInterest("mechatronic_engineering");
                break;
            case "change_interest":
                ShowPage("interest");
                break;
            case "back_main":
                ShowPage("main");
                break;
            case "answer_previous":
                ShowAnswerPage(answerPageIndex - 1);
                break;
            case "answer_next":
                ShowAnswerPage(answerPageIndex + 1);
                break;
            case "scene_main":
                LoadCampusScene(0);
                break;
            case "scene_classroom":
                LoadCampusScene(1);
                break;
            case "scene_exhibition":
                LoadCampusScene(2);
                break;
            case "route_recommended":
                StartMapNavigation(GetRecommendedDestination());
                break;
            case "route_dongwu_gate":
                StartMapNavigation("main_dongwu_gate");
                break;
            case "route_innovation_corridor":
                StartMapNavigation("main_innovation_corridor");
                break;
            case "route_classroom":
                StartMapNavigation("classroom_teaching_area");
                break;
            case "route_exhibition":
                StartMapNavigation("exhibition_ai_history");
                break;
        }
    }

    public bool ShowPageFromAI(string pageId)
    {
        if (pageId != "main" && pageId != "guide" && pageId != "map" &&
            pageId != "collection" && pageId != "tasks" && pageId != "interest" &&
            pageId != "timetable" && pageId != "suggestions" && pageId != "report" &&
            pageId != "exhibit" && pageId != "memory")
            return false;
        if (!isOpen)
        {
            if (heldHand == 0)
                SetOpenImmediate(true);
            else
                ToggleOpen();
        }
        ShowPage(pageId);
        return true;
    }

    private void SelectInterest(string interestId)
    {
        if (CampusUserProfileService.Instance == null ||
            !CampusUserProfileService.Instance.SelectInterest(interestId))
        {
            CampusFeedbackService.Instance?.Error();
            return;
        }
        CampusFeedbackService.Instance?.TaskComplete();
        CampusProgressService.Instance?.CompleteTask("tutorial_interest_selected");
        RefreshTimetable();
        ShowPage("timetable");
        PlayLetterFeedback(new Color(0.2f, 0.85f, 1f, 1f), false);
    }

    private void RefreshTimetable()
    {
        if (timetableTitleText == null || timetableBodyText == null)
            return;
        var interest = CampusUserProfileService.Instance != null
            ? CampusUserProfileService.Instance.InterestId
            : string.Empty;
        timetableTitleText.text = "未来课表 · " +
            (CampusUserProfileService.Instance != null
                ? CampusUserProfileService.Instance.InterestName
                : "测试方向");
        switch (interest)
        {
            case "data_science_big_data":
                timetableBodyText.text = "09:00  数据分析基础\n10:30  大数据平台实践\n14:00  数据可视化设计\n16:00  数据创新项目\n\n【测试课表，正式课程资料待替换】";
                break;
            case "artificial_intelligence":
                timetableBodyText.text = "09:00  机器学习基础\n10:30  生成式 AI 实践\n14:00  智能交互设计\n16:00  AI 创新实验\n\n【测试课表，正式课程资料待替换】";
                break;
            case "robotics_engineering":
                timetableBodyText.text = "09:00  机器人感知技术\n10:30  运动控制实验\n14:00  协作机器人实践\n16:00  机器人创新项目\n\n【测试课表，正式课程资料待替换】";
                break;
            case "mechatronic_engineering":
                timetableBodyText.text = "09:00  机械设计基础\n10:30  电气控制实验\n14:00  PLC 系统实践\n16:00  机电综合项目\n\n【测试课表，正式课程资料待替换】";
                break;
            default:
                timetableTitleText.text = "未来课表";
                timetableBodyText.text = "请先选择你感兴趣的方向。";
                break;
        }
    }

    public void ShowLocationInfo(BuildingInfo building)
    {
        if (building == null || uiCanvas == null)
            return;

        selectedLocationBuilding = building;
        locationTitleText.text = "◈ " + building.buildingName;
        locationDescriptionText.text = string.IsNullOrWhiteSpace(building.buildingDescription)
            ? "该地点暂未录入详细说明，可通过校园知识库了解相关信息。"
            : building.buildingDescription;
        ShowPage("location");
        uiCanvas.gameObject.SetActive(true);
        uiCanvas.transform.localScale = UiFullScale;
        uiCanvas.GetComponent<CanvasGroup>().alpha = 1f;
    }

    private void RunSpatialScan()
    {
        if (AISpatialScanner.Instance == null)
        {
            if (scanResultsText != null) scanResultsText.text = "AI 空间扫描服务尚未初始化。";
            ShowPage("scan");
            return;
        }

        var result = AISpatialScanner.Instance.Scan(head);
        if (scanResultsText != null) scanResultsText.text = result;
        RenderScanResultRows();
        ShowPage("scan");
        PlayLetterFeedback(new Color(0.1f, 0.9f, 1f, 1f), false);
    }

    public void RunTutorialSpatialScan()
    {
        RunSpatialScan();
    }

    private bool TryHandleScanResultAction(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId) || AISpatialScanner.Instance == null)
            return false;
        var prefix = actionId.StartsWith("scan_ask_", StringComparison.Ordinal)
            ? "scan_ask_" : actionId.StartsWith("scan_navigate_", StringComparison.Ordinal)
                ? "scan_navigate_" : null;
        if (prefix == null) return false;
        if (!int.TryParse(actionId.Substring(prefix.Length), out var index) ||
            index < 0 || index >= AISpatialScanner.Instance.LastResults.Count)
            return true;

        var building = AISpatialScanner.Instance.LastResults[index];
        if (building == null) return true;
        if (prefix == "scan_ask_")
        {
            selectedLocationBuilding = building;
            AskAboutSelectedLocation();
        }
        else StartMapNavigation(building.locationId);
        return true;
    }

    private void RenderScanResultRows()
    {
        foreach (var row in scanResultRows)
            if (row != null) Destroy(row);
        scanResultRows.Clear();
        if (scanPage == null || AISpatialScanner.Instance == null) return;

        var results = AISpatialScanner.Instance.LastResults;
        for (var index = 0; index < Mathf.Min(4, results.Count); index++)
        {
            var building = results[index];
            if (building == null) continue;
            var y = 35f - index * 38f;
            var label = CreateText(scanPage, building.buildingName, 16,
                new Vector2(-110f, y), new Vector2(185f, 30f), TextAnchor.MiddleLeft);
            label.color = new Color(0.88f, 0.96f, 1f, 1f);
            scanResultRows.Add(label.gameObject);
            var explain = CreateMenuButton(scanPage, "AI讲解", "scan_ask_" + index,
                new Vector2(78f, y), new Vector2(92f, 30f), "AdmissionLetterUI/button_ai_guide");
            var navigate = CreateMenuButton(scanPage, "导航", "scan_navigate_" + index,
                new Vector2(178f, y), new Vector2(76f, 30f), "AdmissionLetterUI/button_default");
            scanResultRows.Add(explain.gameObject);
            scanResultRows.Add(navigate.gameObject);
        }
    }

    private void ShowNearestScanResult()
    {
        var building = AISpatialScanner.Instance?.NearestResult;
        if (building == null)
        {
            if (scanResultsText != null) scanResultsText.text = "暂无识别目标，请重新扫描。";
            return;
        }
        CampusContextService.Instance?.FocusLocation(
            building.locationId, building.buildingName, building.buildingDescription);
        ShowLocationInfo(building);
    }

    private void NavigateToNearestScanResult()
    {
        var building = AISpatialScanner.Instance?.NearestResult;
        if (building == null || CampusNavigationService.Instance == null ||
            !CampusNavigationService.Instance.StartNavigation(building.locationId))
        {
            if (scanResultsText != null) scanResultsText.text = "当前没有可以导航的扫描目标。";
            CampusFeedbackService.Instance?.Error();
            return;
        }
        CampusFeedbackService.Instance?.Confirm();
        CampusTaskService.Instance?.Report(CampusTaskTrigger.StartNavigation);
        if (isOpen)
            CloseForPhoto();
    }

    private void AskAboutSelectedLocation()
    {
        if (selectedLocationBuilding == null)
            return;
        CampusContextService.Instance?.FocusLocation(
            selectedLocationBuilding.locationId,
            selectedLocationBuilding.buildingName,
            selectedLocationBuilding.buildingDescription);
        ShowPage("guide");
        SubmitRecognizedText("请介绍一下" + selectedLocationBuilding.buildingName + "，并告诉我新生最值得关注的信息");
    }

    private void AskSuggestedQuestion(string question)
    {
        ShowPage("guide");
        SubmitRecognizedText(question);
    }

    private string BuildHereQuestion()
    {
        var context = CampusContextService.Instance;
        if (context != null && !string.IsNullOrWhiteSpace(context.LocationName))
            return "请介绍一下" + context.LocationName + "，并告诉我这里最值得体验什么";
        return "请结合我所在的当前场景，简短介绍这里，并告诉我附近最值得体验什么";
    }

    private static string BuildNextStepQuestion()
    {
        var journey = CampusJourneyService.Instance;
        if (journey != null && !journey.IsComplete)
            return "我当前的推荐任务是“" + journey.CurrentStepTitle + "”，请告诉我接下来该怎么做";
        return "我已经完成推荐体验，请根据新生需求推荐一个接下来值得探索的校园内容";
    }

    private static string BuildInterestQuestion()
    {
        var profile = CampusUserProfileService.Instance;
        if (profile != null && profile.HasInterest)
            return "我对“" + profile.InterestName + "”感兴趣，请推荐相关课程、地点或校园资源";
        return "请通过三个简单问题帮我判断适合关注的专业方向和校园资源";
    }

    private void AskForAIExperienceReport()
    {
        var reports = CampusExperienceReportService.Instance;
        if (reports == null)
        {
            if (experienceReportText != null)
                experienceReportText.text = "探索报告服务尚未初始化，请稍后再试。";
            return;
        }
        ShowPage("guide");
        SubmitRecognizedText(reports.BuildAIReportQuestion());
    }

    private void GenerateExhibit(string theme)
    {
        CampusGenerativeExhibitService.Instance?.Generate(theme);
        RefreshExhibitPage();
    }

    private void AskForExhibitExpansion()
    {
        var exhibit = CampusGenerativeExhibitService.Instance;
        if (exhibit == null) return;
        ShowPage("guide");
        SubmitRecognizedText(exhibit.BuildAIQuestion());
    }

    private void RequestMemoryClear()
    {
        CampusAIActionConfirmationService.Instance?.Request(
            "清除本机保存的兴趣方向、参观记录、印章、提问次数和彩蛋。该操作无法撤销。",
            () =>
            {
                CampusMemoryService.Instance?.Clear();
                RefreshMemoryPage();
                CampusTutorialService.Instance?.RestartTutorial();
            });
    }

    public void HideLocationInfo()
    {
        if (activePageId != "location")
            return;

        if (animationRoutine != null)
        {
            StopCoroutine(animationRoutine);
            animationRoutine = null;
        }
        SetOpenImmediate(false);
    }

    private void ShowPage(string pageId)
    {
        hoveredMenuButton?.ResetVisual();
        foreach (var page in menuPages)
            page.Value.SetActive(page.Key == pageId);
        activePageId = pageId;
        hoveredMenuButton = null;
        if (pageId == "collection")
            RefreshCollectionPage();
        else if (pageId == "report")
            RefreshExperienceReport();
        else if (pageId == "exhibit")
            RefreshExhibitPage();
        else if (pageId == "memory")
            RefreshMemoryPage();
        CampusTaskService.Instance?.Report(CampusTaskTrigger.OpenMenuPage, pageId);
    }

    private void RefreshJourneyPage()
    {
        if (journeyStatusText == null || CampusJourneyService.Instance == null)
            return;
        var journey = CampusJourneyService.Instance;
        if (!journey.IsEnabled)
        {
            journeyStatusText.text = "推荐主线：已暂停（不影响自由探索）\n\n" +
                                     "随时可以重新开启，已完成内容仍会保留。";
            return;
        }
        if (journey.IsComplete)
        {
            journeyStatusText.text = "推荐主线：已完成\n\n你仍可以继续自由探索任意场景和地点。";
            return;
        }
        journeyStatusText.text = $"推荐主线 {journey.CompletedStepCount}/{journey.StepCount}\n\n" +
                                 $"当前建议：{journey.CurrentStepTitle}\n" +
                                 $"预计：{journey.CurrentStepDuration}\n\n" +
                                 "这只是建议，不会锁定场景、按钮或移动。";
    }

    private void LoadCampusScene(int buildIndex)
    {
        SetOpenImmediate(false);
        PicoSceneNavigator.Instance?.LoadSceneByIndex(buildIndex);
    }

    private void StartMapNavigation(string locationId)
    {
        if (string.IsNullOrWhiteSpace(locationId) || CampusNavigationService.Instance == null ||
            !CampusNavigationService.Instance.StartNavigation(locationId))
        {
            hintText.text = "当前目的地尚未注册，请进入对应场景后重试";
            CampusFeedbackService.Instance?.Error();
            return;
        }
        CampusFeedbackService.Instance?.Confirm();
        CampusTaskService.Instance?.Report(CampusTaskTrigger.StartNavigation);
        SetOpenImmediate(false);
    }

    private static string GetRecommendedDestination()
    {
        var progress = CampusProgressService.Instance;
        if (progress == null) return "main_innovation_corridor";
        if (progress.GetLocationState("main_innovation_corridor") != CampusDiscoveryState.Completed)
            return "main_innovation_corridor";
        if (progress.GetLocationState("exhibition_ai_history") != CampusDiscoveryState.Completed)
            return "exhibition_ai_history";
        return "classroom_teaching_area";
    }

    private void ToggleLeftHandAttachment()
    {
        if (heldHand == -1)
        {
            EndLeftHandGrab();
            return;
        }

        if (IsLeftRayTargetingLetter())
        {
            heldHand = -1;
            if (interactionCollider != null)
                interactionCollider.enabled = false;
            State = AdmissionLetterState.Held;
            CampusTaskService.Instance?.Report(CampusTaskTrigger.AttachLetter);
            CampusFeedbackService.Instance?.Confirm(false);
            hintText.text = "已拿到左手 · 再按一次左手食指扳机键放下";
        }
    }

    private void EndLeftHandGrab()
    {
        if (heldHand != -1)
            return;
        heldHand = 0;
        if (interactionCollider != null)
            interactionCollider.enabled = true;
        State = isOpen ? AdmissionLetterState.Open : AdmissionLetterState.Following;
        hintText.text = isOpen
            ? "通知书会继续漂浮跟随 · 按住右手侧边握持键可对话"
            : "按左手侧边握持键打开通知书";
    }

    private void UpdateHeldPose()
    {
        GetHandPose(-1, out var handPosition, out var handRotation);
        transform.position = handPosition + handRotation * leftHandHoldOffset;
        var look = head.position - transform.position;
        if (look.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.Slerp(
                transform.rotation, Quaternion.LookRotation(look, Vector3.up),
                Time.unscaledDeltaTime * 14f);
    }

    private void UpdateGrabHighlight()
    {
        var targetScale = heldHand != 0
            ? Vector3.one * 0.78f
            : (IsLeftRayTargetingLetter() ? Vector3.one * 1.035f : Vector3.one);
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.unscaledDeltaTime * 10f);
    }

    private bool IsLeftRayTargetingLetter()
    {
        if (interactionCollider == null || heldHand != 0)
            return heldHand != 0;

        GetHandPose(-1, out var rayOrigin, out var rayRotation);
        if (!Physics.Raycast(
                rayOrigin, rayRotation * Vector3.forward, out var hit,
                selectionRayDistance, ~0, QueryTriggerInteraction.Collide))
            return false;

        return hit.collider == interactionCollider ||
               hit.collider.transform.IsChildOf(transform);
    }

    private void GetHandPose(int hand, out Vector3 position, out Quaternion rotation)
    {
        var localPosition = hand < 0
            ? leftPositionAction.ReadValue<Vector3>()
            : rightPositionAction.ReadValue<Vector3>();
        var localRotation = hand < 0
            ? leftRotationAction.ReadValue<Quaternion>()
            : rightRotationAction.ReadValue<Quaternion>();
        if (trackingOrigin != null)
        {
            position = trackingOrigin.TransformPoint(localPosition);
            rotation = trackingOrigin.rotation * localRotation;
        }
        else
        {
            position = localPosition;
            rotation = localRotation;
        }
    }

    private void FindTrackingOrigin()
    {
        var xrOrigin = head != null ? head.GetComponentInParent<XROrigin>() : null;
        trackingOrigin = xrOrigin != null ? xrOrigin.transform : null;
    }

    private Vector3 GetSafeHeadPosition()
    {
        var position = head.position;
        if (trackingOrigin != null && position.y - trackingOrigin.position.y < 0.6f)
            position.y = trackingOrigin.position.y + fallbackEyeHeight;
        return position;
    }

    private static InputAction CreateInput(string name, string binding, string expectedType)
    {
        return new InputAction(name, InputActionType.Value, binding, expectedControlType: expectedType);
    }

    public void RecoverIfInvalid()
    {
        if (head == null) return;
        var position = transform.position;
        var invalidNumber = float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z);
        var tooFar = Vector3.Distance(position, head.position) > 8f;
        var tooLow = trackingOrigin != null && position.y < trackingOrigin.position.y - 0.5f;
        if (!invalidNumber && !tooFar && !tooLow) return;

        heldHand = 0;
        if (interactionCollider != null) interactionCollider.enabled = true;
        SetOpenImmediate(false);
        var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        transform.position = GetSafeHeadPosition() + head.right * viewOffset.x +
                             Vector3.up * viewOffset.y + forward * viewOffset.z;
        followVelocity = Vector3.zero;
    }

    public void ResetForShowcase()
    {
        if (animationRoutine != null)
        {
            StopCoroutine(animationRoutine);
            animationRoutine = null;
        }
        if (feedbackRoutine != null)
        {
            StopCoroutine(feedbackRoutine);
            feedbackRoutine = null;
        }
        heldHand = 0;
        if (interactionCollider != null)
            interactionCollider.enabled = true;
        if (feedbackParticles != null)
            feedbackParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (feedbackLight != null)
            feedbackLight.enabled = false;
        SetMaterialColor(paperMaterial, new Color(0.78f, 0.12f, 0.08f, 1f));
        SetMaterialColor(coverMaterial, new Color(0.92f, 0.72f, 0.18f, 1f));
        SetOpenImmediate(false);
        ShowPage("main");
        lastNotifiedLocationId = string.Empty;
        lastLocationNotificationTime = -999f;
        followVelocity = Vector3.zero;
        RecoverIfInvalid();
        if (hintText != null)
            hintText.text = "欢迎体验 AI 虚拟校园 · 左手射线选择通知书";
    }

    public void NotifyLocation(string locationName)
    {
        if (isOpen)
            return;
        State = AdmissionLetterState.Notifying;
        hintText.text = $"发现：{locationName}\n按左手侧边握持键打开通知书";
    }

    public void ToggleOpen()
    {
        if (CampusInteractionCoordinator.Instance != null &&
            CampusInteractionCoordinator.Instance.Mode == CampusInteractionMode.Loading)
            return;
        if (!isOpen && heldHand == 0)
        {
            if (hintText != null)
                hintText.text = "请先用左手食指扳机键拿起通知书，再按左手侧边握持键打开";
            CampusFeedbackService.Instance?.Error(false);
            return;
        }
        if (animationRoutine != null)
            StopCoroutine(animationRoutine);
        animationRoutine = StartCoroutine(AnimateOpen(!isOpen));
    }

    public void CloseForPhoto()
    {
        if (!isOpen) return;
        if (animationRoutine != null) StopCoroutine(animationRoutine);
        animationRoutine = null;
        SetOpenImmediate(false);
    }

    private IEnumerator AnimateOpen(bool open)
    {
        State = AdmissionLetterState.Opening;
        var from = coverPivot.localRotation;
        var to = Quaternion.Euler(0f, open ? OpenCoverAngle : 0f, 0f);
        var elapsed = 0f;
        if (open)
        {
            ShowPage("main");
            CampusTaskService.Instance?.Report(CampusTaskTrigger.OpenLetter);
            uiCanvas.gameObject.SetActive(true);
            projectionBeam.SetActive(true);
        }

        while (elapsed < openDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.SmoothStep(0f, 1f, elapsed / openDuration);
            coverPivot.localRotation = Quaternion.Slerp(from, to, t);
            uiCanvas.GetComponent<CanvasGroup>().alpha = open ? t : 1f - t;
            uiCanvas.transform.localScale = Vector3.Lerp(
                UiCollapsedScale, UiFullScale, open ? t : 1f - t);
            yield return null;
        }

        isOpen = open;
        coverPivot.localRotation = to;
        uiCanvas.gameObject.SetActive(open);
        projectionBeam.SetActive(open);
        State = open ? AdmissionLetterState.Open : AdmissionLetterState.Following;
        CampusInteractionCoordinator.Instance?.TrySetMode(
            open ? CampusInteractionMode.Menu : CampusInteractionMode.Exploration);
        hintText.text = open ? "按住右手侧边握持键说话，松开发送" : "按左手侧边握持键打开通知书";
        animationRoutine = null;
    }

    private void SetOpenImmediate(bool open)
    {
        isOpen = open;
        coverPivot.localRotation = Quaternion.Euler(0f, open ? OpenCoverAngle : 0f, 0f);
        uiCanvas.gameObject.SetActive(open);
        uiCanvas.transform.localScale = open ? UiFullScale : UiCollapsedScale;
        uiCanvas.GetComponent<CanvasGroup>().alpha = open ? 1f : 0f;
        if (projectionBeam != null)
            projectionBeam.SetActive(open);
        if (open)
            ShowPage("main");
        State = open ? AdmissionLetterState.Open : AdmissionLetterState.Following;
        CampusInteractionCoordinator.Instance?.TrySetMode(
            open ? CampusInteractionMode.Menu : CampusInteractionMode.Exploration);
    }

    private void BeginListening()
    {
        if (!isOpen)
        {
            if (animationRoutine != null)
                StopCoroutine(animationRoutine);
            animationRoutine = null;
            SetOpenImmediate(true);
        }
        if (listeningTimeoutRoutine != null)
        {
            StopCoroutine(listeningTimeoutRoutine);
            listeningTimeoutRoutine = null;
        }
        State = AdmissionLetterState.Listening;
        CampusInteractionCoordinator.Instance?.TrySetMode(CampusInteractionMode.Listening);
        ShowPage("guide");
        SetAnswerText("正在聆听……");
        hintText.text = "松开右手侧边握持键发送";
        if (VoiceChatClient.Instance == null)
        {
            SetVoiceCaptureFailed("语音组件尚未启动，请稍候再试。");
            return;
        }
        if (!VoiceChatClient.Instance.BeginVoiceCapture())
            SetVoiceCaptureFailed(VoiceChatClient.Instance.LastCaptureError);
    }

    private void SubmitPlaceholderQuestion()
    {
        if (State != AdmissionLetterState.Listening)
            return;

        if (VoiceChatClient.Instance != null && VoiceChatClient.Instance.EndVoiceCapture())
        {
            SetAnswerText("正在识别语音……");
            hintText.text = "识别完成后将查询校园知识库";
            if (listeningTimeoutRoutine != null)
                StopCoroutine(listeningTimeoutRoutine);
            listeningTimeoutRoutine = StartCoroutine(ListeningTimeout());
            return;
        }
        var detail = VoiceChatClient.Instance == null
            ? "语音组件尚未启动，请稍候再试。"
            : VoiceChatClient.Instance.LastCaptureError;
        SetVoiceCaptureFailed(detail);
    }

    private void SetVoiceCaptureFailed(string detail)
    {
        if (listeningTimeoutRoutine != null)
        {
            StopCoroutine(listeningTimeoutRoutine);
            listeningTimeoutRoutine = null;
        }
        State = AdmissionLetterState.Open;
        CampusInteractionCoordinator.Instance?.TrySetMode(CampusInteractionMode.Menu);
        SetAnswerText("语音识别未开始。\n" +
                      (string.IsNullOrWhiteSpace(detail) ? "请检查 FunASR 与麦克风后重试。" : detail));
        hintText.text = "按住右手侧边握持键重新说话";
    }

    private IEnumerator ListeningTimeout()
    {
        yield return new WaitForSecondsRealtime(10f);
        listeningTimeoutRoutine = null;
        if (State != AdmissionLetterState.Listening)
            yield break;
        State = AdmissionLetterState.Open;
        CampusInteractionCoordinator.Instance?.TrySetMode(CampusInteractionMode.Menu);
        SetAnswerText("没有识别到语音，请按住右手侧边握持键再试一次。");
        hintText.text = "按左手侧边握持键打开通知书";
    }

    public void SubmitRecognizedText(string recognizedText)
    {
        if (awaitingIdentitySpeech)
        {
            awaitingIdentitySpeech = false;
            if (TrySaveSpokenIdentity(recognizedText, out var identityMessage))
            {
                State = AdmissionLetterState.Open;
                CampusInteractionCoordinator.Instance?.TrySetMode(CampusInteractionMode.Menu);
                ShowPage("identity");
                RefreshIdentityPage();
                hintText.text = identityMessage;
                return;
            }
            SetAnswerText(identityMessage);
            hintText.text = "没有识别完整，请按“重新录入”再说一次";
            State = AdmissionLetterState.Open;
            CampusInteractionCoordinator.Instance?.TrySetMode(CampusInteractionMode.Menu);
            return;
        }
        var context = CampusContextService.Instance;
        if (chatService == null || chatService.IsBusy)
        {
            SetAnswerText("AI 正在处理上一条问题，请稍候再试。");
            hintText.text = "AI 处理中，稍后可以再次提问";
            State = AdmissionLetterState.Open;
            CampusInteractionCoordinator.Instance?.TrySetMode(CampusInteractionMode.Menu);
            return;
        }
        if (context == null)
        {
            SetAnswerText("校园 AI 上下文尚未就绪，请稍后再试。");
            hintText.text = "按左手侧边握持键打开通知书";
            State = AdmissionLetterState.Open;
            CampusInteractionCoordinator.Instance?.TrySetMode(CampusInteractionMode.Menu);
            return;
        }

        State = AdmissionLetterState.Thinking;
        CampusInteractionCoordinator.Instance?.TrySetMode(CampusInteractionMode.Thinking);
        SetAnswerText("通知书正在思考……");
        hintText.text = chatService is DifyChatService
            ? "正在查询 Dify 校园知识库"
            : chatService is OllamaChatService
                ? "正在使用本机 Ollama"
                : "AI 尚未配置，正在使用本地模拟服务";

        var question = string.IsNullOrWhiteSpace(recognizedText) ? "请介绍一下这里" : recognizedText.Trim();
        if (CampusVisualContextService.Instance != null)
            question = CampusVisualContextService.Instance.EnrichQuestion(question, head);
        CampusProgressService.Instance?.IncrementQuestionCount();
        CampusTaskService.Instance?.Report(CampusTaskTrigger.AskQuestion);
        var request = context.CreateRequest(question, GetOrCreateUserId(), conversationId);
        chatService.Send(request, OnChatSuccess, OnChatError);
    }

    private static bool TrySaveSpokenIdentity(string speech, out string message)
    {
        message = "没有听清姓名和专业。请按示例说：我叫小明，专业是人工智能。";
        if (string.IsNullOrWhiteSpace(speech) || CampusUserProfileService.Instance == null)
            return false;
        var text = speech.Trim().Replace("。", string.Empty).Replace("！", string.Empty);
        var nameStart = text.IndexOf("我叫", StringComparison.Ordinal);
        var majorStart = text.IndexOf("专业是", StringComparison.Ordinal);
        if (nameStart < 0 || majorStart < 0 || majorStart <= nameStart + 2)
            return false;
        var name = text.Substring(nameStart + 2, majorStart - nameStart - 2).Trim('，', ',', '是', ' ');
        var major = text.Substring(majorStart + 3).Trim('，', ',', '是', ' ');
        if (major.EndsWith("专业", StringComparison.Ordinal))
            major = major.Substring(0, major.Length - 2);
        if (!CampusUserProfileService.Instance.SetIdentity(name, major))
            return false;
        CampusProgressService.Instance?.CompleteTask("tutorial_identity_recorded");
        message = "录入成功。AI 已记住你是" + name + "，专业是" + major + "。";
        return true;
    }

    public bool UseChatService(MonoBehaviour provider)
    {
        if (!(provider is IDifyChatService service))
            return false;
        chatService = service;
        return true;
    }

    private void OnChatSuccess(DifyChatResponse response)
    {
        conversationId = response.conversation_id ?? conversationId;
        State = AdmissionLetterState.Speaking;
        CampusInteractionCoordinator.Instance?.TrySetMode(CampusInteractionMode.Speaking);
        var spokenReply = response.command != null && !string.IsNullOrEmpty(response.command.speech)
            ? response.command.speech
            : response.answer;
        SetAnswerText(spokenReply);
        CampusTextToSpeechService.Ensure().Speak(spokenReply);
        if (response.command != null)
        {
            CommandReceived?.Invoke(response.command);
        }
        hintText.text = chatService is MockDifyChatService
            ? "模拟回答完成 · 按左手侧边握持键关闭"
            : "AI 回答完成 · 按左手侧边握持键关闭";
        StartCoroutine(ReturnToOpenState());
    }

    private void OnChatError(string error)
    {
        State = AdmissionLetterState.Open;
        CampusInteractionCoordinator.Instance?.TrySetMode(CampusInteractionMode.Menu);
        SetAnswerText("请求失败：" + error);
        if (hintText != null)
            hintText.text = "请确认电脑移动热点和一键服务均已启动，然后重新提问";
    }

    private IEnumerator ReturnToOpenState()
    {
        yield return new WaitForSecondsRealtime(1.2f);
        if (State == AdmissionLetterState.Speaking)
        {
            State = AdmissionLetterState.Open;
            CampusInteractionCoordinator.Instance?.TrySetMode(CampusInteractionMode.Menu);
        }
    }

    private void RefreshContext()
    {
        var context = CampusContextService.Instance;
        if (context == null || contextText == null)
            return;

        contextText.text = $"当前场景：{context.SceneName}\n关注地点：{context.LocationName}";
        if (context.HasRecentFocus &&
            (context.LocationId != lastNotifiedLocationId ||
             Time.unscaledTime - lastLocationNotificationTime >= 5f))
        {
            lastNotifiedLocationId = context.LocationId;
            lastLocationNotificationTime = Time.unscaledTime;
            NotifyLocation(context.LocationName);
        }
    }

    private void OnLocationStateChanged(string locationId, CampusDiscoveryState state)
    {
        if (state == CampusDiscoveryState.Discovered)
            PlayLetterFeedback(new Color(0.15f, 0.8f, 1f, 1f), false);
    }

    private void OnStampAwarded(string locationId)
    {
        var locationName = locationId;
        if (CampusLocationRegistry.Instance != null &&
            CampusLocationRegistry.Instance.TryGet(locationId, out var definition))
            locationName = definition.displayName;

        if (hintText != null)
            hintText.text = "获得探索印章 · " + locationName;
        CampusFeedbackService.Instance?.TaskComplete();
        PlayLetterFeedback(new Color(1f, 0.68f, 0.08f, 1f), true);
    }

    private void OnSecretRevealed(string title, string description)
    {
        if (hintText != null)
            hintText.text = "发现隐藏彩蛋 · " + title;
        SetAnswerText("【发现隐藏彩蛋】\n" + title + "\n\n" + description);
        if (isOpen)
            ShowPage("guide");
        CampusFeedbackService.Instance?.TaskComplete();
        PlayLetterFeedback(new Color(0.72f, 0.28f, 1f, 1f), true);
    }

    private void PlayLetterFeedback(Color color, bool playParticles)
    {
        if (feedbackRoutine != null)
            StopCoroutine(feedbackRoutine);
        feedbackRoutine = StartCoroutine(AnimateLetterFeedback(color));
        if (playParticles && feedbackParticles != null)
            feedbackParticles.Play(true);
    }

    private IEnumerator AnimateLetterFeedback(Color color)
    {
        feedbackLight.color = color;
        feedbackLight.enabled = true;
        var elapsed = 0f;
        const float duration = 0.85f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            var pulse = Mathf.Sin(Mathf.Clamp01(elapsed / duration) * Mathf.PI);
            feedbackLight.intensity = pulse * 2.4f;
            SetMaterialColor(paperMaterial, Color.Lerp(new Color(0.78f, 0.12f, 0.08f, 1f), color, pulse * 0.65f));
            SetMaterialColor(coverMaterial, Color.Lerp(new Color(0.92f, 0.72f, 0.18f, 1f), color, pulse * 0.55f));
            yield return null;
        }
        feedbackLight.enabled = false;
        SetMaterialColor(paperMaterial, new Color(0.78f, 0.12f, 0.08f, 1f));
        SetMaterialColor(coverMaterial, new Color(0.92f, 0.72f, 0.18f, 1f));
        feedbackRoutine = null;
    }

    private void BuildPlaceholderModel()
    {
        paperMaterial = CreateMaterial("Letter Paper", new Color(0.78f, 0.12f, 0.08f, 1f));
        coverMaterial = CreateMaterial("Letter Cover", new Color(0.92f, 0.72f, 0.18f, 1f));

        var basePage = GameObject.CreatePrimitive(PrimitiveType.Cube);
        basePage.name = "Back Page Placeholder";
        basePage.transform.SetParent(transform, false);
        basePage.transform.localScale = new Vector3(0.42f, 0.28f, 0.012f);
        backPageRenderer = basePage.GetComponent<Renderer>();
        backPageRenderer.sharedMaterial = paperMaterial;
        Destroy(basePage.GetComponent<Collider>());

        coverPivot = new GameObject("Cover Hinge").transform;
        coverPivot.SetParent(transform, false);
        // This is refined to the real BoardCover width after its visual is
        // instantiated. Its pivot must be on the board's left edge.
        coverPivot.localPosition = new Vector3(-0.21f, 0f, 0f);
        var coverPage = GameObject.CreatePrimitive(PrimitiveType.Cube);
        coverPage.name = "Front Cover Placeholder";
        coverPage.transform.SetParent(coverPivot, false);
        coverPage.transform.localPosition = new Vector3(0.21f, 0f, 0f);
        coverPage.transform.localScale = new Vector3(0.42f, 0.28f, 0.012f);
        coverPageRenderer = coverPage.GetComponent<Renderer>();
        coverPageRenderer.sharedMaterial = coverMaterial;
        Destroy(coverPage.GetComponent<Collider>());

        interactionCollider = gameObject.AddComponent<BoxCollider>();
        interactionCollider.isTrigger = true;
        interactionCollider.center = Vector3.zero;
        interactionCollider.size = new Vector3(0.46f, 0.32f, 0.08f);

        // Optional manual visual replacement. Drop a model at
        //   Resources/AdmissionLetterModel/BoardBack(.prefab|.fbx)  (下板)
        //   Resources/AdmissionLetterModel/BoardCover(.prefab|.fbx) (上板)
        // to swap ONLY the rendered visual. The placeholder cube keeps its
        // position, rotation and size data and is hidden underneath.
        ReplacePlaceholderVisual(backPageRenderer, "AdmissionLetterModel/BoardBack",
            "Board Back Manual Model");
        var coverWidth = ReplacePlaceholderVisual(coverPageRenderer,
            "AdmissionLetterModel/BoardCover", "Board Cover Manual Model");
        PlaceCoverHinge(coverWidth);

        BuildFeedbackEffects();

        BuildTemporaryUi();
    }

    private static float ReplacePlaceholderVisual(Renderer placeholder, string resourcePath, string objectName)
    {
        var model = Resources.Load<GameObject>(resourcePath);
        if (model == null)
            return placeholder.bounds.size.x;

        var anchor = placeholder.transform;
        var visual = UnityEngine.Object.Instantiate(model, anchor);
        visual.name = objectName;
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = BoardVisualRotation;

        foreach (var modelLight in visual.GetComponentsInChildren<Light>(true))
            modelLight.enabled = false;
        foreach (var modelCamera in visual.GetComponentsInChildren<Camera>(true))
            modelCamera.enabled = false;

        var renderers = visual.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Destroy(visual);
            return placeholder.bounds.size.x;
        }

        // The cube is retained solely for its existing transform and interaction
        // data. Neutralize its non-uniform scale so the imported board keeps its
        // authored, vertical proportions and size without being compressed.
        var lossy = anchor.lossyScale;
        // BoardVisualRotation is 90 degrees around Y, so the visual's local
        // X/Z axes pass through the placeholder's Z/X axes respectively.
        // Compensating in the old order would stretch the board enormously.
        var compensate = new Vector3(
            1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            1f / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)));
        visual.transform.localScale = compensate * BoardVisualScale;

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        var largestDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (largestDimension <= 0.0001f || float.IsNaN(largestDimension))
        {
            Destroy(visual);
            return placeholder.bounds.size.x;
        }

        // Center the visual on the existing board pivot. Z stays at zero so
        // the two boards are exactly coplanar after the cover unfolds.
        bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        var centerLocal = anchor.InverseTransformPoint(bounds.center);
        visual.transform.localPosition = new Vector3(-centerLocal.x, -centerLocal.y, 0f);
        placeholder.enabled = false;
        return bounds.size.x;
    }

    private void PlaceCoverHinge(float coverWidth)
    {
        if (coverWidth <= 0.0001f)
            coverWidth = 0.42f;
        var halfWidth = coverWidth * 0.5f;
        // BoardCover contains pages 1/2. From the player's view, local +X is
        // the letter's left side because the letter faces the player. Put the
        // hinge there, then open through -Y so it swings in the intended direction, like the
        // original two-colour placeholder letter.
        coverPivot.localPosition = new Vector3(halfWidth, 0f, 0f);
        coverPageRenderer.transform.localPosition = new Vector3(-halfWidth, 0f, 0f);
    }

    private void BuildFeedbackEffects()
    {
        var lightObject = new GameObject("Letter Feedback Light");
        lightObject.transform.SetParent(transform, false);
        lightObject.transform.localPosition = new Vector3(0f, 0.05f, -0.08f);
        feedbackLight = lightObject.AddComponent<Light>();
        feedbackLight.type = LightType.Point;
        feedbackLight.range = 1.8f;
        feedbackLight.intensity = 0f;
        feedbackLight.shadows = LightShadows.None;
        feedbackLight.enabled = false;

        var particleObject = new GameObject("Stamp Celebration Particles");
        particleObject.transform.SetParent(transform, false);
        particleObject.transform.localPosition = new Vector3(0f, 0.02f, -0.03f);
        feedbackParticles = particleObject.AddComponent<ParticleSystem>();
        feedbackParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = feedbackParticles.main;
        main.loop = false;
        main.duration = 0.8f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.95f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.18f, 0.48f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.032f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.55f, 0.05f, 1f), new Color(1f, 0.95f, 0.35f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        var emission = feedbackParticles.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });
        var shape = feedbackParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.16f;
        shape.radiusThickness = 1f;
        var particleRenderer = feedbackParticles.GetComponent<ParticleSystemRenderer>();
        particleRenderer.material = new Material(Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default"));
        feedbackParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void BuildTemporaryUi()
    {
        var canvasObject = new GameObject(
            "Projected Main Menu",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvasObject.transform.localPosition = new Vector3(0f, 0.36f, 0.025f);
        canvasObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        canvasObject.transform.localScale = UiCollapsedScale;
        uiCanvas = canvasObject.GetComponent<Canvas>();
        uiCanvas.renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)canvasObject.transform;
        rect.sizeDelta = new Vector2(580f, 370f);

        var panelObject = new GameObject(
            "Holographic Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var panelRect = (RectTransform)panelObject.transform;
        panelRect.SetParent(rect, false);
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        var panelImage = panelObject.GetComponent<Image>();
        panelImage.color = new Color(0.025f, 0.09f, 0.16f, 0.9f);
        panelImage.raycastTarget = false;

        if (ApplyOptionalSprite(panelImage, "AdmissionLetterUI/menu_background"))
            panelImage.color = Color.white;

        BuildMainPage(rect);
        BuildGuidePage(rect);
        BuildSuggestionPage(rect);
        BuildMapPage(rect);
        BuildCollectionPage(rect);
        BuildExperienceReportPage(rect);
        BuildTasksPage(rect);
        BuildLocationPage(rect);
        BuildScanPage(rect);
        BuildInterestPage(rect);
        BuildIdentityPage(rect);
        BuildTimetablePage(rect);
        BuildExhibitPage(rect);
        BuildMemoryPage(rect);

        thinkingIndicatorText = CreateText(rect, "✦", 28,
            new Vector2(-342f, 0f), new Vector2(42f, 42f), TextAnchor.MiddleCenter);
        thinkingIndicatorText.color = new Color(0.35f, 0.95f, 1f, 1f);
        thinkingIndicatorText.gameObject.SetActive(false);

        hintText = CreateText(rect, "右手射线选择 · 右手食指扳机键确认 · 左手侧边握持键关闭", 15,
            new Vector2(0f, -151f), new Vector2(480f, 24f), TextAnchor.MiddleCenter);
        hintText.color = new Color(0.45f, 0.82f, 1f, 1f);

        BuildProjectionBeam();
        ShowPage("main");
    }

    private void BuildMainPage(RectTransform root)
    {
        var page = CreatePage(root, "main", "Main Page");
        CreatePageTitle(page, "录取通知书");

        CreateMenuButton(page, "🏠 校园地图", "page_map", new Vector2(-122f, 58f),
            new Vector2(218f, 62f), "AdmissionLetterUI/button_map");
        CreateMenuButton(page, "🤖 AI 校园向导", "page_guide", new Vector2(122f, 58f),
            new Vector2(218f, 62f), "AdmissionLetterUI/button_ai_guide");
        CreateMenuButton(page, "📜 探索图鉴", "page_collection", new Vector2(-122f, -19f),
            new Vector2(218f, 62f), "AdmissionLetterUI/button_collection");
        CreateMenuButton(page, "📋 任务中心", "page_tasks", new Vector2(122f, -19f),
            new Vector2(218f, 62f), "AdmissionLetterUI/button_tasks");
        CreateMenuButton(page, "👤 我的身份", "page_identity", new Vector2(-166f, -91f),
            new Vector2(145f, 38f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "🔍 AI 空间扫描", "ai_spatial_scan", new Vector2(0f, -91f),
            new Vector2(145f, 38f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "🎓 新手教程", "page_tutorial", new Vector2(166f, -91f),
            new Vector2(145f, 38f), "AdmissionLetterUI/button_default");
    }

    private Text identityStatusText;

    private void BuildIdentityPage(RectTransform root)
    {
        var page = CreatePage(root, "identity", "Identity Page");
        CreatePageTitle(page, "我的身份");
        var instructions = CreateText(page,
            "1. 选择下方“录入姓名和专业”。\n2. 按住右手侧边握持键，说“我叫小明，专业是人工智能”。\n3. 说完松开按键，等待文字提示。",
            17, new Vector2(0f, 62f), new Vector2(455f, 92f), TextAnchor.UpperLeft);
        instructions.color = new Color(0.72f, 0.9f, 1f, 1f);
        identityStatusText = CreateText(page, "尚未录入身份", 18,
            new Vector2(0f, -20f), new Vector2(455f, 42f), TextAnchor.MiddleCenter);
        CreateMenuButton(page, "语音录入", "identity_listen", new Vector2(-145f, -76f),
            new Vector2(145f, 38f), "AdmissionLetterUI/button_ai_guide");
        CreateMenuButton(page, "一键体验", "identity_quick_fill", new Vector2(0f, -76f),
            new Vector2(125f, 38f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "打开校园地图", "page_map", new Vector2(145f, -76f),
            new Vector2(135f, 38f), "AdmissionLetterUI/button_default");
        CreateBackButton(page);
        RefreshIdentityPage();
    }

    private void RefreshIdentityPage()
    {
        if (identityStatusText == null) return;
        var profile = CampusUserProfileService.Instance;
        identityStatusText.text = profile != null && profile.HasIdentity
            ? "你好，" + profile.StudentName + "同学 · " + profile.StudentMajor + "专业"
            : "尚未录入身份；完成后 AI 会在后续讲解中称呼你";
    }

    private void BuildGuidePage(RectTransform root)
    {
        var page = CreatePage(root, "guide", "AI Guide Page");
        CreatePageTitle(page, "AI 校园向导");
        var intro = CreateText(page, "按住右手侧边握持键说话，松开后发送。AI 将结合当前场景和最近注视建筑回答。", 18,
            new Vector2(0f, 74f), new Vector2(455f, 55f), TextAnchor.UpperLeft);
        intro.color = new Color(0.65f, 0.9f, 1f, 1f);
        answerText = CreateText(page, "可通过语音或快捷问题查询校园知识库。", 18,
            new Vector2(0f, -7f), new Vector2(455f, 120f), TextAnchor.UpperLeft);
        answerText.color = new Color(0.9f, 0.96f, 1f, 1f);
        CreateMenuButton(page, "上一页", "answer_previous", new Vector2(-166f, -91f),
            new Vector2(90f, 30f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "猜你想问", "page_suggestions", new Vector2(0f, -91f),
            new Vector2(150f, 30f), "AdmissionLetterUI/button_ai_guide");
        CreateMenuButton(page, "下一页", "answer_next", new Vector2(166f, -91f),
            new Vector2(90f, 30f), "AdmissionLetterUI/button_default");
        SetAnswerText(answerText.text);
        CreateBackButton(page);
    }

    private void BuildSuggestionPage(RectTransform root)
    {
        var page = CreatePage(root, "suggestions", "AI Suggested Questions Page");
        CreatePageTitle(page, "AI 猜你想问");
        var tip = CreateText(page, "不用想怎么提问，选择一个你关心的方向即可。", 16,
            new Vector2(0f, 91f), new Vector2(450f, 28f), TextAnchor.MiddleCenter);
        tip.color = new Color(0.65f, 0.9f, 1f, 1f);
        CreateMenuButton(page, "这里有什么？", "ask_here", new Vector2(-120f, 38f),
            new Vector2(210f, 44f), "AdmissionLetterUI/button_ai_guide");
        CreateMenuButton(page, "新生先做什么？", "ask_new_student", new Vector2(120f, 38f),
            new Vector2(210f, 44f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "下一步怎么走？", "ask_next_step", new Vector2(-120f, -18f),
            new Vector2(210f, 44f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "按兴趣推荐", "ask_interest", new Vector2(120f, -18f),
            new Vector2(210f, 44f), "AdmissionLetterUI/button_default");
        CreateBackButton(page);
    }

    private void SetAnswerText(string value)
    {
        answerPages.Clear();
        answerPageIndex = 0;
        var remaining = string.IsNullOrWhiteSpace(value) ? "暂时没有可显示的回答。" : value.Trim();
        while (remaining.Length > AnswerPageCharacterLimit)
        {
            var split = FindAnswerPageBreak(remaining, AnswerPageCharacterLimit);
            answerPages.Add(remaining.Substring(0, split).Trim());
            remaining = remaining.Substring(split).TrimStart();
        }
        answerPages.Add(remaining);
        ShowAnswerPage(0);
    }

    private static int FindAnswerPageBreak(string value, int limit)
    {
        var minimum = Mathf.Max(1, limit / 2);
        for (var index = Mathf.Min(limit, value.Length - 1); index >= minimum; index--)
        {
            var character = value[index];
            if (character == '\n' || character == '。' || character == '！' ||
                character == '？' || character == '；')
                return index + 1;
        }
        return Mathf.Min(limit, value.Length);
    }

    private void ShowAnswerPage(int pageIndex)
    {
        if (answerText == null || answerPages.Count == 0)
            return;
        answerPageIndex = Mathf.Clamp(pageIndex, 0, answerPages.Count - 1);
        var prefix = answerPages.Count > 1
            ? $"【{answerPageIndex + 1}/{answerPages.Count}】\n"
            : string.Empty;
        answerText.text = prefix + answerPages[answerPageIndex];
    }

    private void BuildMapPage(RectTransform root)
    {
        var page = CreatePage(root, "map", "Campus Map Page");
        CreatePageTitle(page, "校园地图");
        CreateMenuButton(page, "东吴门", "route_dongwu_gate", new Vector2(-120f, 52f),
            new Vector2(210f, 40f), "AdmissionLetterUI/button_scene_main");
        CreateMenuButton(page, "科技连廊", "route_innovation_corridor", new Vector2(120f, 52f),
            new Vector2(210f, 40f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "AI 展厅", "route_exhibition", new Vector2(-120f, 2f),
            new Vector2(210f, 40f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "智慧教室", "route_classroom", new Vector2(120f, 2f),
            new Vector2(210f, 40f), "AdmissionLetterUI/button_scene_classroom");
        CreateMenuButton(page, "AI 生成式展项", "page_exhibit", new Vector2(-118f, -62f),
            new Vector2(190f, 36f), "AdmissionLetterUI/button_ai_guide");
        CreateMenuButton(page, "推荐游览顺序", "route_recommended", new Vector2(118f, -62f),
            new Vector2(190f, 36f), "AdmissionLetterUI/button_default");
        CreateBackButton(page);
    }

    private void BuildCollectionPage(RectTransform root)
    {
        var page = CreatePage(root, "collection", "Collection Page");
        CreatePageTitle(page, "探索图鉴");
        collectionStatusText = CreateText(page, "探索记录正在读取……",
            17, new Vector2(0f, 13f), new Vector2(420f, 145f), TextAnchor.MiddleLeft);
        collectionStatusText.color = new Color(0.85f, 0.94f, 1f, 1f);
        RefreshCollectionPage();
        CreateMenuButton(page, "打开“我的第一日”", "page_experience_report", new Vector2(-65f, -91f),
            new Vector2(220f, 34f), "AdmissionLetterUI/button_ai_guide");
        CreateBackButton(page);
    }

    private void BuildExperienceReportPage(RectTransform root)
    {
        var page = CreatePage(root, "report", "Experience Report Page");
        CreatePageTitle(page, "我的第一日 · 新生纪念页");
        experienceReportText = CreateText(page, "正在整理本次探索记录……", 17,
            new Vector2(0f, 18f), new Vector2(445f, 175f), TextAnchor.UpperLeft);
        experienceReportText.color = new Color(0.82f, 0.95f, 1f, 1f);
        CreateMenuButton(page, "AI 写专属寄语", "report_ask_ai", new Vector2(-118f, -91f),
            new Vector2(190f, 34f), "AdmissionLetterUI/button_ai_guide");
        CreateMenuButton(page, "东吴门纪念照", "dongwu_photo", new Vector2(118f, -91f),
            new Vector2(210f, 34f), "AdmissionLetterUI/button_default");
        CreateBackButton(page);
        RefreshExperienceReport();
    }

    private void RefreshExperienceReport()
    {
        if (experienceReportText == null)
            return;
        var reports = CampusExperienceReportService.Instance;
        experienceReportText.text = reports != null
            ? reports.BuildLocalReport()
            : "探索报告服务正在初始化，请稍后重新打开。";
    }

    private void RefreshCollectionPage()
    {
        if (collectionStatusText == null)
            return;
        var progress = CampusProgressService.Instance;
        if (progress == null)
        {
            collectionStatusText.text = "探索记录尚未初始化。";
            return;
        }

        var main = false;
        var classroom = false;
        var exhibition = false;
        foreach (var location in progress.Data.locations)
        {
            if (!location.stampAwarded) continue;
            if (location.locationId.StartsWith("classroom_", StringComparison.OrdinalIgnoreCase)) classroom = true;
            else if (location.locationId.StartsWith("exhibition_", StringComparison.OrdinalIgnoreCase)) exhibition = true;
            else main = true;
        }

        collectionStatusText.text =
            "主校园印章    " + (main ? "◆" : "□") + "\n" +
            "教室探索印章  " + (classroom ? "◆" : "□") + "\n" +
            "展厅探索印章  " + (exhibition ? "◆" : "□") + "\n\n" +
            "隐藏记录：" + progress.Data.unlockedSecretIds.Count + "/3\n" +
            (progress.HasSecret(CampusSecretService.PatientObserver) ? "◆ 细心观察者  " : "◇ ?????  ") +
            (progress.HasSecret(CampusSecretService.CampusExplorer) ? "◆ 校园探索家\n" : "◇ ?????\n") +
            (progress.HasSecret(CampusSecretService.SmartLearningPath) ? "◆ 智慧学习路线" : "◇ ?????");
    }

    private void BuildTasksPage(RectTransform root)
    {
        var page = CreatePage(root, "tasks", "Tasks Page");
        CreatePageTitle(page, "推荐旅程 · 10～15 分钟");
        journeyStatusText = CreateText(page, "推荐主线正在初始化……",
            17, new Vector2(0f, 28f), new Vector2(430f, 145f), TextAnchor.UpperLeft);
        journeyStatusText.color = new Color(0.85f, 0.94f, 1f, 1f);
        CreateMenuButton(page, "开启 / 暂停推荐", "toggle_journey", new Vector2(-145f, -122f),
            new Vector2(155f, 38f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "重看新手引导", "restart_tutorial", new Vector2(20f, -122f),
            new Vector2(155f, 38f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "AI 记忆与隐私", "page_memory", new Vector2(-65f, -79f),
            new Vector2(130f, 34f), "AdmissionLetterUI/button_default");
        CreateBackButton(page);
    }

    private void BuildLocationPage(RectTransform root)
    {
        var page = CreatePage(root, "location", "Location Information Page");
        var card = new GameObject("Location Information Card", typeof(RectTransform), typeof(Image));
        var cardRect = (RectTransform)card.transform;
        cardRect.SetParent(page, false);
        cardRect.anchoredPosition = new Vector2(0f, 25f);
        cardRect.sizeDelta = new Vector2(456f, 224f);
        var cardImage = card.GetComponent<Image>();
        cardImage.raycastTarget = false;
        cardImage.color = ApplyOptionalSprite(cardImage, "AdmissionLetterUI/gaze_card")
            ? Color.white : new Color(0.025f, 0.12f, 0.2f, 0.94f);
        card.transform.SetAsFirstSibling();

        locationTitleText = CreateText(page, "地点介绍", 27,
            new Vector2(0f, 126f), new Vector2(430f, 42f), TextAnchor.MiddleCenter);
        locationTitleText.color = new Color(0.95f, 0.78f, 0.28f, 1f);
        locationDescriptionText = CreateText(page, "注视建筑或使用 AI 扫描后显示介绍。", 18,
            new Vector2(0f, 22f), new Vector2(400f, 128f), TextAnchor.UpperLeft);
        locationDescriptionText.color = new Color(0.88f, 0.95f, 1f, 1f);
        CreateMenuButton(page, "询问 AI", "location_ask_ai", new Vector2(-115f, -72f),
            new Vector2(190f, 38f), "AdmissionLetterUI/button_ai_guide");
        CreateMenuButton(page, "导航到这里", "location_navigate", new Vector2(115f, -72f),
            new Vector2(190f, 38f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "展开空间知识图谱", "location_knowledge_graph", new Vector2(-58f, -115f),
            new Vector2(250f, 34f), "AdmissionLetterUI/button_default");
        CreateBackButton(page);
    }

    private void BuildScanPage(RectTransform root)
    {
        var page = CreatePage(root, "scan", "AI Spatial Scan Page");
        scanPage = page;
        CreatePageTitle(page, "AI 空间感知 · 扫描结果");
        scanResultsText = CreateText(page, "点击主菜单中的 AI 空间扫描开始识别。", 18,
            new Vector2(0f, 76f), new Vector2(440f, 34f), TextAnchor.MiddleCenter);
        scanResultsText.color = new Color(0.45f, 0.95f, 1f, 1f);
        CreateBackButton(page);
    }

    private void BuildInterestPage(RectTransform root)
    {
        var page = CreatePage(root, "interest", "Interest Selection Page");
        CreatePageTitle(page, "选择你的兴趣方向");
        var intro = CreateText(page, "当前为迎新测试选项，后期替换为学校正式专业与方向。", 17,
            new Vector2(0f, 92f), new Vector2(450f, 34f), TextAnchor.MiddleCenter);
        intro.color = new Color(0.65f, 0.9f, 1f, 1f);
        CreateMenuButton(page, "数据科学与大数据技术", "interest_data_science", new Vector2(-120f, 35f),
            new Vector2(210f, 48f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "人工智能", "interest_ai", new Vector2(120f, 35f),
            new Vector2(210f, 48f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "机器人工程", "interest_robotics", new Vector2(-120f, -25f),
            new Vector2(210f, 48f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "机电工程", "interest_mechatronic", new Vector2(120f, -25f),
            new Vector2(210f, 48f), "AdmissionLetterUI/button_default");
        CreateBackButton(page);
    }

    private void BuildTimetablePage(RectTransform root)
    {
        var page = CreatePage(root, "timetable", "Future Timetable Page");
        timetableTitleText = CreateText(page, "未来课表", 27,
            new Vector2(0f, 128f), new Vector2(460f, 40f), TextAnchor.MiddleCenter);
        timetableTitleText.color = new Color(0.95f, 0.78f, 0.28f, 1f);
        timetableBodyText = CreateText(page, "请先选择你感兴趣的方向。", 19,
            new Vector2(0f, 15f), new Vector2(420f, 180f), TextAnchor.UpperLeft);
        timetableBodyText.color = new Color(0.86f, 0.95f, 1f, 1f);
        CreateMenuButton(page, "重新选择", "change_interest", new Vector2(-165f, -122f),
            new Vector2(110f, 38f), "AdmissionLetterUI/button_default");
        CreateBackButton(page);
    }

    private void BuildExhibitPage(RectTransform root)
    {
        var page = CreatePage(root, "exhibit", "Generative Exhibit Page");
        CreatePageTitle(page, "AI 生成式展项");
        var tip = CreateText(page, "用右手射线选择主题，再按右手食指扳机键一次。", 16,
            new Vector2(0f, 92f), new Vector2(450f, 28f), TextAnchor.MiddleCenter);
        tip.color = new Color(0.55f, 0.9f, 1f, 1f);
        CreateMenuButton(page, "AI 发展", "exhibit_history", new Vector2(-150f, 50f),
            new Vector2(135f, 36f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "智慧校园", "exhibit_campus", new Vector2(0f, 50f),
            new Vector2(135f, 36f), "AdmissionLetterUI/button_default");
        CreateMenuButton(page, "未来课堂", "exhibit_classroom", new Vector2(150f, 50f),
            new Vector2(135f, 36f), "AdmissionLetterUI/button_default");
        exhibitContentText = CreateText(page, "尚未生成展项。", 17,
            new Vector2(0f, -18f), new Vector2(440f, 90f), TextAnchor.UpperLeft);
        CreateMenuButton(page, "让 AI 扩展讲解", "exhibit_ask_ai", new Vector2(-65f, -91f),
            new Vector2(210f, 34f), "AdmissionLetterUI/button_ai_guide");
        CreateBackButton(page);
        RefreshExhibitPage();
    }

    private void RefreshExhibitPage()
    {
        if (exhibitContentText == null) return;
        var exhibit = CampusGenerativeExhibitService.Instance;
        exhibitContentText.text = exhibit != null
            ? "主题：" + exhibit.CurrentTheme + "\n" + exhibit.CurrentContent
            : "生成式展项服务尚未初始化。";
    }

    private void BuildMemoryPage(RectTransform root)
    {
        var page = CreatePage(root, "memory", "AI Memory Page");
        CreatePageTitle(page, "AI 记忆与隐私");
        memoryStatusText = CreateText(page, "正在读取本机记录……", 17,
            new Vector2(0f, 15f), new Vector2(440f, 190f), TextAnchor.UpperLeft);
        CreateMenuButton(page, "清除全部本机记忆", "memory_clear_request", new Vector2(-65f, -101f),
            new Vector2(220f, 36f), "AdmissionLetterUI/button_default");
        CreateBackButton(page);
        RefreshMemoryPage();
    }

    private void RefreshMemoryPage()
    {
        if (memoryStatusText != null)
            memoryStatusText.text = CampusMemoryService.Instance != null
                ? CampusMemoryService.Instance.BuildSummary() : "AI 记忆服务尚未初始化。";
    }


    private RectTransform CreatePage(RectTransform parent, string id, string objectName)
    {
        var pageObject = new GameObject(objectName, typeof(RectTransform));
        var page = (RectTransform)pageObject.transform;
        page.SetParent(parent, false);
        page.anchorMin = Vector2.zero;
        page.anchorMax = Vector2.one;
        page.offsetMin = Vector2.zero;
        page.offsetMax = Vector2.zero;
        menuPages[id] = pageObject;
        var bottom = new GameObject("Bottom Technology Decoration", typeof(RectTransform), typeof(Image));
        var bottomRect = (RectTransform)bottom.transform;
        bottomRect.SetParent(page, false);
        bottomRect.anchoredPosition = new Vector2(0f, -151f);
        bottomRect.sizeDelta = new Vector2(470f, 12f);
        var bottomImage = bottom.GetComponent<Image>();
        bottomImage.raycastTarget = false;
        bottomImage.color = AdmissionLetterGuide.ApplyOptionalSprite(
            bottomImage, "AdmissionLetterUI/bottom_decoration") ? Color.white : Color.clear;
        return page;
    }

    private void CreatePageTitle(RectTransform page, string value)
    {
        var decoration = new GameObject("Title Technology Decoration", typeof(RectTransform), typeof(Image));
        var decorationRect = (RectTransform)decoration.transform;
        decorationRect.SetParent(page, false);
        decorationRect.anchoredPosition = new Vector2(0f, 130f);
        decorationRect.sizeDelta = new Vector2(470f, 42f);
        var decorationImage = decoration.GetComponent<Image>();
        decorationImage.raycastTarget = false;
        decorationImage.color = ApplyOptionalSprite(decorationImage,
            "AdmissionLetterUI/title_decoration") ? Color.white : Color.clear;
        var text = CreateText(page, value, 27, new Vector2(0f, 130f),
            new Vector2(460f, 40f), TextAnchor.MiddleCenter);
        text.color = Color.white;
    }

    private void CreateBackButton(RectTransform page)
    {
        CreateMenuButton(page, "返回", "back_main", new Vector2(190f, -122f),
            new Vector2(95f, 38f), "AdmissionLetterUI/button_back");
    }

    private AdmissionLetterMenuButton CreateMenuButton(
        RectTransform parent, string label, string actionId, Vector2 position,
        Vector2 size, string spritePath)
    {
        var buttonObject = new GameObject(
            label + " Button", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(BoxCollider), typeof(AdmissionLetterMenuButton));
        var rect = (RectTransform)buttonObject.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        var image = buttonObject.GetComponent<Image>();
        var baseColor = new Color(0.08f, 0.31f, 0.48f, 0.96f);
        image.raycastTarget = false;
        var hasSprite = ApplyOptionalSprite(image, spritePath) ||
                        ApplyOptionalSprite(image, "AdmissionLetterUI/button_default");
        if (hasSprite)
            baseColor = Color.white;
        image.color = baseColor;

        var collider = buttonObject.GetComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(size.x, size.y, 14f);

        var labelText = CreateText(rect, label, 20, Vector2.zero, size, TextAnchor.MiddleCenter);
        labelText.color = Color.white;
        labelText.raycastTarget = false;

        var button = buttonObject.GetComponent<AdmissionLetterMenuButton>();
        button.Configure(this, actionId, image, baseColor);
        return button;
    }

    public static bool ApplyOptionalSprite(Image image, string resourcePath)
    {
        var sprite = Resources.Load<Sprite>(resourcePath);
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            return true;
        }
        var texture = Resources.Load<Texture2D>(resourcePath);
        if (texture == null) return false;
        image.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f);
        image.type = Image.Type.Simple;
        return true;
    }

    private void BuildProjectionBeam()
    {
        projectionBeam = new GameObject("Menu Projection Beam");
        projectionBeam.transform.SetParent(transform, false);
        // Keep the compatibility object because the open/close animation toggles it,
        // but intentionally render nothing. The old blue trapezoid distracted from
        // the two physical pages of the admission letter.
        projectionBeam.SetActive(false);
    }

    private static Text CreateText(RectTransform parent, string value, int size, Vector2 position, Vector2 dimensions, TextAnchor anchor)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        var text = go.GetComponent<Text>();
        text.font = LoadRuntimeFont();
        text.text = value;
        text.fontSize = size;
        text.color = new Color(0.86f, 0.96f, 1f, 1f);
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.resizeTextForBestFit = true;
        text.resizeTextMinSize = Mathf.Max(12, size - 6);
        text.resizeTextMaxSize = size;
        return text;
    }

    private static Font LoadRuntimeFont()
    {
        // Unity 2022.2+ renamed the built-in Arial resource. Requesting
        // Arial.ttf throws during Awake and leaves the letter at world origin.
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null)
            return font;

        return Font.CreateDynamicFontFromOSFont(
            new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
    }

    private static Material CreateMaterial(string name, Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        return material;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
            return;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    private static string GetOrCreateUserId()
    {
        const string key = "Campus.Dify.UserId";
        var id = PlayerPrefs.GetString(key, string.Empty);
        if (!string.IsNullOrEmpty(id))
            return id;
        id = System.Guid.NewGuid().ToString("N");
        PlayerPrefs.SetString(key, id);
        PlayerPrefs.Save();
        return id;
    }
}
