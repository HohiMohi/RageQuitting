using System.Globalization;

public interface ISubstanceSource
{
    bool CanExtract(ContainerSubstanceSO substance);
    float TryExtract(ContainerSubstanceSO substance, float units);
}

public static class SubstanceAmount
{
    public static bool IsFinitePositive(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;

    public static string Format(float value)
    {
        string concise = value.ToString("0.##", CultureInfo.InvariantCulture);
        return value > 0f && concise == "0"
            ? value.ToString("G9", CultureInfo.InvariantCulture)
            : concise;
    }
}
