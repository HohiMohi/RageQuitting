using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameTimerUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI timerText;
    [SerializeField] private Image timerProgressBar; // Radial or linear fill progress bar

    [Header("Feedback Settings")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color warningColor = new Color(0.9f, 0.2f, 0.2f); // Vibrant red warning color
    [SerializeField] private float warningThreshold = 60f; // Warning effects trigger under 60 seconds
    [SerializeField] private float pulseSpeed = 5f;

    private void Start()
    {
        if (GameTimerManager.Instance != null)
        {
            GameTimerManager.Instance.OnTimerChanged += GameTimerManager_OnTimerChanged;
        }
        else
        {
            Debug.LogError("GameTimerUI: GameTimerManager Instance not found in scene!");
        }
    }

    private void OnDestroy()
    {
        if (GameTimerManager.Instance != null)
        {
            GameTimerManager.Instance.OnTimerChanged -= GameTimerManager_OnTimerChanged;
        }
    }

    private void GameTimerManager_OnTimerChanged(object sender, GameTimerManager.OnTimerChangedEventArgs e)
    {
        UpdateUI(e.timeRemaining, e.normalizedTimeRemaining);
    }

    private void UpdateUI(float timeRemaining, float normalizedTimeRemaining)
    {
        // 1. Format time into MM:SS format
        int minutes = Mathf.FloorToInt(timeRemaining / 60F);
        int seconds = Mathf.FloorToInt(timeRemaining - (minutes * 60));
        timerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);

        // 2. Update horizontal progress bar fill
        if (timerProgressBar != null)
        {
            timerProgressBar.fillAmount = normalizedTimeRemaining;
        }

        // 3. Danger/Warning Feedback Effects (Color and Text Pulsing)
        if (timeRemaining <= warningThreshold)
        {
            timerText.color = warningColor;
            if (timerProgressBar != null)
            {
                timerProgressBar.color = warningColor;
            }

            // Create a subtle breathing/pulsing scale effect to highlight high urgency
            float pulseScale = 1.0f + Mathf.Sin(Time.time * pulseSpeed) * 0.08f;
            timerText.transform.localScale = new Vector3(pulseScale, pulseScale, 1.0f);
        }
        else
        {
            timerText.color = normalColor;
            if (timerProgressBar != null)
            {
                timerProgressBar.color = normalColor;
            }
            timerText.transform.localScale = Vector3.one;
        }
    }
}
