using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;

public sealed class CampusTutorialService : MonoBehaviour
{
    public static CampusTutorialService Instance { get; private set; }
    public event Action<string> InstructionChanged;
    public string CurrentInstruction { get; private set; }

    private GameObject tutorialPanel;
    private RectTransform tutorialRect;
    private Canvas fallbackCanvas;
    private bool attachedToLetter;
    private Text tutorialText;
    private Text interruptionText;
    private Coroutine completionRoutine;
    private string baseInstruction = string.Empty;
    private string currentStepId = string.Empty;
    private float stepStartedTime;
    private float nextRefreshTime;
    private int reminderLevel;
    private readonly List<string> instructionHistory = new List<string>();
    private readonly List<string> instructionHistoryIds = new List<string>();
    private int historyIndex = -1;
    
    private InputAction rightPositionAction;
    private InputAction rightRotationAction;
    private InputAction rightTriggerAction;
    private InputAction leftStickAction;
    private Transform trackingOrigin;
    private bool triggerWasPressed;
    private AdmissionLetterMenuButton hoveredButton;
    private Vector3 lastHeadPosition;
    private bool moveTaskReported;

    private readonly Dictionary<string, float> stepCompletedTimestamps = new Dictionary<string, float>();
    private const float StepCompletionDelay = 0.5f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (CampusTaskService.Instance != null) CampusTaskService.Instance.TaskCompleted += OnTaskCompleted;
        if (CampusProgressService.Instance != null) CampusProgressService.Instance.ProgressChanged += RefreshInstruction;
        if (CampusUserProfileService.Instance != null) CampusUserProfileService.Instance.ProfileChanged += RefreshInstruction;
        if (CampusInteractionCoordinator.Instance != null) CampusInteractionCoordinator.Instance.ModeChanged += OnModeChanged;
        StartCoroutine(BuildTutorialPanelWhenReady());
        RefreshInstruction();
        InitializeInputActions();
    }

    private void OnDestroy()
    {
        if (CampusTaskService.Instance != null) CampusTaskService.Instance.TaskCompleted -= OnTaskCompleted;
        if (CampusProgressService.Instance != null) CampusProgressService.Instance.ProgressChanged -= RefreshInstruction;
        if (CampusUserProfileService.Instance != null) CampusUserProfileService.Instance.ProfileChanged -= RefreshInstruction;
        if (CampusInteractionCoordinator.Instance != null) CampusInteractionCoordinator.Instance.ModeChanged -= OnModeChanged;
        if (Instance == this) Instance = null;
        rightPositionAction?.Dispose();
        rightRotationAction?.Dispose();
        rightTriggerAction?.Dispose();
    }

    private void Update()
    {
        UpdatePanelPosition();
        UpdateInterruptionStatus();
        UpdateTutorialRayInteraction();
        DetectMovement();
        if (Time.unscaledTime >= nextRefreshTime)
        {
            nextRefreshTime = Time.unscaledTime + 0.5f;
            RefreshInstruction();
        }
        var progress = CampusProgressService.Instance;
        if (progress == null || progress.Data.tutorialCompleted || string.IsNullOrWhiteSpace(baseInstruction)) return;
        var idle = Time.unscaledTime - stepStartedTime;
        if (idle >= 40f && reminderLevel < 2)
        {
            reminderLevel = 2;
            SetInstruction(baseInstruction + "\n\n仍然没找到？先不要走动，只做本页第 ① 项。通知书菜单里的“任务中心”也会显示当前目标。");
            CampusFeedbackService.Instance?.Confirm();
        }
        else if (idle >= 22f && reminderLevel < 1)
        {
            reminderLevel = 1;
            SetInstruction(baseInstruction + "\n\n慢慢来：移动右手时只转动手腕，按钮变亮后再按右手食指扳机键。");
            CampusFeedbackService.Instance?.Hover();
        }
    }

    private void DetectMovement()
    {
        if (moveTaskReported) return;
        var head = Camera.main?.transform;
        if (head == null) return;
        if (lastHeadPosition == Vector3.zero)
        {
            lastHeadPosition = head.position;
            return;
        }
        var distance = Vector3.Distance(lastHeadPosition, head.position);
        if (distance > 0.3f)
        {
            moveTaskReported = true;
            CampusTaskService.Instance?.Report(CampusTaskTrigger.Move);
        }
        lastHeadPosition = head.position;
    }

    public void RestartTutorial()
    {
        if (completionRoutine != null) { StopCoroutine(completionRoutine); completionRoutine = null; }
        currentStepId = string.Empty;
        stepCompletedTimestamps.Clear();
        moveTaskReported = false;
        CampusProgressService.Instance?.ResetTutorialProgress();
        RefreshInstruction();
    }

    private void OnTaskCompleted(CampusTaskDefinition task)
    {
        CampusFeedbackService.Instance?.TaskComplete();
        RefreshInstruction();
    }

    private void OnModeChanged(CampusInteractionMode previous, CampusInteractionMode next)
    {
        UpdateInterruptionStatus();
        if (!IsFlowInterrupted(next)) RefreshInstruction();
    }

    private static bool IsFlowInterrupted(CampusInteractionMode mode)
    {
        return mode == CampusInteractionMode.Listening || mode == CampusInteractionMode.Thinking ||
               mode == CampusInteractionMode.Speaking || mode == CampusInteractionMode.Loading;
    }

    private void UpdateInterruptionStatus()
    {
        if (interruptionText == null) return;
        var coordinator = CampusInteractionCoordinator.Instance;
        var interrupted = coordinator != null && IsFlowInterrupted(coordinator.Mode);
        interruptionText.gameObject.SetActive(interrupted);
        if (!interrupted) return;
        var action = coordinator.Mode == CampusInteractionMode.Loading ? "正在切换场景" :
                     coordinator.Mode == CampusInteractionMode.Listening ? "正在听你说话" :
                     coordinator.Mode == CampusInteractionMode.Thinking ? "AI 正在思考" : "AI 正在回答";
        interruptionText.text = "教程暂时停在当前步骤 · " + action +
                                "\n完成后自动从这里继续，不需要重新开始";
    }

    private bool IsStepReadyToAdvance(string taskId)
    {
        var progress = CampusProgressService.Instance;
        if (progress == null || !progress.IsTaskCompleted(taskId))
            return false;
        if (!stepCompletedTimestamps.TryGetValue(taskId, out var timestamp))
        {
            stepCompletedTimestamps[taskId] = Time.unscaledTime;
            return false;
        }
        return Time.unscaledTime - timestamp >= StepCompletionDelay;
    }

    private void RefreshInstruction()
    {
        var progress = CampusProgressService.Instance;
        if (progress == null) return;
        var coordinator = CampusInteractionCoordinator.Instance;
        if (!string.IsNullOrEmpty(currentStepId) && coordinator != null && IsFlowInterrupted(coordinator.Mode))
            return;
        if (progress.Data.tutorialCompleted)
        {
            if (currentStepId != "complete") SetInstruction(string.Empty);
            return;
        }
        var profile = CampusUserProfileService.Instance;

        if (!IsStepReadyToAdvance("tutorial_attach_letter"))
            SetStep("attach", "操作熟悉 1/10 · 拿起通知书\n做什么：左手射线对准红色通知书，按一次左手食指扳机键。\n达成条件：通知书吸附在左手上。");
        else if (!IsStepReadyToAdvance("tutorial_open_letter"))
            SetStep("open", "操作熟悉 2/10 · 打开通知书\n做什么：按一次左手侧边握持键。\n达成条件：通知书展开并显示菜单。");
        else if (!IsStepReadyToAdvance("tutorial_identity_recorded"))
            SetStep("identity", "操作熟悉 3/10 · 录入身份\n做什么：选择“我的身份”，按“语音录入”说出姓名和专业，或选择“一键体验”。\n达成条件：页面显示姓名和专业。");
        else if (!IsStepReadyToAdvance("tutorial_move"))
            SetStep("move", "操作熟悉 4/10 · 移动与转向\n做什么：关闭通知书；左手摇杆向前移动，右手摇杆向左或右转向。\n达成条件：头显位置移动超过 0.3 米。");
        else if (!IsStepReadyToAdvance("tutorial_open_map"))
            SetStep("map", "操作熟悉 5/10 · 打开地图\n做什么：右手射线对准“校园地图”，按右手食指扳机键。\n达成条件：显示校园地图页面。");
        else if (!IsStepReadyToAdvance("tutorial_start_navigation"))
            SetStep("navigation", "操作熟悉 6/10 · 前往连廊\n做什么：在地图选择“科技连廊”。\n达成条件：地面出现金色导航路线。");
        else if (!IsLocationCompleted("main_innovation_corridor"))
            SetStep("corridor", "游览 7/10 · 连廊展板\n做什么：沿金色路线到连廊，拿起一块展板观看。\n达成条件：连廊印章获得，并出现前往展厅的导航。");
        else if (!IsStepReadyToAdvance("tutorial_visit_exhibition"))
            SetStep("exhibition", "游览 8/10 · 科技展厅\n做什么：跟随导航进入 AI 展厅。\n达成条件：成功进入科技展厅场景。");
        else if (!IsStepReadyToAdvance("tutorial_visit_classroom"))
            SetStep("classroom", "游览 9/10 · 智慧教室\n做什么：打开地图，选择“智慧教室”，进入传送门。\n达成条件：成功进入智慧教室场景，并在讲台看到老师。");
        else if (!IsStepReadyToAdvance("tutorial_listen_class"))
            SetStep("lesson", "游览 10/10 · 开始听课\n做什么：黑板已显示课件首页。按一次左手控制器顶部的 X 键开始讲课；需要停止时按 X 键旁边的 Y 键。\n达成条件：老师开始播放课程讲解，课件随语音进度同步切换。");
        else
        {
            progress.SetTutorialCompleted(true);
            currentStepId = "complete";
            SetInstruction("游览完成\n达成条件：已完成操作熟悉、连廊、科技展厅和智慧教室。");
            if (completionRoutine != null) StopCoroutine(completionRoutine);
            completionRoutine = StartCoroutine(HideCompletionMessage());
        }
    }

    private static bool IsLocationCompleted(string locationId)
    {
        return CampusProgressService.Instance != null &&
               CampusProgressService.Instance.GetLocationState(locationId) == CampusDiscoveryState.Completed;
    }

    private void SetStep(string id, string value)
    {
        if (currentStepId == id) return;
        currentStepId = id;
        baseInstruction = value;
        stepStartedTime = Time.unscaledTime;
        reminderLevel = 0;
        if (!instructionHistoryIds.Contains(id))
        {
            instructionHistoryIds.Add(id);
            instructionHistory.Add(value);
        }
        historyIndex = instructionHistory.Count - 1;
        SetInstruction(value);
        if (id == "scan")
            StartCoroutine(AutoRunSpatialScan());
    }

    private IEnumerator AutoRunSpatialScan()
    {
        // Render the seventh-step instruction first, then run detection. This
        // prevents the task completion callback from skipping the visible step.
        yield return new WaitForSecondsRealtime(0.75f);
        AdmissionLetterGuide.Instance?.RunTutorialSpatialScan();
    }

    public void ShowTutorialPage()
    {
        if (instructionHistory.Count == 0 && !string.IsNullOrWhiteSpace(baseInstruction))
        {
            instructionHistoryIds.Add(currentStepId);
            instructionHistory.Add(baseInstruction);
        }
        historyIndex = Mathf.Max(0, instructionHistory.Count - 1);
        ShowHistoryAt(historyIndex);
    }

    public void ShowPreviousInstruction()
    {
        if (instructionHistory.Count == 0) return;
        historyIndex = Mathf.Max(0, historyIndex - 1);
        ShowHistoryAt(historyIndex);
    }

    public void ShowNextInstruction()
    {
        if (instructionHistory.Count == 0) return;
        historyIndex = Mathf.Min(instructionHistory.Count - 1, historyIndex + 1);
        ShowHistoryAt(historyIndex);
    }

    private void ShowHistoryAt(int index)
    {
        if (index < 0 || index >= instructionHistory.Count) return;
        var navigation = "\n\n历史教学 " + (index + 1) + "/" + instructionHistory.Count +
                         " · 使用上方“上一页 / 下一页”查看";
        SetInstruction(instructionHistory[index] + navigation);
    }

    private void SetInstruction(string value)
    {
        var changed = CurrentInstruction != value;
        CurrentInstruction = value;
        if (tutorialPanel != null) tutorialPanel.SetActive(!string.IsNullOrWhiteSpace(value));
        if (tutorialText != null) tutorialText.text = value;
        if (changed) InstructionChanged?.Invoke(value);
    }

    private IEnumerator BuildTutorialPanelWhenReady()
    {
        while (Camera.main == null || AdmissionLetterGuide.Instance == null ||
               AdmissionLetterGuide.Instance.MenuUiRoot == null)
            yield return null;

        var fallback = new GameObject("Tutorial View Anchor", typeof(RectTransform), typeof(Canvas));
        fallback.transform.SetParent(Camera.main.transform, false);
        fallback.transform.localPosition = new Vector3(0.38f, -0.16f, 1.16f);
        fallback.transform.localRotation = Quaternion.Euler(0f, -7f, 0f);
        fallback.transform.localScale = Vector3.one * 0.00135f;
        var fallbackRect = (RectTransform)fallback.transform;
        fallbackRect.sizeDelta = new Vector2(390f, 510f);
        fallbackCanvas = fallback.GetComponent<Canvas>();
        fallbackCanvas.renderMode = RenderMode.WorldSpace;
        fallbackCanvas.sortingOrder = 480;

        var root = new GameObject("VR New User Tutorial Second Page", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        root.transform.SetParent(fallbackRect, false);
        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(390f, 510f);
        rect.anchoredPosition = Vector2.zero;
        tutorialPanel = root;
        tutorialRect = rect;
        var background = root.GetComponent<Image>();
        background.color = new Color(0.015f, 0.08f, 0.14f, 0.94f);
        if (AdmissionLetterGuide.ApplyOptionalSprite(background, "AdmissionLetterUI/tutorial_background"))
            background.color = Color.white;

        CreateTutorialButton(rect, "‹ 返回上一页", "tutorial_previous", new Vector2(-98f, 222f));
        CreateTutorialButton(rect, "继续下一页 ›", "tutorial_next", new Vector2(98f, 222f));

        var interruptionObject = new GameObject("Tutorial Resume Status", typeof(RectTransform), typeof(Text));
        var interruptionRect = (RectTransform)interruptionObject.transform;
        interruptionRect.SetParent(rect, false);
        interruptionRect.anchoredPosition = new Vector2(0f, 174f);
        interruptionRect.sizeDelta = new Vector2(350f, 52f);
        interruptionText = interruptionObject.GetComponent<Text>();
        interruptionText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        interruptionText.fontSize = 15;
        interruptionText.alignment = TextAnchor.MiddleCenter;
        interruptionText.color = new Color(1f, 0.82f, 0.28f, 1f);
        interruptionText.gameObject.SetActive(false);

        var textObject = new GameObject("Instruction", typeof(RectTransform), typeof(Text));
        var textRect = (RectTransform)textObject.transform;
        textRect.SetParent(rect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(22f, 52f);
        textRect.offsetMax = new Vector2(-22f, -92f);
        tutorialText = textObject.GetComponent<Text>();
        tutorialText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ??
                            Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
        tutorialText.fontSize = 18;
        tutorialText.lineSpacing = 1.05f;
        tutorialText.alignment = TextAnchor.MiddleLeft;
        tutorialText.horizontalOverflow = HorizontalWrapMode.Wrap;
        tutorialText.verticalOverflow = VerticalWrapMode.Truncate;
        tutorialText.resizeTextForBestFit = true;
        tutorialText.resizeTextMinSize = 14;
        tutorialText.resizeTextMaxSize = 18;
        tutorialText.color = new Color(0.82f, 0.96f, 1f, 1f);
        var footerObject = new GameObject("History Navigation Help", typeof(RectTransform), typeof(Text));
        var footerRect = (RectTransform)footerObject.transform;
        footerRect.SetParent(rect, false);
        footerRect.anchoredPosition = new Vector2(0f, -222f);
        footerRect.sizeDelta = new Vector2(350f, 34f);
        var footer = footerObject.GetComponent<Text>();
        footer.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        footer.fontSize = 14;
        footer.alignment = TextAnchor.MiddleCenter;
        footer.color = new Color(0.62f, 0.9f, 1f, 1f);
        footer.text = "右手射线对准上方按钮，再按右手食指扳机键";
        SetInstruction(CurrentInstruction ?? string.Empty);
    }

    private void UpdatePanelPosition()
    {
        if (tutorialRect == null || fallbackCanvas == null || AdmissionLetterGuide.Instance == null)
            return;
        var shouldAttach = AdmissionLetterGuide.Instance.IsOpen;
        if (shouldAttach != attachedToLetter)
        {
            attachedToLetter = shouldAttach;
            tutorialRect.SetParent(shouldAttach
                ? AdmissionLetterGuide.Instance.MenuUiRoot
                : (RectTransform)fallbackCanvas.transform, false);
            tutorialRect.localScale = Vector3.one;
            tutorialRect.localRotation = Quaternion.identity;
            if (attachedToLetter)
                tutorialRect.pivot = new Vector2(0f, 0.5f);
            else
                tutorialRect.pivot = new Vector2(0.5f, 0.5f);
        }
        if (attachedToLetter)
        {
            tutorialRect.anchoredPosition = new Vector2(350f, -82f);
            tutorialRect.localRotation = Quaternion.Euler(0f, 30f, 0f);
        }
        else
        {
            tutorialRect.anchoredPosition = new Vector2(0f, 0f);
            tutorialRect.localRotation = Quaternion.identity;
        }
        if (tutorialPanel != null && !string.IsNullOrWhiteSpace(CurrentInstruction))
            tutorialPanel.SetActive(true);
    }

    private static void CreateTutorialButton(RectTransform parent, string label, string actionId, Vector2 position)
    {
        var item = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(BoxCollider), typeof(AdmissionLetterMenuButton));
        var rect = (RectTransform)item.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(160f, 38f);
        var color = new Color(0.06f, 0.38f, 0.52f, 0.96f);
        var image = item.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        if (AdmissionLetterGuide.ApplyOptionalSprite(image, "AdmissionLetterUI/button_default"))
            color = image.color = Color.white;
        item.GetComponent<BoxCollider>().size = new Vector3(160f, 38f, 8f);
        item.GetComponent<AdmissionLetterMenuButton>().Configure(
            AdmissionLetterGuide.Instance, actionId, image, color);

        var textObject = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Outline));
        var textRect = (RectTransform)textObject.transform;
        textRect.SetParent(rect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var text = textObject.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ??
                    Font.CreateDynamicFontFromOSFont(
                        new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
        text.fontSize = 18;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.text = label;
        var outline = textObject.GetComponent<Outline>();
        outline.effectColor = new Color(0.01f, 0.05f, 0.1f, 0.95f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
    }

    private IEnumerator HideCompletionMessage()
    {
        yield return new WaitForSecondsRealtime(12f);
        SetInstruction(string.Empty);
        completionRoutine = null;
    }
    
    private void InitializeInputActions()
    {
        rightPositionAction = CreateInput("Tutorial Right Position", "<XRController>{RightHand}/devicePosition", "Vector3");
        rightRotationAction = CreateInput("Tutorial Right Rotation", "<XRController>{RightHand}/deviceRotation", "Quaternion");
        rightTriggerAction = CreateInput("Tutorial Right Trigger", "<XRController>{RightHand}/trigger", "Axis");
        rightPositionAction.Enable();
        rightRotationAction.Enable();
        rightTriggerAction.Enable();
    }
    
    private void UpdateTutorialRayInteraction()
    {
        if (tutorialPanel == null || !tutorialPanel.activeInHierarchy || attachedToLetter)
            return;
        if (AdmissionLetterGuide.Instance != null && AdmissionLetterGuide.Instance.IsOpen)
            return;
        
        GetRightHandPose(out var rayOrigin, out var rayRotation);
        AdmissionLetterMenuButton target = null;
        
        if (Physics.Raycast(rayOrigin, rayRotation * Vector3.forward, out var hit, 12f,
                ~0, QueryTriggerInteraction.Collide))
            target = hit.collider.GetComponent<AdmissionLetterMenuButton>();
        
        if (target != hoveredButton)
        {
            hoveredButton?.SetHovered(false);
            hoveredButton = target;
            hoveredButton?.SetHovered(true);
        }
        
        var triggerPressed = rightTriggerAction.ReadValue<float>() > 0.55f;
        if (triggerPressed && !triggerWasPressed)
            hoveredButton?.Activate();
        triggerWasPressed = triggerPressed;
    }
    
    private void GetRightHandPose(out Vector3 position, out Quaternion rotation)
    {
        var localPosition = rightPositionAction.ReadValue<Vector3>();
        var localRotation = rightRotationAction.ReadValue<Quaternion>();
        
        if (trackingOrigin == null)
        {
            var head = Camera.main?.transform;
            if (head != null)
                trackingOrigin = head.GetComponentInParent<XROrigin>()?.transform;
        }
        
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
    
    private static InputAction CreateInput(string name, string binding, string expectedType)
    {
        return new InputAction(name, InputActionType.Value, binding, expectedControlType: expectedType);
    }
}
