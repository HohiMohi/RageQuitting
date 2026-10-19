/// <summary>Receives the one successful single-carry placement commit after holder cleanup.</summary>
public interface ISingleCarryPlacementCommitListener
{
    void OnSingleCarryPlacementCommitted(PlayerInteractionNew holder);
}
