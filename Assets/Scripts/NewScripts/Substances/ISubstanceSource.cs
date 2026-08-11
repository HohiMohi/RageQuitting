public interface ISubstanceSource
{
    bool CanExtract(ContainerSubstanceSO substance);
    bool TryExtract(ContainerSubstanceSO substance, int units);
}
