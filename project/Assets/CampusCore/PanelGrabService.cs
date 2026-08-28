using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

public sealed class PanelGrabService : MonoBehaviour
{
    public static event Action<Transform> PanelPickedUp;
    private static readonly HashSet<int> BusyHands = new HashSet<int>();
    private static readonly string[] PanelNames =
    {
        "牌1", "牌2", "牌3", "牌4", "牌5", "牌6",
        "牌7", "牌8", "牌9", "牌10", "牌11"
    };

    [SerializeField] private float selectionDistance = 8f;
    [SerializeField] private float holdDistance = 0.4f;

    private InputAction leftPosition;
    private InputAction leftRotation;
    private InputAction rightPosition;
    private InputAction rightRotation;
    private InputAction leftTrigger;
    private InputAction rightTrigger;
    private Transform trackingOrigin;
    private PanelGrabInteractable leftHeld;
    private PanelGrabInteractable rightHeld;
    private bool leftWasPressed;
    private bool rightWasPressed;

    public static bool IsHandBusy(int hand)
    {
        return BusyHands.Contains(hand < 0 ? -1 : 1);
    }

    private void Awake()
    {
        trackingOrigin = GetComponent<XROrigin>()?.transform;
        leftPosition = CreateInput("Panel Left Position", "<XRController>{LeftHand}/devicePosition", "Vector3");
        leftRotation = CreateInput("Panel Left Rotation", "<XRController>{LeftHand}/deviceRotation", "Quaternion");
        rightPosition = CreateInput("Panel Right Position", "<XRController>{RightHand}/devicePosition", "Vector3");
        rightRotation = CreateInput("Panel Right Rotation", "<XRController>{RightHand}/deviceRotation", "Quaternion");
        leftTrigger = CreateInput("Panel Left Trigger", "<XRController>{LeftHand}/trigger", "Axis");
        rightTrigger = CreateInput("Panel Right Trigger", "<XRController>{RightHand}/trigger", "Axis");
        EnableInputs(true);
        SceneManager.sceneLoaded += OnSceneLoaded;
        ConfigurePanels(SceneManager.GetActiveScene());
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Release(-1);
        Release(1);
        EnableInputs(false);
        leftPosition?.Dispose();
        leftRotation?.Dispose();
        rightPosition?.Dispose();
        rightRotation?.Dispose();
        leftTrigger?.Dispose();
        rightTrigger?.Dispose();
    }

    private void Update()
    {
        var leftPressed = leftTrigger.ReadValue<float>() > 0.55f;
        var rightPressed = rightTrigger.ReadValue<float>() > 0.55f;
        ProcessHand(-1, leftPressed, leftWasPressed);
        ProcessHand(1, rightPressed, rightWasPressed);
        leftWasPressed = leftPressed;
        rightWasPressed = rightPressed;
    }

    private void LateUpdate()
    {
        UpdateHeldPose(-1, leftHeld);
        UpdateHeldPose(1, rightHeld);
    }

    private void ProcessHand(int hand, bool pressed, bool wasPressed)
    {
        if (pressed && !wasPressed)
            TryGrab(hand);
        else if (!pressed && wasPressed)
            Release(hand);
    }

    private void TryGrab(int hand)
    {
        if (IsHandBusy(hand))
            return;
        if (hand < 0 && AdmissionLetterGuide.Instance != null &&
            AdmissionLetterGuide.Instance.IsHeld)
            return;

        GetHandPose(hand, out var position, out var rotation);
        if (!Physics.Raycast(position, rotation * Vector3.forward, out var hit,
                selectionDistance, ~0, QueryTriggerInteraction.Collide))
            return;

        var panel = hit.collider.GetComponentInParent<PanelGrabInteractable>();
        if (panel == null || panel.IsHeld)
            return;

        panel.BeginGrab(hand);
        PanelPickedUp?.Invoke(panel.transform);
        BusyHands.Add(hand);
        if (hand < 0) leftHeld = panel;
        else rightHeld = panel;
        CampusFeedbackService.Instance?.Confirm(hand < 0);
    }

    private void Release(int hand)
    {
        var panel = hand < 0 ? leftHeld : rightHeld;
        if (panel != null)
            panel.EndGrab();
        if (hand < 0) leftHeld = null;
        else rightHeld = null;
        BusyHands.Remove(hand);
    }

    private void UpdateHeldPose(int hand, PanelGrabInteractable panel)
    {
        if (panel == null)
            return;
        GetHandPose(hand, out var position, out var rotation);
        panel.SetHeldPose(position + rotation * Vector3.forward * holdDistance);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Release(-1);
        Release(1);
        ConfigurePanels(scene);
    }

    private static void ConfigurePanels(Scene scene)
    {
        foreach (var root in scene.GetRootGameObjects())
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (Array.IndexOf(PanelNames, transform.name) < 0)
                continue;
            if (transform.GetComponent<PanelGrabInteractable>() == null)
                transform.gameObject.AddComponent<PanelGrabInteractable>();
        }
    }

    private void GetHandPose(int hand, out Vector3 position, out Quaternion rotation)
    {
        var localPosition = (hand < 0 ? leftPosition : rightPosition).ReadValue<Vector3>();
        var localRotation = (hand < 0 ? leftRotation : rightRotation).ReadValue<Quaternion>();
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

    private void EnableInputs(bool enabled)
    {
        var inputs = new[]
        {
            leftPosition, leftRotation, rightPosition, rightRotation,
            leftTrigger, rightTrigger
        };
        foreach (var input in inputs)
        {
            if (input == null) continue;
            if (enabled) input.Enable();
            else input.Disable();
        }
    }

    private static InputAction CreateInput(string name, string binding, string expectedType)
    {
        return new InputAction(name, InputActionType.Value, binding,
            expectedControlType: expectedType);
    }
}

public sealed class PanelGrabInteractable : MonoBehaviour
{
    [SerializeField] private float grabScale = 0.20f;
    
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private Vector3 homeScale;
    private Transform homeParent;
    private bool returning;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 centerOffset;
    private Vector3 heldCenterOffset;
    private Vector3 faceNormalLocal = Vector3.forward;

    public bool IsHeld { get; private set; }

    private void Awake()
    {
        homeParent = transform.parent;
        homePosition = transform.localPosition;
        homeRotation = transform.localRotation;
        homeScale = transform.localScale;
        CalculatePresentationBounds();
        EnsureCollider();
    }

    public void BeginGrab(int hand)
    {
        returning = false;
        IsHeld = true;
        transform.SetParent(null);
        transform.localScale = homeScale * grabScale;
        heldCenterOffset = Vector3.Scale(centerOffset, transform.localScale);
        targetPosition = transform.position;
        targetRotation = transform.rotation;
    }

    public void SetHeldPose(Vector3 position)
    {
        if (!IsHeld) return;
        var head = Camera.main?.transform;
        if (head != null)
        {
            var lookDirection = head.position - position;
            if (lookDirection.sqrMagnitude > 0.001f)
            {
                lookDirection.Normalize();
                var normal = faceNormalLocal;
                if (Vector3.Dot(transform.TransformDirection(normal), lookDirection) < 0f)
                    normal = -normal;
                targetRotation = GetUprightFacingRotation(normal, lookDirection);
            }
        }
        targetPosition = position - targetRotation * heldCenterOffset;
    }

    public void EndGrab()
    {
        IsHeld = false;
        returning = true;
        transform.SetParent(homeParent, false);
    }

    // Map the board face to the viewer while its local up remains world up.
    // This prevents the held panel from rolling sideways with the controller.
    private Quaternion GetUprightFacingRotation(Vector3 localNormal, Vector3 worldLookDirection)
    {
        var localUp = Vector3.up;
        if (Mathf.Abs(Vector3.Dot(localNormal.normalized, localUp)) > 0.98f)
            localUp = Vector3.forward;
        var localFaceBasis = Quaternion.LookRotation(localNormal, localUp);
        return Quaternion.LookRotation(worldLookDirection, Vector3.up) * Quaternion.Inverse(localFaceBasis);
    }

    private void Update()
    {
        if (IsHeld)
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * 12f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 12f);
            return;
        }
        if (!returning) return;
        transform.localPosition = Vector3.Lerp(
            transform.localPosition, homePosition, Time.deltaTime * 9f);
        transform.localRotation = Quaternion.Slerp(
            transform.localRotation, homeRotation, Time.deltaTime * 9f);
        transform.localScale = Vector3.Lerp(
            transform.localScale, homeScale, Time.deltaTime * 9f);
        if (Vector3.SqrMagnitude(transform.localPosition - homePosition) < 0.000001f &&
            Quaternion.Angle(transform.localRotation, homeRotation) < 0.1f)
        {
            transform.localPosition = homePosition;
            transform.localRotation = homeRotation;
            transform.localScale = homeScale;
            returning = false;
        }
    }

    private void EnsureCollider()
    {
        if (GetComponentInChildren<Collider>(true) != null)
            return;

        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;
        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        var box = gameObject.AddComponent<BoxCollider>();
        box.center = transform.InverseTransformPoint(bounds.center);
        var localMin = transform.InverseTransformPoint(bounds.min);
        var localMax = transform.InverseTransformPoint(bounds.max);
        box.size = new Vector3(
            Mathf.Abs(localMax.x - localMin.x),
            Mathf.Abs(localMax.y - localMin.y),
            Mathf.Abs(localMax.z - localMin.z));
    }

    private void CalculatePresentationBounds()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        centerOffset = transform.InverseTransformPoint(bounds.center);
        var localMin = transform.InverseTransformPoint(bounds.min);
        var localMax = transform.InverseTransformPoint(bounds.max);
        var size = new Vector3(Mathf.Abs(localMax.x - localMin.x),
            Mathf.Abs(localMax.y - localMin.y), Mathf.Abs(localMax.z - localMin.z));
        if (size.x <= size.y && size.x <= size.z) faceNormalLocal = Vector3.right;
        else if (size.y <= size.x && size.y <= size.z) faceNormalLocal = Vector3.up;
        else faceNormalLocal = Vector3.forward;
    }
}
