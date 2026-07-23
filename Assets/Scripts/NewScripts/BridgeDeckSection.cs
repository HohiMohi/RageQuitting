using System.Collections.Generic;
using UnityEngine;

public class BridgeDeckSection : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float sectionLength = 14f;
    [SerializeField, Min(0f)] private float panelGap = 0.04f;
    [SerializeField, Min(0.1f)] private float nominalPanelLength = 2f;
    [SerializeField] private BridgeComponent[] prerequisites;
    [SerializeField] private BridgeDeckPanelConstructionSite[] panelSlots;

    private int lastUnlockedIndex = -2;

    public float SectionLength => sectionLength;
    public float PanelGap => panelGap;
    public IReadOnlyList<BridgeDeckPanelConstructionSite> PanelSlots => panelSlots;

    private void Awake()
    {
        ConfigureSlots();
        RefreshLayout();
        RefreshUnlockState(true);
    }

    private void Update()
    {
        RefreshUnlockState(false);
    }

    public bool IsSlotUnlocked(int index)
    {
        if (panelSlots == null || index < 0 || index >= panelSlots.Length || !ArePrerequisitesComplete())
        {
            return false;
        }

        return index == 0 || (panelSlots[index - 1] != null && panelSlots[index - 1].IsComplete);
    }

    public void RefreshLayout()
    {
        int count = panelSlots?.Length ?? 0;
        if (count == 0) return;

        float availableLength = sectionLength - panelGap * (count - 1);
        if (availableLength <= 0f)
        {
            Debug.LogWarning($"{name}: section length is too short for {count} panel slots and configured gaps.", this);
            return;
        }

        float panelLength = availableLength / count;
        float stride = panelLength + panelGap;
        float start = -sectionLength * 0.5f + panelLength * 0.5f;
        for (int i = 0; i < count; i++)
        {
            BridgeDeckPanelConstructionSite slot = panelSlots[i];
            if (slot == null) continue;
            Transform slotTransform = slot.transform;
            slotTransform.localPosition = new Vector3(start + stride * i, slotTransform.localPosition.y, slotTransform.localPosition.z);
            slot.SetLayoutLength(panelLength, nominalPanelLength);
        }
    }

    private void ConfigureSlots()
    {
        if (panelSlots == null) return;
        for (int i = 0; i < panelSlots.Length; i++)
        {
            panelSlots[i]?.ConfigureSection(this, i, i == 0 || i == panelSlots.Length - 1);
        }
    }

    private void RefreshUnlockState(bool force)
    {
        int unlockedIndex = -1;
        if (panelSlots != null)
        {
            for (int i = 0; i < panelSlots.Length; i++)
            {
                if (IsSlotUnlocked(i) && panelSlots[i] != null && !panelSlots[i].IsComplete)
                {
                    unlockedIndex = i;
                    break;
                }
            }
        }

        if (!force && unlockedIndex == lastUnlockedIndex) return;
        lastUnlockedIndex = unlockedIndex;
        if (panelSlots == null) return;
        for (int i = 0; i < panelSlots.Length; i++)
        {
            panelSlots[i]?.RefreshSectionAvailability();
        }
    }

    private bool ArePrerequisitesComplete()
    {
        if (prerequisites == null || prerequisites.Length == 0) return true;
        foreach (BridgeComponent prerequisite in prerequisites)
        {
            if (prerequisite != null && !prerequisite.IsAssembled) return false;
        }
        return true;
    }

    private void OnValidate()
    {
        sectionLength = Mathf.Max(0.1f, sectionLength);
        panelGap = Mathf.Max(0f, panelGap);
        nominalPanelLength = Mathf.Max(0.1f, nominalPanelLength);
        ConfigureSlots();
        RefreshLayout();
    }
}
