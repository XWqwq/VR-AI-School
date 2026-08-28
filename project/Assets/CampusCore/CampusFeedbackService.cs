using UnityEngine;
using UnityEngine.XR;

public sealed class CampusFeedbackService : MonoBehaviour
{
    public static CampusFeedbackService Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Hover(bool rightHand = true) => Pulse(rightHand, 0.12f, 0.025f);
    public void Confirm(bool rightHand = true) => Pulse(rightHand, 0.32f, 0.055f);
    public void Error(bool rightHand = true) => Pulse(rightHand, 0.5f, 0.12f);
    public void TaskComplete()
    {
        Pulse(false, 0.35f, 0.08f);
        Pulse(true, 0.35f, 0.08f);
    }

    private static void Pulse(bool rightHand, float amplitude, float duration)
    {
        var device = InputDevices.GetDeviceAtXRNode(
            rightHand ? XRNode.RightHand : XRNode.LeftHand);
        if (device.isValid)
            device.SendHapticImpulse(0u, amplitude, duration);
    }
}
