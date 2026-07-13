using System;
using UnityEngine;
using UnityEngine.UI;

public class ProductionProgressUI : MonoBehaviour
{
    [SerializeField] private FurnaceStorage furnaceStorage;
    [SerializeField] private Image progressHolder;
    [SerializeField] private Image combustionProgressHolder;
    private bool uiVisible = false;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        furnaceStorage.ProductionStarted += FurnaceStorage_OnProductionStarted;
        furnaceStorage.ProductionFinished += FurnaceStorage_OnProductionFinished;
        furnaceStorage.FurnaceStateChanged += FurnaceStorage_OnFurnaceStateChanged;
        Hide();
        FurnaceStorage_OnFurnaceStateChanged(this, EventArgs.Empty);
    }

    private void OnDestroy()
    {
        if (furnaceStorage != null)
        {
            furnaceStorage.ProductionStarted -= FurnaceStorage_OnProductionStarted;
            furnaceStorage.ProductionFinished -= FurnaceStorage_OnProductionFinished;
            furnaceStorage.FurnaceStateChanged -= FurnaceStorage_OnFurnaceStateChanged;
        }
    }

    private void FurnaceStorage_OnProductionFinished(object sender, EventArgs e)
    {
        Hide();
    }

    private void FurnaceStorage_OnProductionStarted(object sender, EventArgs e)
    {
        Show();
    }

    private void FurnaceStorage_OnFurnaceStateChanged(object sender, EventArgs e)
    {
        if (furnaceStorage.IsFurnaceOn)
        {
            Show();
            UpdateFillMeters();
        }
        else
        {
            Hide();
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void FixedUpdate()
    {
        if (uiVisible)
        {
            UpdateFillMeters();
        }
    }
    private void Show()
    {
        gameObject.SetActive(true);
        uiVisible = true;
    }

    private void Hide()
    {
        gameObject.SetActive(false);
        uiVisible = false;
    }

    private void ResetFillMeters()
    {
        progressHolder.fillAmount = 0;
        combustionProgressHolder.fillAmount = 0;
    }

    private void UpdateFillMeters()
    {
        progressHolder.fillAmount = furnaceStorage.GetProductionProgressNormalized();
        combustionProgressHolder.fillAmount = furnaceStorage.GetCombustionProgressNormalized();
    }
}
