using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class BuildingInputManager : MonoBehaviour
{
    [SerializeField]
    private Camera drawingTableCamera;

    private Vector3 lastPosition;

    [SerializeField]
    private LayerMask drawingTableLayermask;

    public event Action OnClicked, OnExit;

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
            OnClicked?.Invoke();
        if (Input.GetKeyDown(KeyCode.Escape))
            OnExit?.Invoke();
    }

    public bool IsPointerOverUI()
        => EventSystem.current.IsPointerOverGameObject();

    public Vector3 GetSelectedMapPosition()
    {
        Vector3 mousePos = Input.mousePosition;
        mousePos.z = drawingTableCamera.nearClipPlane;
        Ray ray = drawingTableCamera.ScreenPointToRay(mousePos);
        RaycastHit hit;
        if(Physics.Raycast(ray, out hit, 100, drawingTableLayermask))
        {
            lastPosition = hit.point;
        }
        return lastPosition;
    }
}
