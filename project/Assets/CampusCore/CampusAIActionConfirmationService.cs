using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class CampusAIActionConfirmationService : MonoBehaviour
{
    public static CampusAIActionConfirmationService Instance { get; private set; }
    public bool IsShowing => canvas != null && canvas.gameObject.activeSelf;

    private Canvas canvas;
    private Text descriptionText;
    private CampusAIActionChoice hoveredChoice;
    private Action confirmAction;
    private Transform head;
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
        BuildPanel();
    }

    private void OnEnable()
    {
        rightPositionAction = CreateInput("AI Action Right Hand Position", "<XRController>{RightHand}/devicePosition", "Vector3");
        rightRotationAction = CreateInput("AI Action Right Hand Rotation", "<XRController>{RightHand}/deviceRotation", "Quaternion");
        rightConfirmAction = CreateInput("AI Action Right Confirm", "<XRController>{RightHand}/trigger", "Axis");
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
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!IsShowing)
            return;
        if (head == null)
            head = Camera.main != null ? Camera.main.transform : null;
        if (head == null)
            return;
        UpdatePose();
        UpdatePointer();
    }

    public bool Request(string description, Action onConfirm)
    {
        if (IsShowing || onConfirm == null)
            return false;
        head = Camera.main != null ? Camera.main.transform : null;
        if (head == null)
            return false;
        confirmAction = onConfirm;
        descriptionText.text = "AI 建议执行以下操作：\n\n" + description +
            "\n\n确认后系统才会执行。若这不是你想要的，请选择“取消”。";
        canvas.gameObject.SetActive(true);
        UpdatePose(true);
        CampusFeedbackService.Instance?.Confirm();
        return true;
    }

    public void HandleChoice(bool confirmed)
    {
        var action = confirmed ? confirmAction : null;
        Hide();
        if (confirmed)
        {
            CampusFeedbackService.Instance?.TaskComplete();
            action?.Invoke();
        }
        else
        {
            CampusFeedbackService.Instance?.Hover();
            Debug.Log("用户已取消 AI 建议操作。");
        }
    }

    private void Hide()
    {
        hoveredChoice?.SetHovered(false);
        hoveredChoice = null;
        confirmAction = null;
        canvas.gameObject.SetActive(false);
    }

    private void UpdatePose(bool immediate = false)
    {
        var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
        var target = head.position + forward * 1.35f;
        canvas.transform.position = immediate ? target : Vector3.Lerp(
            canvas.transform.position, target, Time.unscaledDeltaTime * 6f);
        canvas.transform.rotation = Quaternion.LookRotation(canvas.transform.position - head.position, Vector3.up);
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
        CampusAIActionChoice target = null;
        if (Physics.Raycast(origin, rotation * Vector3.forward, out var hit, 12f,
                ~0, QueryTriggerInteraction.Collide))
            target = hit.collider.GetComponent<CampusAIActionChoice>();
        if (target != hoveredChoice)
        {
            hoveredChoice?.SetHovered(false);
            hoveredChoice = target;
            hoveredChoice?.SetHovered(true);
        }
        var pressed = rightConfirmAction.ReadValue<float>() > 0.55f;
        if (pressed && !confirmWasPressed) hoveredChoice?.Activate();
        confirmWasPressed = pressed;
    }

    private void BuildPanel()
    {
        var root = new GameObject("AI Action Confirmation", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasGroup), typeof(Image));
        root.transform.SetParent(transform, false);
        root.transform.localScale = Vector3.one * 0.0015f;
        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(570f, 300f);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 1000;
        root.GetComponent<Image>().color = new Color(0.025f, 0.075f, 0.13f, 0.97f);

        var title = CreateText(rect, "AI 操作确认", 28, new Vector2(0f, 117f), new Vector2(520f, 45f));
        title.alignment = TextAnchor.MiddleCenter;
        title.color = new Color(1f, 0.78f, 0.25f, 1f);
        descriptionText = CreateText(rect, "", 19, new Vector2(0f, 23f), new Vector2(500f, 150f));
        CreateChoice(rect, "确认执行", true, new Vector2(-105f, -100f), new Color(0.05f, 0.52f, 0.62f, 1f));
        CreateChoice(rect, "取消", false, new Vector2(105f, -100f), new Color(0.35f, 0.18f, 0.22f, 1f));
        var tip = CreateText(rect, "操作：右手射线对准按钮，再按右手食指扳机键一次", 15,
            new Vector2(0f, -136f), new Vector2(520f, 24f));
        tip.alignment = TextAnchor.MiddleCenter;
        tip.color = new Color(0.55f, 0.88f, 1f, 1f);
        root.SetActive(false);
    }

    private void CreateChoice(RectTransform parent, string label, bool confirmed,
        Vector2 position, Color color)
    {
        var item = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(BoxCollider), typeof(CampusAIActionChoice));
        var rect = (RectTransform)item.transform;
        rect.SetParent(parent, false);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(180f, 48f);
        var image = item.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        item.GetComponent<BoxCollider>().size = new Vector3(180f, 48f, 8f);
        item.GetComponent<CampusAIActionChoice>().Configure(this, confirmed, image, color);
        var text = CreateText(rect, label, 20, Vector2.zero, new Vector2(170f, 44f));
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
        text.color = new Color(0.9f, 0.96f, 1f, 1f);
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.text = value;
        return text;
    }

    private static InputAction CreateInput(string name, string binding, string expectedType)
    {
        return new InputAction(name, InputActionType.Value, binding, expectedControlType: expectedType);
    }
}

public sealed class CampusAIActionChoice : MonoBehaviour
{
    private CampusAIActionConfirmationService owner;
    private bool confirmed;
    private Image image;
    private Color normalColor;

    public void Configure(CampusAIActionConfirmationService service, bool value,
        Image background, Color normal)
    {
        owner = service;
        confirmed = value;
        image = background;
        normalColor = normal;
    }

    public void SetHovered(bool hovered)
    {
        if (image != null) image.color = hovered ? new Color(0.18f, 0.82f, 1f, 1f) : normalColor;
        if (hovered) CampusFeedbackService.Instance?.Hover();
    }

    public void Activate() => owner?.HandleChoice(confirmed);
}
