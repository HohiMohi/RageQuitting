using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ResourceSourceDurabilityUI : MonoBehaviour
{
    [SerializeField] private BaseResourceSource resourceSource;
    [SerializeField] private Image currentDurabilityImage;
    [SerializeField] private TextMeshProUGUI currentDurabilityText;
    [SerializeField] private float showUITime;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        resourceSource.ResourceSourceDurabilityChanged += ResourceSource_OnResourceSourceDurabilityChanged;
        Hide();
    }

    private void ResourceSource_OnResourceSourceDurabilityChanged(object sender, BaseResourceSource.ResourceSourceDurabilityChangedEventArgs e)
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
