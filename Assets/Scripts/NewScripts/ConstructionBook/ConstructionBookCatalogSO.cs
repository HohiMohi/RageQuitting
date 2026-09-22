using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ConstructionBookCatalog", menuName = "Scriptable Objects/Construction Book Catalog")]
public sealed class ConstructionBookCatalogSO : ScriptableObject
{
    [Serializable]
    public sealed class Requirement
    {
        public string displayName;
        public Sprite sprite;
    }

    [Serializable]
    public sealed class Step
    {
        [TextArea] public string instruction;
        public Requirement[] requirements = Array.Empty<Requirement>();
    }

    [Serializable]
    public sealed class Entry
    {
        public BridgeComponentSO component;
        public string title;
        public Sprite partSprite;
        public Step[] steps = Array.Empty<Step>();
    }

    [SerializeField] private Entry[] entries = Array.Empty<Entry>();
    public IReadOnlyList<Entry> Entries => entries;

    public bool TryGetEntry(BridgeComponentSO component, out Entry entry)
    {
        foreach (Entry candidate in entries)
        {
            if (candidate != null && candidate.component == component)
            {
                entry = candidate;
                return true;
            }
        }
        entry = null;
        return false;
    }

    public IEnumerable<string> ValidateCatalog()
    {
        HashSet<BridgeComponentSO> seen = new HashSet<BridgeComponentSO>();
        for (int i = 0; i < entries.Length; i++)
        {
            Entry entry = entries[i];
            if (entry == null || entry.component == null) yield return $"Entry {i + 1}: missing BridgeComponentSO.";
            else if (!seen.Add(entry.component)) yield return $"Entry {i + 1}: duplicate component '{entry.component.name}'.";
            if (entry != null && entry.partSprite == null) yield return $"Entry {i + 1}: missing part sprite.";
            if (entry == null || entry.steps == null || entry.steps.Length == 0) yield return $"Entry {i + 1}: missing authored steps.";
            else for (int s = 0; s < entry.steps.Length; s++)
            {
                Step step = entry.steps[s];
                if (step == null || string.IsNullOrWhiteSpace(step.instruction))
                    yield return $"Entry {i + 1}, step {s + 1}: missing instruction.";
                if (step == null || step.requirements == null) continue;
                for (int r = 0; r < step.requirements.Length; r++)
                {
                    Requirement requirement = step.requirements[r];
                    if (requirement == null)
                    {
                        yield return $"Entry {i + 1}, step {s + 1}, requirement {r + 1}: null requirement.";
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(requirement.displayName))
                        yield return $"Entry {i + 1}, step {s + 1}, requirement {r + 1}: missing display name.";
                    if (requirement.sprite == null)
                        yield return $"Entry {i + 1}, step {s + 1}, requirement {r + 1}: missing icon.";
                }
            }
        }
    }
}
