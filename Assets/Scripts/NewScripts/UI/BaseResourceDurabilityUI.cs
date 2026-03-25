using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BaseResourceDurabilityUI : MonoBehaviour
{
    [SerializeField] private BaseResourceNew baseResourceNew;
    [SerializeField] private Image currentDurabilityImage;
    [SerializeField] private TextMeshProUGUI currentDurabilityText;
    [SerializeField] private float showUITime;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        baseResourceNew.ResourceDurabilityChanged += BaseResource_OnResourceDurabilityChanged;
        Hide();
    }

    private void BaseResource_OnResourceDurabilityChanged(object sender, BaseResourceNew.ResourceDurabilityChangedEventArgs e)
    {
        UpdateUI(e.resourceDurability, e.resourceDurabilityNormalized);
        Show();
        StartCoroutine(HideUIAfterTime());
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    private void UpdateUI(float durability, float durabilityNormalized)
    {
        currentDurabilityImage.fillAmount = durabilityNormalized;
        currentDurabilityText.text = Math.Round(durability,2).ToString();
    }
    private void Show()
    {
        gameObject.SetActive(true);
    }
    private void Hide()
    {
        gameObject.SetActive(false);
    }

    private IEnumerator HideUIAfterTime()
    {
        yield return new WaitForSeconds(showUITime);
        Hide();
    }
}
