using UnityEngine;

public sealed class CampusRecoveryService : MonoBehaviour
{
    private float nextCheck;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + 1f;
        AdmissionLetterGuide.Instance?.RecoverIfInvalid();
    }
}
