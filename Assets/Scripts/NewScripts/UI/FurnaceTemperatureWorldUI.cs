using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FurnaceTemperatureWorldUI : MonoBehaviour
{
    [SerializeField] private FurnaceStorage furnaceStorage;
    [SerializeField] private TextMeshProUGUI temperatureText;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Color coldColor = new Color(0.25f, 0.55f, 1f);
    [SerializeField] private Color optimalColor = new Color(0.25f, 1f, 0.35f);
    [SerializeField] private Color overheatColor = new Color(1f, 0.2f, 0.15f);

    private void Awake()
    {
        EnsureReferences();
        Refresh();
    }

    private void OnEnable()
    {
        if (furnaceStorage != null)
        {
            furnaceStorage.FurnaceStateChanged += FurnaceStorage_FurnaceStateChanged;
        }

        Refresh();
    }

    private void OnDisable()
    {
        if (furnaceStorage != null)
        {
            furnaceStorage.FurnaceStateChanged -= FurnaceStorage_FurnaceStateChanged;
        }
    }

    public void SetFurnaceStorage(FurnaceStorage storage)
    {
        if (furnaceStorage != null)
        {
            furnaceStorage.FurnaceStateChanged -= FurnaceStorage_FurnaceStateChanged;
        }

        furnaceStorage = storage;
        if (isActiveAndEnabled && furnaceStorage != null)
        {
            furnaceStorage.FurnaceStateChanged += FurnaceStorage_FurnaceStateChanged;
        }

        Refresh();
    }

    public void Refresh()
    {
        EnsureReferences();
        if (temperatureText == null)
        {
            return;
        }

        if (furnaceStorage == null)
        {
            temperatureText.text = "--°C";
            temperatureText.color = coldColor;
            return;
        }

        temperatureText.text = $"{furnaceStorage.CurrentTemperature:0}°C";
        temperatureText.color = GetTemperatureColor();
    }

    private Color GetTemperatureColor()
    {
        if (furnaceStorage == null || !furnaceStorage.HasSelectedComponent)
        {
            return coldColor;
        }

        float currentTemperature = furnaceStorage.CurrentTemperature;
        if (currentTemperature < furnaceStorage.CurrentMeltingPoint)
        {
            return coldColor;
        }

        if (furnaceStorage.CurrentCombustionTemperature > furnaceStorage.CurrentMeltingPoint
            && currentTemperature > furnaceStorage.CurrentCombustionTemperature)
        {
            return overheatColor;
        }

        return optimalColor;
    }

    private void FurnaceStorage_FurnaceStateChanged(object sender, System.EventArgs e)
    {
        Refresh();
    }

    private void EnsureReferences()
    {
        if (temperatureText == null)
        {
            temperatureText = GetComponentInChildren<TextMeshProUGUI>(true);
        }

        if (backgroundImage == null)
        {
            backgroundImage = GetComponentInChildren<Image>(true);
        }
    }
}
