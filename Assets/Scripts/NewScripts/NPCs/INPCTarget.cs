using UnityEngine;

public interface INPCTarget
{
    Transform TargetTransform { get; }
    NPCFactionSO Faction { get; }
    bool IsTargetAvailable { get; }
}
