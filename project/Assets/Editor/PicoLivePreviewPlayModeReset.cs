using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Management;

/// <summary>
/// PICO Live Preview owns a native streaming session. Explicitly release that
/// session after Play Mode so the next Play does not inherit a stale loader.
/// </summary>
[InitializeOnLoad]
public static class PicoLivePreviewPlayModeReset
{
    static PicoLivePreviewPlayModeReset()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        var settings = XRGeneralSettings.Instance;
        var manager = settings != null ? settings.Manager : null;
        if (manager == null)
            return;

        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            manager.StopSubsystems();
        }
        else if (state == PlayModeStateChange.EnteredEditMode && manager.activeLoader != null)
        {
            manager.DeinitializeLoader();
            Debug.Log("PICO Live Preview XR session released for the next Play.");
        }
    }
}
