using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

public static class PicoLivePreviewRecovery
{
#if UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void StartRecoveryCheck()
    {
        var host = new GameObject("PICO Live Preview Startup Check");
        Object.DontDestroyOnLoad(host);
        host.hideFlags = HideFlags.HideInHierarchy;
        host.AddComponent<RecoveryRunner>();
    }

    private sealed class RecoveryRunner : MonoBehaviour
    {
        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(1.25f);

            if (HasRunningDisplay())
            {
                Destroy(gameObject);
                yield break;
            }

            XRManagerSettings manager = null;
            float deadline = Time.realtimeSinceStartup + 8f;
            while (manager == null && Time.realtimeSinceStartup < deadline)
            {
                var settings = XRGeneralSettings.Instance;
                manager = settings != null ? settings.Manager : null;
                if (manager == null)
                    yield return new WaitForSecondsRealtime(0.25f);
            }

            if (manager == null)
            {
                Debug.LogError(
                    "PICO Live Preview recovery: XR Manager did not become available within 8 seconds.");
                Destroy(gameObject);
                yield break;
            }

            Debug.LogWarning(
                "PICO Live Preview display did not start on the first Play; retrying XR initialization.");

            manager.StopSubsystems();
            manager.DeinitializeLoader();
            yield return null;
            yield return manager.InitializeLoader();

            if (manager.activeLoader != null)
                manager.StartSubsystems();
            else
                Debug.LogError("PICO Live Preview recovery failed to initialize the XR loader.");

            Destroy(gameObject);
        }

        private static bool HasRunningDisplay()
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetInstances(displays);
            foreach (var display in displays)
            {
                if (display.running)
                    return true;
            }
            return false;
        }
    }
#endif
}
