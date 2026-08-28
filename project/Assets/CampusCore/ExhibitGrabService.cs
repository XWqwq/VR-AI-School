using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;

/// <summary>
/// Enables close-up inspection of the existing exhibition models. Point at a
/// model and hold that hand's trigger to pick it up; releasing returns it to
/// its exact original pose.
/// </summary>
public sealed class ExhibitGrabService : MonoBehaviour
{
    private static readonly HashSet<int> BusyHands = new HashSet<int>();
    private static readonly string[] ExhibitNames =
    {
        "四轴机械手", "六足机器人", "机械臂", "机械狗",
        "减速器精度测量装置", "机器人", "无人机"
    };

    [SerializeField] private float selectionDistance = 12f;
    [SerializeField] private float holdDistance = 0.18f;

    private InputAction leftPosition;
    private InputAction leftRotation;
    private InputAction rightPosition;
    private InputAction rightRotation;
    private InputAction leftTrigger;
    private InputAction rightTrigger;
    private Transform trackingOrigin;
    private ExhibitGrabInteractable leftHeld;
    private ExhibitGrabInteractable rightHeld;
    private bool leftWasPressed;
    private bool rightWasPressed;

    public static bool IsHandBusy(int hand)
    {
        return BusyHands.Contains(hand < 0 ? -1 : 1);
    }

    private void Awake()
    {
        trackingOrigin = GetComponent<XROrigin>()?.transform;
        leftPosition = CreateInput("Exhibit Left Position", "<XRController>{LeftHand}/devicePosition", "Vector3");
        leftRotation = CreateInput("Exhibit Left Rotation", "<XRController>{LeftHand}/deviceRotation", "Quaternion");
        rightPosition = CreateInput("Exhibit Right Position", "<XRController>{RightHand}/devicePosition", "Vector3");
        rightRotation = CreateInput("Exhibit Right Rotation", "<XRController>{RightHand}/deviceRotation", "Quaternion");
        leftTrigger = CreateInput("Exhibit Left Trigger", "<XRController>{LeftHand}/trigger", "Axis");
        rightTrigger = CreateInput("Exhibit Right Trigger", "<XRController>{RightHand}/trigger", "Axis");
        EnableInputs(true);
        SceneManager.sceneLoaded += OnSceneLoaded;
        ConfigureExhibition(SceneManager.GetActiveScene());
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

        var exhibit = hit.collider.GetComponentInParent<ExhibitGrabInteractable>();
        if (exhibit == null || exhibit.IsHeld)
            return;

        exhibit.BeginGrab(hand);
        BusyHands.Add(hand);
        if (hand < 0) leftHeld = exhibit;
        else rightHeld = exhibit;
        CampusFeedbackService.Instance?.Confirm(hand < 0);
    }

    private void Release(int hand)
    {
        var exhibit = hand < 0 ? leftHeld : rightHeld;
        if (exhibit != null)
            exhibit.EndGrab();
        if (hand < 0) leftHeld = null;
        else rightHeld = null;
        BusyHands.Remove(hand);
    }

    private void UpdateHeldPose(int hand, ExhibitGrabInteractable exhibit)
    {
        if (exhibit == null)
            return;
        GetHandPose(hand, out var position, out var rotation);
        exhibit.SetHeldPose(position + rotation * Vector3.forward * holdDistance, rotation);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Release(-1);
        Release(1);
        ConfigureExhibition(scene);
    }

    private static void ConfigureExhibition(Scene scene)
    {
        if (scene.buildIndex != 2 && !scene.name.Contains("Exhibition"))
            return;

        foreach (var root in scene.GetRootGameObjects())
        foreach (var transform in root.GetComponentsInChildren<Transform>(true))
        {
            if (Array.IndexOf(ExhibitNames, transform.name) < 0)
                continue;
            if (transform.GetComponent<ExhibitGrabInteractable>() == null)
                transform.gameObject.AddComponent<ExhibitGrabInteractable>();
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

public sealed class ExhibitGrabInteractable : MonoBehaviour
{
    private Vector3 homePosition;
    private Quaternion homeRotation;
    private Vector3 homeScale;
    private Transform homeParent;
    private bool returning;
    private Vector3 centerOffset;
    private Vector3 heldCenterOffset;

    public bool IsHeld { get; private set; }

    private void Awake()
    {
        homeParent = transform.parent;
        homePosition = transform.localPosition;
        homeRotation = transform.localRotation;
        homeScale = transform.localScale;
        CalculateCenterOffset();
        EnsureCollider();
    }

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 smoothVelocity;

    public void BeginGrab(int hand)
    {
        returning = false;
        IsHeld = true;
        transform.SetParent(null);
        transform.localScale = homeScale * ResolveGrabScale();
        heldCenterOffset = Vector3.Scale(centerOffset, transform.localScale);
        targetPosition = transform.position;
        targetRotation = transform.rotation;
        smoothVelocity = Vector3.zero;
    }

    public void SetHeldPose(Vector3 position, Quaternion rotation)
    {
        if (!IsHeld) return;
        targetPosition = position - rotation * heldCenterOffset;
        targetRotation = rotation;
    }

    public void EndGrab()
    {
        IsHeld = false;
        returning = true;
        transform.SetParent(homeParent, false);
    }

    private void Update()
    {
        if (IsHeld)
        {
            transform.position = Vector3.Lerp(
                transform.position, targetPosition, Time.deltaTime * 18f);
            transform.rotation = Quaternion.Lerp(
                transform.rotation, targetRotation, Time.deltaTime * 20f);
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

    private void CalculateCenterOffset()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            centerOffset = Vector3.zero;
            return;
        }
        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        centerOffset = transform.InverseTransformPoint(bounds.center);
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

    private float ResolveGrabScale()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return 1f;
        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        var size = bounds.size.magnitude;
        if (size <= 1.2f) return 1f;
        if (size >= 4f) return 0.22f;
        return 0.45f;
    }
}
