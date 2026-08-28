using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class CampusLocationDefinition
{
    public string id;
    public string displayName;
    public string description;
    public int sceneIndex;
    public Vector3 navigationTarget;
    public string category;
    public bool grantsStamp;
}

public sealed class CampusLocationRegistry : MonoBehaviour
{
    public static CampusLocationRegistry Instance { get; private set; }

    private readonly Dictionary<string, CampusLocationDefinition> locations =
        new Dictionary<string, CampusLocationDefinition>();

    public IEnumerable<CampusLocationDefinition> All => locations.Values;

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

    public void Register(CampusLocationDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.id))
            return;
        locations[definition.id] = definition;
    }

    public bool TryGet(string id, out CampusLocationDefinition definition)
    {
        return locations.TryGetValue(id ?? string.Empty, out definition);
    }
}
