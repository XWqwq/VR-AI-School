using UnityEngine;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;

[RequireComponent(typeof(XROrigin))]
[RequireComponent(typeof(CharacterController))]
public class PicoLocomotion : MonoBehaviour
{
    [Header("Continuous locomotion")]
    [SerializeField] private float moveSpeed = 1.4f;
    [SerializeField] private float turnSpeed = 75f;
    [SerializeField] private bool enableContinuousMovement = true;
    [SerializeField] private bool useSnapTurn = true;
    [SerializeField] private float snapTurnAngle = 45f;
    [SerializeField] private bool useGravity = false;
    [SerializeField] private float gravity = -9.81f;

    [Header("Teleport")]
    [SerializeField] private float teleportDistance = 20f;
    [SerializeField] private LayerMask teleportMask = ~0;

    private XROrigin origin;
    private CharacterController character;
    private InputAction moveAction;
    private InputAction turnAction;
    private InputAction teleportAction;
    private InputAction rightPositionAction;
    private InputAction rightRotationAction;
    private LineRenderer teleportLine;
    private float verticalVelocity;
    private bool snapTurnReady = true;
    private bool teleportWasPressed;
    private bool hasTeleportTarget;
    private Vector3 teleportTarget;
    private float fixedPlayerY;
    private const int TeleportArcSegments = 24;

    public bool ContinuousMovementEnabled => enableContinuousMovement;

    public void SetFixedHeight(float worldY)
    {
        fixedPlayerY = worldY;
        LockVerticalPosition();
    }

    public void SetComfortMode(bool enabled)
    {
        enableContinuousMovement = !enabled;
        useSnapTurn = true;
    }

    private void Awake()
    {
        origin = GetComponent<XROrigin>();
        character = GetComponent<CharacterController>();
        fixedPlayerY = transform.position.y;
        CreateTeleportLine();

#if UNITY_EDITOR
        QualitySettings.SetQualityLevel(3, true);
        QualitySettings.shadowDistance = 35f;
        UnityEngine.XR.XRSettings.eyeTextureResolutionScale = 0.85f;
#endif
#if UNITY_ANDROID && !UNITY_EDITOR
        // Keep model/world proportions unchanged while reducing the standalone
        // headset pixel workload enough to avoid sustained 99% PICO GPU load.
        UnityEngine.XR.XRSettings.eyeTextureResolutionScale = 0.85f;
#endif
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 72;
    }

    private void OnEnable()
    {
        moveAction = CreateValueAction("Move", "<XRController>{LeftHand}/primary2DAxis", "Vector2");
        turnAction = CreateValueAction("Turn", "<XRController>{RightHand}/primary2DAxis", "Vector2");
        teleportAction = new InputAction(
            "Teleport", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
        rightPositionAction = CreateValueAction(
            "Right Controller Position", "<XRController>{RightHand}/devicePosition", "Vector3");
        rightRotationAction = CreateValueAction(
            "Right Controller Rotation", "<XRController>{RightHand}/deviceRotation", "Quaternion");

        moveAction.Enable();
        turnAction.Enable();
        teleportAction.Enable();
        rightPositionAction.Enable();
        rightRotationAction.Enable();
    }

    private void OnDisable()
    {
        moveAction?.Dispose();
        turnAction?.Dispose();
        teleportAction?.Dispose();
        rightPositionAction?.Dispose();
        rightRotationAction?.Dispose();
    }

    private void Update()
    {
        UpdateCharacterCapsule();
        if (CampusInteractionCoordinator.Instance != null &&
            CampusInteractionCoordinator.Instance.BlocksLocomotion)
        {
            teleportLine.enabled = false;
            return;
        }
        ApplyContinuousMovement();
        ApplyContinuousTurn();
        UpdateTeleport();
        LockVerticalPosition();
    }

    private void ApplyContinuousMovement()
    {
        if (!enableContinuousMovement)
            return;
        var input = moveAction.ReadValue<Vector2>();
        var cameraTransform = origin.Camera.transform;
        var forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        var right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        var motion = (forward * input.y + right * input.x) * moveSpeed;

        if (useGravity)
        {
            verticalVelocity = character.isGrounded
                ? -0.5f
                : verticalVelocity + gravity * Time.deltaTime;
            motion.y = verticalVelocity;
        }
        else
        {
            verticalVelocity = 0f;
            motion.y = 0f;
        }
        character.Move(motion * Time.deltaTime);
    }

    private void ApplyContinuousTurn()
    {
        var turn = turnAction.ReadValue<Vector2>().x;
        if (useSnapTurn)
        {
            if (Mathf.Abs(turn) < 0.35f)
            {
                snapTurnReady = true;
                return;
            }
            if (Mathf.Abs(turn) >= 0.7f && snapTurnReady)
            {
                var snapHeadPosition = origin.Camera.transform.position;
                transform.RotateAround(snapHeadPosition, Vector3.up, Mathf.Sign(turn) * snapTurnAngle);
                snapTurnReady = false;
            }
            return;
        }
        if (Mathf.Abs(turn) < 0.15f)
            return;

        var head = origin.Camera.transform.position;
        transform.RotateAround(head, Vector3.up, turn * turnSpeed * Time.deltaTime);
    }

    private void UpdateTeleport()
    {
        var localPosition = rightPositionAction.ReadValue<Vector3>();
        var localRotation = rightRotationAction.ReadValue<Quaternion>();
        var rayOrigin = transform.TransformPoint(localPosition);
        var rayDirection = transform.rotation * localRotation * Vector3.forward;
        var pressed = teleportAction.IsPressed();

        if (pressed)
        {
            teleportLine.enabled = true;
            hasTeleportTarget = CalculateTeleportArc(rayOrigin, rayDirection, out teleportTarget);
            teleportLine.startColor = teleportLine.endColor = hasTeleportTarget
                ? new Color(0.15f, 1f, 0.65f, 0.9f)
                : Color.red;
        }
        else
        {
            teleportLine.enabled = false;
            if (teleportWasPressed && hasTeleportTarget)
                TeleportTo(teleportTarget);
            hasTeleportTarget = false;
        }

        teleportWasPressed = pressed;
    }

    private bool CalculateTeleportArc(Vector3 originPosition, Vector3 forward, out Vector3 target)
    {
        var velocity = (forward.normalized + Vector3.up * 0.18f).normalized * 9f;
        var previous = originPosition;
        teleportLine.positionCount = TeleportArcSegments;
        teleportLine.SetPosition(0, previous);
        target = previous;

        for (var i = 1; i < TeleportArcSegments; i++)
        {
            var time = i * 0.09f;
            var current = originPosition + velocity * time +
                          Vector3.up * (-9.81f * 0.5f * time * time);
            if (Physics.Linecast(previous, current, out var hit, teleportMask,
                    QueryTriggerInteraction.Ignore))
            {
                target = hit.point;
                teleportLine.positionCount = i + 1;
                teleportLine.SetPosition(i, target);
                return Vector3.Dot(hit.normal, Vector3.up) >= 0.55f;
            }
            teleportLine.SetPosition(i, current);
            previous = current;
        }
        return false;
    }

    private void TeleportTo(Vector3 destination)
    {
        var head = origin.Camera.transform.position;
        var headOffset = Vector3.ProjectOnPlane(head - transform.position, Vector3.up);
        character.enabled = false;
        var position = destination - headOffset;
        position.y = fixedPlayerY;
        transform.position = position;
        character.enabled = true;
        verticalVelocity = 0f;
    }

    private void LockVerticalPosition()
    {
        var position = transform.position;
        if (Mathf.Approximately(position.y, fixedPlayerY)) return;
        character.enabled = false;
        position.y = fixedPlayerY;
        transform.position = position;
        character.enabled = true;
    }

    private void UpdateCharacterCapsule()
    {
        var headLocal = transform.InverseTransformPoint(origin.Camera.transform.position);
        character.height = Mathf.Clamp(headLocal.y, 1f, 2.2f);
        character.center = new Vector3(headLocal.x, character.height * 0.5f, headLocal.z);
        character.radius = 0.22f;
    }

    private void CreateTeleportLine()
    {
        teleportLine = gameObject.AddComponent<LineRenderer>();
        teleportLine.positionCount = TeleportArcSegments;
        teleportLine.useWorldSpace = true;
        teleportLine.startWidth = 0.012f;
        teleportLine.endWidth = 0.006f;
        teleportLine.material = new Material(Shader.Find("Sprites/Default"));
        teleportLine.enabled = false;
    }

    private static InputAction CreateValueAction(string name, string binding, string controlType)
    {
        return new InputAction(name, InputActionType.Value, binding, expectedControlType: controlType);
    }
}
