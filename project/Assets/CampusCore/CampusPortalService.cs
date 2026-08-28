using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class CampusPortalService : MonoBehaviour
{
    public static CampusPortalService Instance { get; private set; }
    public bool IsPortalOpen => portalRoot != null && portalRoot.activeSelf;

    private GameObject portalRoot;
    private Transform portalSurface;
    private TextMesh portalLabel;
    private Transform head;
    private int targetSceneIndex = -1;
    private string targetName;
    private InputAction rightPositionAction;
    private InputAction rightRotationAction;
    private InputAction rightConfirmAction;
    private bool confirmWasPressed;
    private bool loading;
    private CanvasGroup fadeGroup;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildFadeOverlay();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnEnable()
    {
        rightPositionAction = CreateInput("Portal Right Hand Position", "<XRController>{RightHand}/devicePosition", "Vector3");
        rightRotationAction = CreateInput("Portal Right Hand Rotation", "<XRController>{RightHand}/deviceRotation", "Quaternion");
        rightConfirmAction = CreateInput("Portal Right Confirm", "<XRController>{RightHand}/trigger", "Axis");
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
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!IsPortalOpen || loading)
            return;
        if (head == null)
            head = Camera.main != null ? Camera.main.transform : null;
        if (head == null)
            return;

        AnimatePortal();
        if (IsHeadInsidePortal())
        {
            StartCoroutine(EnterPortal());
            return;
        }

        var pressed = rightConfirmAction.ReadValue<float>() > 0.55f;
        if (pressed && !confirmWasPressed && IsRightRayPointingAtPortal())
            StartCoroutine(EnterPortal());
        confirmWasPressed = pressed;
    }

    public bool OpenPortal(int sceneIndex, string destinationName)
    {
        if (loading || sceneIndex < 0 || sceneIndex >= SceneManager.sceneCountInBuildSettings)
            return false;
        if (sceneIndex == SceneManager.GetActiveScene().buildIndex)
            return false;

        head = Camera.main != null ? Camera.main.transform : null;
        if (head == null)
            return false;
        if (portalRoot != null)
            Destroy(portalRoot);
        targetSceneIndex = sceneIndex;
        targetName = string.IsNullOrWhiteSpace(destinationName) ? "目标场景" : destinationName;
        BuildPortal();
        CampusFeedbackService.Instance?.TaskComplete();
        return true;
    }

    public void CancelPortal()
    {
        if (portalRoot != null) Destroy(portalRoot);
        portalRoot = null;
        targetSceneIndex = -1;
        if (loading)
        {
            loading = false;
            CampusInteractionCoordinator.Instance?.ForceSetMode(CampusInteractionMode.Exploration);
        }
    }

    private void BuildPortal()
    {
        portalRoot = new GameObject("Campus Portal - " + targetName);
        var flatForward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (flatForward.sqrMagnitude < 0.1f) flatForward = Vector3.forward;
        portalRoot.transform.position = head.position + flatForward * 2.2f - Vector3.up * 0.32f;
        portalRoot.transform.rotation = Quaternion.LookRotation(-flatForward, Vector3.up);

        var material = CreatePortalMaterial(new Color(0.08f, 0.82f, 1f, 1f));
        const int frameSegmentCount = 48;
        for (var i = 0; i < frameSegmentCount; i++)
        {
            var angle = i / (float)frameSegmentCount * Mathf.PI * 2f;
            var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            segment.name = "Portal Light " + i;
            segment.transform.SetParent(portalRoot.transform, false);
            segment.transform.localPosition = new Vector3(Mathf.Cos(angle) * 0.72f,
                Mathf.Sin(angle) * 1.08f, 0f);
            segment.transform.localRotation = Quaternion.Euler(0f, 0f, -angle * Mathf.Rad2Deg);
            segment.transform.localScale = new Vector3(0.15f, 0.105f, 0.10f);
            Destroy(segment.GetComponent<Collider>());
            segment.GetComponent<Renderer>().sharedMaterial = material;
        }

        var surface = new GameObject("Portal Surface", typeof(MeshFilter), typeof(MeshRenderer));
        surface.name = "Portal Surface";
        surface.transform.SetParent(portalRoot.transform, false);
        surface.transform.localScale = new Vector3(1.28f, 1.95f, 1f);
        surface.GetComponent<MeshFilter>().sharedMesh = CreateEllipseMesh(64);
        var surfaceMaterial = CreatePortalMaterial(new Color(0.35f, 0.85f, 1f, 0.82f));
        var portalTexture = Resources.Load<Texture2D>("AdmissionLetterUI/menu_background");
        if (portalTexture != null)
            surfaceMaterial.mainTexture = portalTexture;
        surface.GetComponent<MeshRenderer>().sharedMaterial = surfaceMaterial;
        var portalTrigger = surface.AddComponent<BoxCollider>();
        portalTrigger.isTrigger = true;
        portalTrigger.size = new Vector3(1f, 1f, 0.02f);
        portalSurface = surface.transform;

        var label = new GameObject("Portal Instructions");
        label.transform.SetParent(portalRoot.transform, false);
        label.transform.localPosition = new Vector3(0f, 1.5f, -0.04f);
        label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        portalLabel = label.AddComponent<TextMesh>();
        portalLabel.anchor = TextAnchor.MiddleCenter;
        portalLabel.alignment = TextAlignment.Center;
        portalLabel.fontSize = 46;
        portalLabel.characterSize = 0.012f;
        portalLabel.color = new Color(0.7f, 0.96f, 1f, 1f);
        portalLabel.text = "前往：" + targetName + "\n\n方法一：向前走进传送门\n方法二：右手射线对准门面，按食指扳机键\n场景会短暂变暗，这是正常切换过程";
        var labelRenderer = portalLabel.GetComponent<MeshRenderer>();
        labelRenderer.sortingOrder = 12;

        var background = new GameObject("Portal Instruction UI Background");
        background.transform.SetParent(portalRoot.transform, false);
        background.transform.localPosition = new Vector3(0f, 1.5f, -0.015f);
        background.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var backgroundRenderer = background.AddComponent<SpriteRenderer>();
        backgroundRenderer.sprite = Resources.Load<Sprite>("AdmissionLetterUI/gaze_card");
        backgroundRenderer.color = new Color(0.12f, 0.18f, 0.26f, 0.98f);
        backgroundRenderer.sortingOrder = 11;
        if (backgroundRenderer.sprite != null)
        {
            var spriteSize = backgroundRenderer.sprite.bounds.size;
            background.transform.localScale = new Vector3(
                2.15f / Mathf.Max(0.01f, spriteSize.x),
                0.72f / Mathf.Max(0.01f, spriteSize.y), 1f);
        }
    }

    private bool IsHeadInsidePortal()
    {
        var local = portalRoot.transform.InverseTransformPoint(head.position);
        return Mathf.Abs(local.x) < 0.66f && Mathf.Abs(local.y) < 1.08f && Mathf.Abs(local.z) < 0.24f;
    }

    private bool IsRightRayPointingAtPortal()
    {
        var origin = rightPositionAction.ReadValue<Vector3>();
        var rotation = rightRotationAction.ReadValue<Quaternion>();
        var xrOrigin = head.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
        if (xrOrigin != null)
        {
            origin = xrOrigin.transform.TransformPoint(origin);
            rotation = xrOrigin.transform.rotation * rotation;
        }
        return Physics.Raycast(origin, rotation * Vector3.forward, out var hit, 12f,
                   ~0, QueryTriggerInteraction.Collide) && hit.transform == portalSurface;
    }

    private IEnumerator EnterPortal()
    {
        if (loading) yield break;
        loading = true;
        CampusInteractionCoordinator.Instance?.ForceSetMode(CampusInteractionMode.Loading);
        CampusFeedbackService.Instance?.TaskComplete();
        yield return Fade(0f, 1f, 0.35f);
        if (portalRoot != null) Destroy(portalRoot);
        portalRoot = null;
        if (PicoSceneNavigator.Instance == null)
        {
            loading = false;
            targetSceneIndex = -1;
            CampusInteractionCoordinator.Instance?.ForceSetMode(CampusInteractionMode.Exploration);
            yield return Fade(1f, 0f, 0.45f);
            yield break;
        }
        PicoSceneNavigator.Instance.LoadSceneByIndex(targetSceneIndex);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!loading) return;
        head = null;
        StartCoroutine(FinishSceneArrival());
    }

    private IEnumerator FinishSceneArrival()
    {
        yield return null;
        yield return Fade(1f, 0f, 0.45f);
        loading = false;
        targetSceneIndex = -1;
        CampusInteractionCoordinator.Instance?.ForceSetMode(CampusInteractionMode.Exploration);
    }

    private void AnimatePortal()
    {
        for (var i = 0; i < portalRoot.transform.childCount; i++)
        {
            var child = portalRoot.transform.GetChild(i);
            if (!child.name.StartsWith("Portal Light")) continue;
            var pulse = 0.85f + Mathf.Sin(Time.unscaledTime * 4f + i * 0.5f) * 0.18f;
            child.localScale = new Vector3(0.15f, 0.105f, 0.10f) * pulse;
        }
    }

    private void BuildFadeOverlay()
    {
        var root = new GameObject("Portal Fade", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(Image));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;
        var rect = (RectTransform)root.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        fadeGroup = root.GetComponent<CanvasGroup>();
        fadeGroup.alpha = 0f;
        fadeGroup.blocksRaycasts = false;
        root.GetComponent<Image>().color = Color.black;
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        var elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            fadeGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        fadeGroup.alpha = to;
    }

    private static Material CreatePortalMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        var material = new Material(shader);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 2.5f);
        return material;
    }

    private static Mesh CreateEllipseMesh(int segments)
    {
        var vertices = new Vector3[segments + 1];
        var uv = new Vector2[segments + 1];
        var triangles = new int[segments * 3];
        vertices[0] = Vector3.zero;
        uv[0] = new Vector2(0.5f, 0.5f);
        for (var i = 0; i < segments; i++)
        {
            var angle = i / (float)segments * Mathf.PI * 2f;
            var x = Mathf.Cos(angle) * 0.5f;
            var y = Mathf.Sin(angle) * 0.5f;
            vertices[i + 1] = new Vector3(x, y, 0f);
            uv[i + 1] = new Vector2(x + 0.5f, y + 0.5f);
            var offset = i * 3;
            triangles[offset] = 0;
            triangles[offset + 1] = i + 1;
            triangles[offset + 2] = (i + 1) % segments + 1;
        }
        var mesh = new Mesh { name = "Portal Ellipse UI Mesh" };
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static InputAction CreateInput(string name, string binding, string expectedType)
    {
        return new InputAction(name, InputActionType.Value, binding, expectedControlType: expectedType);
    }
}
