using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum CampusDiscoveryState
{
    Locked,
    Discovered,
    Completed
}

[Serializable]
public sealed class CampusLocationProgress
{
    public string locationId;
    public CampusDiscoveryState state;
    public bool stampAwarded;
    public long firstDiscoveredUtc;
}

[Serializable]
public sealed class CampusProgressData
{
    public int version = 2;
    public string studentName = string.Empty;
    public string studentMajor = string.Empty;
    public int lastSceneIndex;
    public int questionCount;
    public bool tutorialCompleted;
    public List<int> visitedScenes = new List<int>();
    public List<string> completedTaskIds = new List<string>();
    public List<string> completedLocationIds = new List<string>();
    public List<string> unlockedSecretIds = new List<string>();
    public List<CampusLocationProgress> locations = new List<CampusLocationProgress>();
}

public sealed class CampusProgressService : MonoBehaviour
{
    public static CampusProgressService Instance { get; private set; }

    public event Action ProgressChanged;
    public event Action<string, CampusDiscoveryState> LocationStateChanged;
    public event Action<string> StampAwarded;
    public event Action<string> SecretUnlocked;

    public CampusProgressData Data { get; private set; } = new CampusProgressData();
    public string SavePath => Path.Combine(Application.persistentDataPath, "campus_progress.json");
    public bool IsShowcaseMode { get; private set; }
    private string personalProgressSnapshot;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Save();
            Instance = null;
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Data.lastSceneIndex = scene.buildIndex;
        if (!Data.visitedScenes.Contains(scene.buildIndex))
            Data.visitedScenes.Add(scene.buildIndex);
        SaveAndNotify();
    }

    public CampusDiscoveryState GetLocationState(string locationId)
    {
        var entry = FindLocation(locationId);
        return entry != null ? entry.state : CampusDiscoveryState.Locked;
    }

    public void DiscoverLocation(string locationId)
    {
        SetLocationState(locationId, CampusDiscoveryState.Discovered);
    }

    public void CompleteLocation(string locationId)
    {
        SetLocationState(locationId, CampusDiscoveryState.Completed);
        if (CampusLocationRegistry.Instance != null &&
            CampusLocationRegistry.Instance.TryGet(locationId, out var definition) &&
            definition.grantsStamp)
            AwardStamp(locationId);
    }

    public void AwardStamp(string locationId)
    {
        var entry = GetOrCreateLocation(locationId);
        if (entry.stampAwarded)
            return;
        entry.stampAwarded = true;
        StampAwarded?.Invoke(locationId);
        CampusTaskService.Instance?.Report(CampusTaskTrigger.CollectStamp, locationId);
        SaveAndNotify();
    }

    public bool HasStamp(string locationId)
    {
        var entry = FindLocation(locationId);
        return entry != null && entry.stampAwarded;
    }

    public bool HasSecret(string secretId)
    {
        return Data.unlockedSecretIds != null && Data.unlockedSecretIds.Contains(secretId);
    }

    public void UnlockSecret(string secretId)
    {
        if (string.IsNullOrWhiteSpace(secretId) || HasSecret(secretId))
            return;
        if (Data.unlockedSecretIds == null)
            Data.unlockedSecretIds = new List<string>();
        Data.unlockedSecretIds.Add(secretId);
        SecretUnlocked?.Invoke(secretId);
        SaveAndNotify();
    }

    public void IncrementQuestionCount()
    {
        Data.questionCount++;
        SaveAndNotify();
    }

    public void CompleteTask(string taskId)
    {
        if (string.IsNullOrEmpty(taskId) || Data.completedTaskIds.Contains(taskId))
            return;
        Data.completedTaskIds.Add(taskId);
        SaveAndNotify();
    }

    public void ResetTutorialProgress()
    {
        Data.tutorialCompleted = false;
        Data.completedTaskIds.RemoveAll(id => id != null && id.StartsWith("tutorial_"));
        SaveAndNotify();
    }

    public bool IsTaskCompleted(string taskId)
    {
        return Data.completedTaskIds.Contains(taskId);
    }

    public void SetTutorialCompleted(bool completed)
    {
        Data.tutorialCompleted = completed;
        SaveAndNotify();
    }

    public void ClearAllPersonalProgress()
    {
        Data = new CampusProgressData();
        SaveAndNotify();
    }

    public void Save()
    {
        if (IsShowcaseMode)
            return;
        try
        {
            File.WriteAllText(SavePath, JsonUtility.ToJson(Data, true));
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Campus progress save failed: " + exception.Message);
        }
    }

    public void BeginShowcaseMode()
    {
        if (IsShowcaseMode)
            return;
        personalProgressSnapshot = JsonUtility.ToJson(Data);
        IsShowcaseMode = true;
        ResetShowcaseRound();
    }

    public void ResetShowcaseRound()
    {
        if (!IsShowcaseMode)
            return;
        Data = new CampusProgressData();
        ProgressChanged?.Invoke();
    }

    public void EndShowcaseMode()
    {
        if (!IsShowcaseMode)
            return;
        IsShowcaseMode = false;
        if (!string.IsNullOrEmpty(personalProgressSnapshot))
        {
            var restored = JsonUtility.FromJson<CampusProgressData>(personalProgressSnapshot);
            if (restored != null)
                Data = restored;
        }
        personalProgressSnapshot = null;
        SaveAndNotify();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(SavePath))
                return;
            var loaded = JsonUtility.FromJson<CampusProgressData>(File.ReadAllText(SavePath));
            if (loaded != null)
            {
                Data = loaded;
                EnsureCollections();
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Campus progress load failed; starting fresh: " + exception.Message);
            Data = new CampusProgressData();
        }
    }

    private void SetLocationState(string locationId, CampusDiscoveryState next)
    {
        if (string.IsNullOrWhiteSpace(locationId))
            return;
        var entry = GetOrCreateLocation(locationId);
        if (entry.state >= next)
            return;
        entry.state = next;
        if (next == CampusDiscoveryState.Completed)
        {
            if (Data.completedLocationIds == null)
                Data.completedLocationIds = new List<string>();
            if (!Data.completedLocationIds.Contains(locationId))
                Data.completedLocationIds.Add(locationId);
        }
        if (entry.firstDiscoveredUtc == 0)
            entry.firstDiscoveredUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        LocationStateChanged?.Invoke(locationId, next);
        SaveAndNotify();
    }

    private CampusLocationProgress GetOrCreateLocation(string id)
    {
        var entry = FindLocation(id);
        if (entry != null)
            return entry;
        entry = new CampusLocationProgress { locationId = id };
        Data.locations.Add(entry);
        return entry;
    }

    private void EnsureCollections()
    {
        if (Data.visitedScenes == null) Data.visitedScenes = new List<int>();
        if (Data.completedTaskIds == null) Data.completedTaskIds = new List<string>();
        if (Data.completedLocationIds == null) Data.completedLocationIds = new List<string>();
        if (Data.unlockedSecretIds == null) Data.unlockedSecretIds = new List<string>();
        if (Data.locations == null) Data.locations = new List<CampusLocationProgress>();
    }

    private CampusLocationProgress FindLocation(string id)
    {
        return Data.locations.Find(item => item.locationId == id);
    }

    private void SaveAndNotify()
    {
        Save();
        ProgressChanged?.Invoke();
    }
}
