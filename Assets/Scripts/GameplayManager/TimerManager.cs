using UnityEngine;
using TMPro;

public class TimerManager : MonoBehaviour
{
    public TextMeshProUGUI timerTextMesh;
    public float currentLevelTimeLimitInSeconds;
    // int minutesCounter;
    // float secondsCounter;
    float secondsLeft;
    bool isGameplayOn = true;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void OnEnable()
    {
        secondsLeft = currentLevelTimeLimitInSeconds;

    }
    void Start()
    {

    }

    private void FixedUpdate()
    {
        CheckTimeLeft();
        if (isGameplayOn)
        {
            secondsLeft -= Time.fixedDeltaTime;
            UpdateTimerText();
        }
    }

    void UpdateTimerText()
    {
        float tempCounter = Mathf.Ceil(secondsLeft);
        timerTextMesh.text = tempCounter.ToString();
    }

    void CheckTimeLeft()
    {
        if(secondsLeft <= 0 && isGameplayOn)
        {
            print("Time left.");
            timerTextMesh.text = "Time left!";
            isGameplayOn = false;
        }
    }
    //void UpdateCounters()
    //{
    //    secondsCounter = secondsLeft % 60;
    //    minutesCounter = (int)(secondsLeft / 60);
    //}
    //void UpdateTimerText()
    //{
    //    timerTextMesh.text = minutesCounter.ToString() + ":" + secondsCounter.ToString();
    //}

}
