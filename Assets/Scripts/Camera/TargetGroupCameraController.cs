using UnityEngine;
using Cinemachine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

public class TargetGroupCameraController : MonoBehaviour
{
    public CinemachineVirtualCamera virtualCamera;
    public CinemachineTargetGroup targetGroup;
    public GameObject playersHolder;

    // Start is called once before the first execution of Update after the MonoBehaviour is created

    void Start()
    {
        UpdateTargetGroup();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    public void UpdateTargetGroup()
    {
        PlayerInput[] playerInputs = playersHolder.GetComponentsInChildren<PlayerInput>();
        foreach (PlayerInput player in playerInputs)
        {
            targetGroup.AddMember(player.transform, 1, 1);
        }

    }
}
