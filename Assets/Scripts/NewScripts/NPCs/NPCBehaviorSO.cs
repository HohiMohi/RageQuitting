using UnityEngine;

public abstract class NPCBehaviorSO : ScriptableObject
{
    public abstract NPCBehaviorController CreateController(NPCBrain brain);
}
