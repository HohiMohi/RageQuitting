using System;
using UnityEngine;
using UnityEngine.UI;

public class ProductionMinigameUI : MonoBehaviour
{
    [SerializeField] private Minigame minigame;
    [SerializeField] private Image background;
    [SerializeField] private Color backgroundNeutralColor;
    [SerializeField] private Color backgroundPositiveColor;
    [SerializeField] private Color backgroundNegativeColor;
    [SerializeField] private RectTransform currentPlayerValueObject;
    [SerializeField] private RectTransform currentRequiredValueObject;
    [SerializeField] private RectTransform currentPerfectValueObject;
    [SerializeField] private RectTransform currentCriticalFailureValueObject;
    [SerializeField] private float lerpSpeed;
    private float sizeDiff;
    private float perfSizeDiff;
    private bool isMinigameOn;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        minigame.StartMinigameEvent += Minigame_OnStartMinigame;
        minigame.EndMinigameEvent += Minigame_OnEndMinigame;
        sizeDiff = currentRequiredValueObject.sizeDelta.y / 2;
        perfSizeDiff = currentPerfectValueObject.sizeDelta.y / 2;
        Hide();
    }

    private void Minigame_OnEndMinigame(object sender, EventArgs e)
    {
        Hide();
    }

    private void Minigame_OnStartMinigame(object sender, EventArgs e)
    {
        Show();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Show()
    {
        isMinigameOn = true;
        gameObject.SetActive(true);
    }

    private void Hide()
    {
        isMinigameOn = false;
        ResetValueObjectsPosition();
        gameObject.SetActive(false);

    }
    private void FixedUpdate()
    {
      if(isMinigameOn)
        {
            float playerValueY = minigame.GetPlayerValue();
            float currentRequiredValueY = minigame.GetCurrentRequiredValue();
            float currentPerfectValueY = minigame.GetCurrentPerfectValue();
            float currentCriticalFailureValueY = minigame.GetCurrentCriticalFailureValue();
            Vector3 newPlayerObjectPosition = Vector3.Lerp(currentPlayerValueObject.anchoredPosition, new Vector3(0, playerValueY, 0), lerpSpeed * Time.deltaTime);
            Vector3 newRequiredValueObjectPosition = Vector3.Lerp(currentRequiredValueObject.anchoredPosition, new Vector3(0, currentRequiredValueY, 0), lerpSpeed * Time.deltaTime);
            Vector3 newPerfectValueObjectPosition = Vector3.Lerp(currentPerfectValueObject.anchoredPosition, new Vector3(0, currentPerfectValueY, 0), lerpSpeed * Time.deltaTime);
            Vector3 newCriticalFailureValueObjectPosition = Vector3.Lerp(currentCriticalFailureValueObject.anchoredPosition, new Vector3(0, currentCriticalFailureValueY, 0), lerpSpeed * Time.deltaTime);
            currentPlayerValueObject.anchoredPosition = newPlayerObjectPosition;
            currentRequiredValueObject.anchoredPosition = newRequiredValueObjectPosition;
            currentPerfectValueObject.anchoredPosition = newPerfectValueObjectPosition;
            currentCriticalFailureValueObject.anchoredPosition = newCriticalFailureValueObjectPosition;
            if (minigame.CalculateValueDifference() < sizeDiff)
            {
                background.color = backgroundNeutralColor;
            }
            else if (minigame.CalculatePerfectValueDifference() <= perfSizeDiff)
            {
                background.color = backgroundPositiveColor;
            }
            else
            {
                background.color = backgroundNegativeColor;
            }
        }
    }

    private void ResetValueObjectsPosition()
    {
        currentPlayerValueObject.anchoredPosition = new Vector3(0, -currentPlayerValueObject.sizeDelta.y / 2, 0);
        currentRequiredValueObject.anchoredPosition = new Vector3(0, -currentRequiredValueObject.sizeDelta.y / 2, 0);
        currentPerfectValueObject.anchoredPosition = new Vector3(0, - (currentRequiredValueObject.sizeDelta.y + currentPerfectValueObject.sizeDelta.y / 2), 0);
        currentCriticalFailureValueObject.anchoredPosition = new Vector3(0, -(currentRequiredValueObject.sizeDelta.y + currentPerfectValueObject.sizeDelta.y + currentCriticalFailureValueObject.sizeDelta.y / 2), 0);
    }



}
