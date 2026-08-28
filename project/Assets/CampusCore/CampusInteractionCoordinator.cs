using System;
using UnityEngine;

public enum CampusInteractionMode
{
    Exploration,
    Menu,
    Listening,
    Thinking,
    Speaking,
    Guiding,
    Loading,
    Tutorial
}

public sealed class CampusInteractionCoordinator : MonoBehaviour
{
    public static CampusInteractionCoordinator Instance { get; private set; }

    public event Action<CampusInteractionMode, CampusInteractionMode> ModeChanged;

    public CampusInteractionMode Mode { get; private set; } = CampusInteractionMode.Exploration;
    public bool BlocksLocomotion => Mode == CampusInteractionMode.Loading ||
                                    Mode == CampusInteractionMode.Tutorial;
    public bool AllowsWorldInteraction => Mode == CampusInteractionMode.Exploration ||
                                          Mode == CampusInteractionMode.Guiding;
    public bool AllowsMenuInteraction => Mode == CampusInteractionMode.Menu ||
                                         Mode == CampusInteractionMode.Listening ||
                                         Mode == CampusInteractionMode.Thinking ||
                                         Mode == CampusInteractionMode.Speaking;
    public bool AllowsSceneShortcuts => Mode == CampusInteractionMode.Exploration ||
                                        Mode == CampusInteractionMode.Guiding;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool TrySetMode(CampusInteractionMode next)
    {
        if (Mode == CampusInteractionMode.Loading && next != CampusInteractionMode.Exploration)
            return false;
        SetMode(next);
        return true;
    }

    public void ForceSetMode(CampusInteractionMode next)
    {
        SetMode(next);
    }

    private void SetMode(CampusInteractionMode next)
    {
        if (Mode == next)
            return;
        var previous = Mode;
        Mode = next;
        ModeChanged?.Invoke(previous, next);
    }
}
