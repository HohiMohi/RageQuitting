public interface ISubstanceSink
{
    bool CanAccept(PortableSubstanceContainer container);
    bool TryDeposit(PortableSubstanceContainer container, PlayerInteractionNew player);
    string GetDepositPrompt(PortableSubstanceContainer container);
}
