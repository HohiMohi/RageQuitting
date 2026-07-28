using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class BridgeStageInfoEntry
{
    public BridgeComponentSO componentType;
    public BridgeConstructionStage stage;
    public string title;
    [TextArea(2, 6)] public string message;
}

public class BridgeStageInfoManager : MonoBehaviour
{
    private readonly struct MessageKey : IEquatable<MessageKey>
    {
        private readonly int componentTypeId;
        private readonly BridgeConstructionStage stage;

        public MessageKey(BridgeComponentSO componentType, BridgeConstructionStage stage)
        {
            componentTypeId = componentType != null ? componentType.GetInstanceID() : 0;
            this.stage = stage;
        }

        public bool Equals(MessageKey other)
        {
            return componentTypeId == other.componentTypeId && stage == other.stage;
        }

        public override bool Equals(object obj)
        {
            return obj is MessageKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(componentTypeId, (int)stage);
        }
    }

    [SerializeField] private BridgeStageInfoEntry[] entries = Array.Empty<BridgeStageInfoEntry>();

    private readonly HashSet<MessageKey> shownMessages = new HashSet<MessageKey>();
    private GameplayManager gameplayManager;

    public static event Action<string, string> MessageRequested;

    private IEnumerator Start()
    {
        while (GameplayManager.Instance == null)
        {
            yield return null;
        }

        gameplayManager = GameplayManager.Instance;
        gameplayManager.OnConstructionStageChanged += GameplayManager_OnConstructionStageChanged;
    }

    private void OnDisable()
    {
        if (gameplayManager != null)
        {
            gameplayManager.OnConstructionStageChanged -= GameplayManager_OnConstructionStageChanged;
            gameplayManager = null;
        }
    }

    private void GameplayManager_OnConstructionStageChanged(
        object sender,
        BridgeConstructionStageChangedEventArgs e)
    {
        BridgeComponentSO componentType = e.Component != null
            ? e.Component.GetBridgeComponentSO()
            : null;
        if (componentType == null)
        {
            return;
        }

        MessageKey key = new MessageKey(componentType, e.CurrentStage);
        if (shownMessages.Contains(key) ||
            !TryGetEntry(componentType, e.CurrentStage, out BridgeStageInfoEntry entry))
        {
            return;
        }

        shownMessages.Add(key);
        MessageRequested?.Invoke(entry.title, entry.message);
    }

    private bool TryGetEntry(
        BridgeComponentSO componentType,
        BridgeConstructionStage stage,
        out BridgeStageInfoEntry result)
    {
        if (entries != null)
        {
            foreach (BridgeStageInfoEntry entry in entries)
            {
                if (entry != null
                    && entry.componentType == componentType
                    && entry.stage == stage
                    && (!string.IsNullOrWhiteSpace(entry.title)
                        || !string.IsNullOrWhiteSpace(entry.message)))
                {
                    result = entry;
                    return true;
                }
            }
        }

        result = null;
        return false;
    }
}
