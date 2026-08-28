using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using Unity.XR.CoreUtils;

[RequireComponent(typeof(XROrigin))]
public class PicoControllerVisuals : MonoBehaviour
{
    private static readonly Color RayColor = new Color(0.15f, 0.85f, 1f, 0.95f);

    [SerializeField] private float rayLength = 12f;
    [SerializeField] private LayerMask rayMask = ~0;

    private Transform leftController;
    private Transform rightController;
    private LineRenderer leftRay;
    private LineRenderer rightRay;
    private Material leftMaterial;
    private Material rightMaterial;
    private Material rayMaterial;

    private void Awake()
    {
        var origin = GetComponent<XROrigin>();
        var parent = origin.CameraFloorOffsetObject != null
            ? origin.CameraFloorOffsetObject.transform
            : transform;

        rayMaterial = LoadMaterial("PICO/ControllerRay", RayColor);
        leftMaterial = LoadMaterial(
            "PICO/ControllerLeft", new Color(0.12f, 0.45f, 0.95f, 1f));
        rightMaterial = LoadMaterial(
            "PICO/ControllerRight", new Color(0.95f, 0.35f, 0.18f, 1f));

        leftController = CreateController(
            parent, "Left PICO Controller", "LeftHand", leftMaterial, out leftRay);
        rightController = CreateController(
            parent, "Right PICO Controller", "RightHand", rightMaterial, out rightRay);
    }

    private void LateUpdate()
    {
        var letterInLeftHand = AdmissionLetterGuide.Instance != null &&
                               AdmissionLetterGuide.Instance.IsHeld;
        UpdateRay(leftController, leftRay,
            !letterInLeftHand && !ExhibitGrabService.IsHandBusy(-1));
        UpdateRay(rightController, rightRay, !ExhibitGrabService.IsHandBusy(1));
    }

    private Transform CreateController(
        Transform parent,
        string objectName,
        string handUsage,
        Material material,
        out LineRenderer ray)
    {
        var root = new GameObject(objectName);
        root.transform.SetParent(parent, false);

        var driver = root.AddComponent<TrackedPoseDriver>();
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        driver.positionInput = Action(
            objectName + " Position",
            $"<XRController>{{{handUsage}}}/devicePosition",
            "Vector3");
        driver.rotationInput = Action(
            objectName + " Rotation",
            $"<XRController>{{{handUsage}}}/deviceRotation",
            "Quaternion");
        driver.trackingStateInput = Action(
            objectName + " Tracking State",
            $"<XRController>{{{handUsage}}}/trackingState",
            "Integer");
        driver.ignoreTrackingState = false;

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Controller Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0f, -0.025f, -0.015f);
        body.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
        body.transform.localScale = new Vector3(0.045f, 0.075f, 0.045f);
        body.GetComponent<Renderer>().material = material;
        Destroy(body.GetComponent<Collider>());

        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Controller Head";
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = new Vector3(0f, 0.015f, 0.035f);
        head.transform.localScale = new Vector3(0.075f, 0.035f, 0.09f);
        head.GetComponent<Renderer>().material = material;
        Destroy(head.GetComponent<Collider>());

        ray = root.AddComponent<LineRenderer>();
        ray.useWorldSpace = true;
        ray.positionCount = 2;
        ray.startWidth = 0.008f;
        ray.endWidth = 0.0025f;
        ray.material = rayMaterial;
        ray.startColor = ray.endColor = RayColor;

        return root.transform;
    }

    private void UpdateRay(Transform controller, LineRenderer ray, bool visible)
    {
        if (controller == null || ray == null)
            return;

        ray.enabled = visible;
        if (!visible)
            return;

        var start = controller.position;
        var direction = controller.forward;
        var end = Physics.Raycast(
            start, direction, out var hit, rayLength, rayMask,
            QueryTriggerInteraction.Collide)
            ? hit.point
            : start + direction * rayLength;

        ray.SetPosition(0, start);
        ray.SetPosition(1, end);
    }

    private static InputActionProperty Action(
        string name, string binding, string expectedControlType)
    {
        return new InputActionProperty(new InputAction(
            name, InputActionType.Value, binding,
            expectedControlType: expectedControlType));
    }

    private static Material LoadMaterial(string resourcePath, Color fallbackColor)
    {
        var savedMaterial = Resources.Load<Material>(resourcePath);
        if (savedMaterial != null)
            return savedMaterial;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        var material = new Material(shader);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", fallbackColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", fallbackColor);
        return material;
    }
}
