using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class CampusAICompanionService : MonoBehaviour
{
    public static CampusAICompanionService Instance { get; private set; }
    public event System.Action<bool> VisibilityChanged;
    public bool IsVisible => bubbleCanvas != null && bubbleCanvas.gameObject.activeSelf;
    public Transform BubbleTransform => bubbleCanvas != null ? bubbleCanvas.transform : null;

    [SerializeField] private float helpDelay = 28f;
    [SerializeField] private float promptCooldown = 90f;
    [SerializeField] private int maxPromptsPerSession = 3;

    private Transform head;
    private Canvas bubbleCanvas;
    private Text messageText;
    private CampusCompanionChoice hoveredChoice;
    private float lastMeaningfulActivity;
    private float lastPromptTime = -999f;
    private int promptCount;
    private InputAction rightPositionAction;
    private InputAction rightRotationAction;
    private InputAction rightConfirmAction;
    private bool confirmWasPressed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        lastMeaningfulActivity = Time.unscaledTime;
        BuildBubble();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void Start()
    {
        if (CampusTaskService.Instance != null)
            CampusTaskService.Instance.TaskCompleted += OnTaskCompleted;
        if (CampusContextService.Instance != null)
            CampusContextService.Instance.ContextChanged += ReportMeaningfulActivity;
        if (CampusNavigationService.Instance != null)
            CampusNavigationService.Instance.NavigationStarted += OnNavigationStarted;
    }

    private void OnEnable()
    {
        rightPositionAction = CreateInput("Companion Right Hand Position", "<XRController>{RightHand}/devicePosition", "Vector3");
        rightRotationAction = CreateInput("Companion Right Hand Rotation", "<XRController>{RightHand}/deviceRotation", "Quaternion");
        rightConfirmAction = CreateInput("Companion Right Confirm", "<XRController>{RightHand}/trigger", "Axis");
        rightPositionAction.Enable();
        rightRotationAction.Enable();
        rightConfirmAction.Enable();
    }

    private void OnDisable()
    {
        rightPositionAction?.Dispose();
        rightRotationAction?.Dispose();
        rightConfirmAction?.Dispose();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (CampusTaskService.Instance != null)
            CampusTaskService.Instance.TaskCompleted -= OnTaskCompleted;
        if (CampusContextService.Instance != null)
            CampusContextService.Instance.ContextChanged -= ReportMeaningfulActivity;
        if (CampusNavigationService.Instance != null)
            CampusNavigationService.Instance.NavigationStarted -= OnNavigationStarted;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (head == null)
            head = Camera.main != null ? Camera.main.transform : null;

        if (bubbleCanvas.gameObject.activeSelf)
        {
            UpdateBubblePose();
            UpdatePointer();
            return;
        }

        if (ShouldOfferHelp())
            ShowHelp();
    }

    public void ReportMeaningfulActivity()
    {
        lastMeaningfulActivity = Time.unscaledTime;
    }

    public void HandleChoice(string action)
    {
        ReportMeaningfulActivity();
        switch (action)
        {
            case "next":
                messageText.text = BuildNextStepMessage() +
                    "\n\n照着上面做即可。完成后系统会自动记录进度。";
                CampusFeedbackService.Instance?.Confirm();
                break;
            case "ask_here":
                HideBubble();
                var guide = AdmissionLetterGuide.Instance;
                if (guide != null)
                {
                    guide.ShowPageFromAI("guide");
                    var context = CampusContextService.Instance;
                    var place = context != null && context.HasRecentFocus
                        ? context.LocationName : "当前场景";
                    guide.SubmitRecognizedText("请用新生容易理解的方式介绍" + place +
                        "，并明确告诉我接下来可以做什么");
                }
                break;
            case "later":
                HideBubble();
                lastPromptTime = Time.unscaledTime + 90f;
                break;
        }
    }

    private bool ShouldOfferHelp()
    {
        if (head == null || promptCount >= maxPromptsPerSession ||
            Time.unscaledTime - lastMeaningfulActivity < helpDelay ||
            Time.unscaledTime - lastPromptTime < promptCooldown)
            return false;
        var progress = CampusProgressService.Instance;
        if (progress == null || !progress.Data.tutorialCompleted)
            return false;
        var coordinator = CampusInteractionCoordinator.Instance;
        if (coordinator != null && coordinator.Mode != CampusInteractionMode.Exploration &&
            coordinator.Mode != CampusInteractionMode.Guiding)
            return false;
        return CampusPortalService.Instance == null || !CampusPortalService.Instance.IsPortalOpen;
    }

    private void ShowHelp()
    {
        promptCount++;
        lastPromptTime = Time.unscaledTime;
        lastMeaningfulActivity = Time.unscaledTime;
        messageText.text = "需要帮助？\n\n打开录取通知书，我可以帮你：\n✦ 介绍当前地点\n✦ 推荐下一步探索\n✦ 查询校园知识\n\n" + BuildNextStepMessage();
        bubbleCanvas.gameObject.SetActive(true);
        VisibilityChanged?.Invoke(true);
        UpdateBubblePose(true);
        CampusFeedbackService.Instance?.Hover();
    }

    private static string BuildNextStepMessage()
    {
        var journey = CampusJourneyService.Instance;
        if (journey != null && journey.IsEnabled && !journey.IsComplete)
            return "当前建议：" + journey.CurrentStepTitle + "（" + journey.CurrentStepDuration + "）";
        var context = CampusContextService.Instance;
        if (context != null && context.HasRecentFocus)
            return "你刚才关注了“" + context.LocationName + "”";
        return "自由探索，或打开通知书获取指引";
    }

    private void HideBubble()
    {
        hoveredChoice?.SetHovered(false);
        hoveredChoice = null;
        bubbleCanvas.gameObject.SetActive(false);
        VisibilityChanged?.Invoke(false);
    }

    private void UpdatePointer()
    {
        var origin = rightPositionAction.ReadValue<Vector3>();
        var rotation = rightRotationAction.ReadValue<Quaternion>();
        var xrOrigin = head.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null)
        {
            origin = xrOrigin.transform.TransformPoint(origin);
            rotation = xrOrigin.transform.rotation * rotation;
        }

        CampusCompanionChoice target = null;
        if (Physics.Raycast(origin, rotation * Vector3.forward, out var hit, 12f,
                ~0, QueryTriggerInteraction.Collide))
            target = hit.collider.GetComponent<CampusCompanionChoice>();
        if (target != hoveredChoice)
        {
            hoveredChoice?.SetHovered(false);
            hoveredChoice = target;
            hoveredChoice?.SetHovered(true);
        }
        var pressed = rightConfirmAction.ReadValue<float>() > 0.55f;
        if (pressed && !confirmWasPressed)
            hoveredChoice?.Activate();
        confirmWasPressed = pressed;
    }

    private void UpdateBubblePose(bool immediate = false)
    {
        if (head == null) return;
        var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
        var target = head.position + forward * 1.45f + Vector3.up * 0.08f;
        bubbleCanvas.transform.position = immediate ? target : Vector3.Lerp(
            bubbleCanvas.transform.position, target, Time.unscaledDeltaTime * 5f);
        bubbleCanvas.transform.rotation = Quaternion.LookRotation(
            bubbleCanvas.transform.position - head.position, Vector3.up);
    }

    private void BuildBubble()
    {
        var root = new GameObject("Admission Letter AI Assistant", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasGroup), typeof(Image));
        root.transform.SetParent(transform, false);
        root.transform.localScale = Vector3.one * 0.0015f;
        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(590f, 285f);
        bubbleCanvas = root.GetComponent<Canvas>();
        bubbleCanvas.renderMode = RenderMode.WorldSpace;
        bubbleCanvas.sortingOrder = 800;
        root.GetComponent<Image>().color = new Color(0.025f, 0.09f, 0.16f, 0.95f);

        var title = CreateText(rect, "录取通知书 · AI助手", 25,
            new Vector2(0f, 112f), new Vector2(540f, 42f));
        title.alignment = TextAnchor.MiddleCenter;
        title.color = new Color(0.35f, 0.95f, 1f, 1f);
        messageText = CreateText(rect, "", 19, new Vector2(0f, 25f), new Vector2(525f, 130f));

        CreateChoice(rect, "打开通知书", "ask_here", new Vector2(-190f, -97f));
        CreateChoice(rect, "推荐下一步", "next", new Vector2(0f, -97f));
        CreateChoice(rect, "暂时不需要", "later", new Vector2(190f, -97f));
        bubbleCanvas.gameObject.SetActive(false);
    }

    private void CreateChoice(RectTransform parent, string label, string action, Vector2 position)
    {
        var item = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(BoxCollider), typeof(CampusCompanionChoice));
        var rect = (RectTransform)item.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(170f, 44f);
        var image = item.GetComponent<Image>();
        image.color = new Color(0.08f, 0.34f, 0.52f, 0.96f);
        image.raycastTarget = false;
        var collider = item.GetComponent<BoxCollider>();
        collider.size = new Vector3(170f, 44f, 8f);
        item.GetComponent<CampusCompanionChoice>().Configure(this, action, image);
        var text = CreateText(rect, label, 18, Vector2.zero, new Vector2(160f, 40f));
        text.alignment = TextAnchor.MiddleCenter;
    }

    private static Text CreateText(RectTransform parent, string value, int size,
        Vector2 position, Vector2 dimensions)
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
        text.color = new Color(0.88f, 0.96f, 1f, 1f);
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private void OnTaskCompleted(CampusTaskDefinition task) => ReportMeaningfulActivity();
    private void OnNavigationStarted(string id) => ReportMeaningfulActivity();
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        head = null;
        HideBubble();
        ReportMeaningfulActivity();
    }

    private static InputAction CreateInput(string name, string binding, string expectedType)
    {
        return new InputAction(name, InputActionType.Value, binding, expectedControlType: expectedType);
    }
}

public sealed class CampusCompanionChoice : MonoBehaviour
{
    private CampusAICompanionService owner;
    private string action;
    private Image image;
    private readonly Color normal = new Color(0.08f, 0.34f, 0.52f, 0.96f);

    public void Configure(CampusAICompanionService service, string actionId, Image background)
    {
        owner = service;
        action = actionId;
        image = background;
    }

    public void SetHovered(bool hovered)
    {
        if (image != null)
            image.color = hovered ? new Color(0.15f, 0.8f, 1f, 1f) : normal;
        if (hovered) CampusFeedbackService.Instance?.Hover();
    }

    public void Activate()
    {
        CampusFeedbackService.Instance?.Confirm();
        owner?.HandleChoice(action);
    }
}
