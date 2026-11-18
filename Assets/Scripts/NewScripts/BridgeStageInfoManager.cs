using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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
    [SerializeField, Tooltip("Enable only on the Stage02 tutorial scene instance.")]
    private bool useBridgeGuidance;
    private bool showTutorialHud = true;
    private static readonly List<BridgeStageInfoManager> activeManagers = new List<BridgeStageInfoManager>();
    private Coroutine bindRoutine;

    public bool UsesBridgeGuidance => useBridgeGuidance;
    public bool ShowTutorialHud => showTutorialHud;
    public event Action PresentationPreferenceChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => activeManagers.Clear();

    public static BridgeStageInfoManager GetGuidanceForScene(Scene scene)
    {
        foreach (BridgeStageInfoManager manager in activeManagers)
            if (manager != null && manager.isActiveAndEnabled && manager.useBridgeGuidance && manager.gameObject.scene == scene)
                return manager;
        return null;
    }

    public static BridgeStageInfoManager GetCurrentGuidance()
    {
        GameplayManager current = GameplayManager.Instance;
        return GetGuidanceForScene(current != null ? current.gameObject.scene : SceneManager.GetActiveScene());
    }

    public void ConfigureBridgeGuidance(bool enabled)
    {
        if (useBridgeGuidance == enabled) return;
        useBridgeGuidance = enabled;
        PresentationPreferenceChanged?.Invoke();
    }

    public void SetTutorialHudVisible(bool visible)
    {
        if (showTutorialHud == visible) return;
        showTutorialHud = visible;
        PresentationPreferenceChanged?.Invoke();
    }

    private readonly HashSet<MessageKey> shownMessages = new HashSet<MessageKey>();
    private GameplayManager gameplayManager;

    public static event Action<string, string> MessageRequested;

    private void OnEnable()
    {
        if (!activeManagers.Contains(this)) activeManagers.Add(this);
        bindRoutine = StartCoroutine(BindGameplayManager());
    }

    private IEnumerator BindGameplayManager()
    {
        while (GameplayManager.Instance == null)
        {
            yield return null;
        }

        gameplayManager = GameplayManager.Instance;
        gameplayManager.OnConstructionStageChanged += GameplayManager_OnConstructionStageChanged;
        bindRoutine = null;
    }

    private void OnDisable()
    {
        activeManagers.Remove(this);
        if (bindRoutine != null) StopCoroutine(bindRoutine);
        bindRoutine = null;
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
        if (useBridgeGuidance) return;
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
