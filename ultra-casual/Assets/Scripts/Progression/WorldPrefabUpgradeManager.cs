using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Generic manager that swaps prefabs under given roots based on UpgradeSystem steps.
/// Configure multiple slots, each mapped to a specific UpgradeType and prefab field name(s).
/// Listens to UpgradeSystem.OnReachedNextStep and refreshes only the affected slots.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Upgrades/World Prefab Upgrade Manager")]
public class WorldPrefabUpgradeManager : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("If left empty, will use UpgradeSystem.Instance.")]
    public UpgradeSystem upgradeSystem;

    [Header("Behavior")]
    [Tooltip("Refresh all slots on Start().")]
    public bool refreshOnStart = true;

    [Tooltip("If true, a slot will NOT respawn if the resolved prefab is the same as currently spawned.")]
    public bool skipRespawnIfSamePrefab = true;

    [Serializable]
    public class Slot
    {
        [Tooltip("Which upgrade type drives this slot's prefab.")]
        public UpgradeType upgradeType = default;

        [Tooltip("Parent transform where the resolved prefab will be spawned.")]
        public Transform targetRoot;

        [Tooltip("Optional: local position applied to the spawned prefab.")]
        public Vector3 spawnLocalPosition = Vector3.zero;

        [Tooltip("Optional: local rotation applied to the spawned prefab.")]
        public Vector3 spawnLocalEuler = Vector3.zero;

        [Tooltip("Optional: local scale applied to the spawned prefab.")]
        public Vector3 spawnLocalScale = Vector3.one;

        [Tooltip("Names to look up inside the StepData to extract the prefab. Checked as field, then property. First match wins.")]
        public List<string> prefabFieldCandidates = new List<string> { "prefab", "rampPrefab" };

        [Tooltip("If true, clears all children in targetRoot before spawning. If false, only removes what this slot spawned.")]
        public bool hardClearRoot = true;

        // Runtime
        [NonSerialized] public GameObject spawnedInstance;
        [NonSerialized] public GameObject lastPrefabRef;
    }

    [Header("Slots")]
    public List<Slot> slots = new();

    private void Awake()
    {
        if (upgradeSystem == null)
        {
            upgradeSystem = UpgradeSystem.Instance;
        }
    }

    private void OnEnable()
    {
        if (upgradeSystem != null)
        {
            // Expected signature: Action<UpgradeType, int level, int stepIndex, object stepData>
            // If your event signature differs, add or adjust an overload below.
            upgradeSystem.OnReachedNextStep += OnReachedNextStep;
        }
        else
        {
            Debug.LogWarning("[WorldPrefabUpgradeManager] No UpgradeSystem assigned or found.", this);
        }
    }

    private void Start()
    {
        if (refreshOnStart)
        {
            RefreshAll();
        }
    }

    private void OnDisable()
    {
        if (upgradeSystem != null)
        {
            upgradeSystem.OnReachedNextStep -= OnReachedNextStep;
        }
    }

    // -------- Public Controls --------

    [ContextMenu("Refresh All")]
    public void RefreshAll()
    {
        if (upgradeSystem == null) return;

        foreach (var slot in slots)
        {
            RefreshSlot(slot);
        }
    }

    public void RefreshByType(UpgradeType type)
    {
        if (upgradeSystem == null) return;

        foreach (var slot in slots)
        {
            if (slot.upgradeType.Equals(type))
            {
                RefreshSlot(slot);
            }
        }
    }

    public void ClearAll()
    {
        foreach (var slot in slots)
        {
            ClearSlot(slot);
        }
    }

    // -------- Event Handlers --------

    private void OnReachedNextStep(UpgradeType type, int stepIndex)
    {
        // Refresh only affected slots.
        foreach (var slot in slots)
        {
            if (!slot.upgradeType.Equals(type)) continue;
            UpgradeStepData stepData = upgradeSystem.GetCurrentStepData(type);
            SpawnForSlotAndStepData(slot, stepData);
        }
    }

    // If your UpgradeSystem event has a different signature (e.g., only UpgradeType),
    // add another handler and wire it in OnEnable instead:
    // private void OnReachedNextStep(UpgradeType type)
    // {
    //     RefreshByType(type);
    // }

    // -------- Core --------

    private void RefreshSlot(Slot slot)
    {
        if (slot == null) return;

        UpgradeStepData stepData = upgradeSystem.GetCurrentStepData(slot.upgradeType);
        if (stepData == null)
        {
            Debug.LogWarning($"[WorldPrefabUpgradeManager] Could not retrieve current StepData for '{slot.upgradeType}'.", this);
            return;
        }

        SpawnForSlotAndStepData(slot, stepData);
    }

    private void SpawnForSlotAndStepData(Slot slot, UpgradeStepData stepData)
    {
        if (slot == null || slot.targetRoot == null) return;

        GameObject prefab = stepData.worldPrefab;
        if (prefab == null)
        {
            Debug.LogWarning($"[WorldPrefabUpgradeManager] StepData for '{slot.upgradeType}' has no prefab in fields/properties: [{string.Join(", ", slot.prefabFieldCandidates)}].", this);
            return;
        }

        if (skipRespawnIfSamePrefab && slot.lastPrefabRef == prefab && slot.spawnedInstance != null)
        {
            return; // nothing to do
        }

        if (slot.hardClearRoot)
        {
            HardClearRoot(slot);
        }
        else
        {
            SoftClearSpawn(slot); // remove only what we spawned before
        }

        var go = Instantiate(prefab, slot.targetRoot);
        var t = go.transform;
        t.localPosition = slot.spawnLocalPosition;
        t.localRotation = Quaternion.Euler(slot.spawnLocalEuler);
        t.localScale = Vector3.one;

        slot.spawnedInstance = go;
        slot.lastPrefabRef = prefab;
    }

    private void ClearSlot(Slot slot)
    {
        if (slot == null || slot.targetRoot == null) return;

        if (slot.hardClearRoot)
        {
            HardClearRoot(slot);
        }
        else
        {
            SoftClearSpawn(slot);
        }
    }

    private static void SoftClearSpawn(Slot slot)
    {
        if (slot.spawnedInstance == null) return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            DestroyImmediate(slot.spawnedInstance);
        }
        else
#endif
        {
            Destroy(slot.spawnedInstance);
        }

        slot.spawnedInstance = null;
        slot.lastPrefabRef = null;
    }

    private static void HardClearRoot(Slot slot)
    {
        if (slot.targetRoot == null) return;

        for (int i = slot.targetRoot.childCount - 1; i >= 0; i--)
        {
            var child = slot.targetRoot.GetChild(i);
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(child.gameObject);
            }
            else
#endif
            {
                Destroy(child.gameObject);
            }
        }

        slot.spawnedInstance = null;
        slot.lastPrefabRef = null;
    }



}
