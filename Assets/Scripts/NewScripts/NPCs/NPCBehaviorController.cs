public abstract class NPCBehaviorController
{
    protected readonly NPCBrain Brain;

    protected NPCBehaviorController(NPCBrain brain)
    {
        Brain = brain;
    }

    public virtual void Enter() { }
    public abstract void Tick();
    public virtual void Exit() { }
}
