using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DrawingTableSystem : MonoBehaviour
{
    [SerializeField]
    GameObject cursorIndicator, cellIndicator;
    [SerializeField]
    private BuildingInputManager buildingInputManager;
    [SerializeField]
    private Grid grid;

    [SerializeField]
    private LayerMask bridgeObjectsMask;
    public BridgeGraph2 bridgeGraph2;

    [SerializeField]
    private BridgeElementsDatabaseSO elementDatabase;
    private int selectedElementIndex = -1;

    [SerializeField]
    private GameObject gridVisualization;

    private GridData SupportData, SpanData, ConnectorData, GroundMountData, SideMountData;

    private Renderer previewRenderer;

    private List<GameObject> placedGameObjects = new();

    private Vector3Int selectedFirstGridPositionHolder, selectedSecondGridPositionHolder;
    
    private void Start()
    {
        StopPlacement();
        SupportData = new GridData();
        SpanData = new GridData();
        ConnectorData = new GridData();
        GroundMountData = new GridData();
        SideMountData = new GridData();
        previewRenderer = cellIndicator.GetComponentInChildren<Renderer>();
        ResetPositionHolders();
    }

    public void ResetPositionHolders()
    {
        selectedFirstGridPositionHolder = new Vector3Int(-100, -100, -100);
        selectedSecondGridPositionHolder = new Vector3Int(-100, -100, -100);
    }

    public void StartPlacement(int ID)
    {
        StopPlacement();
        selectedElementIndex = elementDatabase.objectsData.FindIndex(data => data.ID == ID);
        ResetPositionHolders();

        if (selectedElementIndex < 0)
        {
            Debug.LogError($"No ID found {ID}");
            return;
        }

        gridVisualization.SetActive(true);
        cellIndicator.SetActive(true);
        buildingInputManager.OnClicked += PlaceElement;
        buildingInputManager.OnExit += StopPlacement;
    }

    private void PlaceElement()
    {

        if (buildingInputManager.IsPointerOverUI())
        {
            return;
        }

        switch (selectedElementIndex)
        {
            case 0:
                {
                    Vector3 cursorPosition = buildingInputManager.GetSelectedMapPosition();
                    Vector3Int gridPosition = grid.WorldToCell(cursorPosition);

                    bool placementValidity = CheckPlacementValidity(gridPosition, selectedElementIndex);
                    if (placementValidity == false)
                        return;
                    GameObject elementToSpawn = Instantiate(elementDatabase.objectsData[selectedElementIndex].Prefab);
                    elementToSpawn.transform.position = grid.CellToWorld(gridPosition);
                    placedGameObjects.Add(elementToSpawn);

                    BridgeNodeDataHolder nodeToAdd = new BridgeNodeDataHolder();
                    GridData selectedData = GetGridDataOfType(selectedElementIndex);

                    nodeToAdd.buildingElementSO = elementDatabase.objectsData[0].buildingElementSO;
                    nodeToAdd.occupiedCellVectorsList = selectedData.CalculatePositions(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size);
                    bridgeGraph2.AddNode(nodeToAdd);

                    nodeToAdd.ConnectNode(bridgeGraph2.FindBridgeNode(elementDatabase.objectsData[3].buildingElementSO, gridPosition));

                    selectedData.AddElementAt(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size, elementDatabase.objectsData[selectedElementIndex].ID, placedGameObjects.Count - 1);
                    break;
                }
            case 1:
                {
                    Vector3 cursorPosition = buildingInputManager.GetSelectedMapPosition();
                    if (selectedFirstGridPositionHolder == new Vector3Int(-100, -100, -100) && CheckPlacementValidity(grid.WorldToCell(cursorPosition), selectedElementIndex))
                    {
                        selectedFirstGridPositionHolder = grid.WorldToCell(cursorPosition);
                        break;
                    }
                    else if(selectedSecondGridPositionHolder == new Vector3Int(-100, -100, -100) && (CheckPlacementValidity(selectedFirstGridPositionHolder, grid.WorldToCell(cursorPosition), selectedElementIndex)) && (selectedFirstGridPositionHolder.y == grid.WorldToCell(cursorPosition).y) && (selectedFirstGridPositionHolder != grid.WorldToCell(cursorPosition)))
                    {
                        selectedSecondGridPositionHolder = grid.WorldToCell(cursorPosition);

                        Vector3Int elementSpawnPosition;
                        if (selectedFirstGridPositionHolder.x < selectedSecondGridPositionHolder.x)
                            elementSpawnPosition = selectedFirstGridPositionHolder;
                        else
                            elementSpawnPosition = selectedSecondGridPositionHolder;

                        //Find bridgeNode, that element will be connected to
                        BridgeNodeDataHolder firstElementHolder, secondElementHolder;
                        firstElementHolder = bridgeGraph2.FindBridgeNode(elementDatabase.objectsData[0].buildingElementSO, selectedFirstGridPositionHolder);
                        if (firstElementHolder == null)
                        {
                            firstElementHolder = bridgeGraph2.FindBridgeNode(elementDatabase.objectsData[4].buildingElementSO, selectedFirstGridPositionHolder); 
                        }

                        secondElementHolder = bridgeGraph2.FindBridgeNode(elementDatabase.objectsData[0].buildingElementSO, selectedSecondGridPositionHolder);
                        if (secondElementHolder == null)
                        {
                            secondElementHolder = bridgeGraph2.FindBridgeNode(elementDatabase.objectsData[4].buildingElementSO, selectedSecondGridPositionHolder);
                        }

                        
                        float elementScale = MathF.Abs(selectedFirstGridPositionHolder.x - selectedSecondGridPositionHolder.x);
                        GameObject elementToSpawn = Instantiate(elementDatabase.objectsData[selectedElementIndex].Prefab);
                        elementToSpawn.transform.position = grid.CellToWorld(elementSpawnPosition);
                        placedGameObjects.Add(elementToSpawn);
                        GameObject elementRendererHolder = elementToSpawn.GetComponentInChildren<Renderer>().gameObject;

                        ModifyTransformPropertiesOfObject(elementRendererHolder, elementScale);

                        //Create BridgeNode to add to BridgeGraph
                        BridgeNodeDataHolder nodeToAdd = new BridgeNodeDataHolder();
                        GridData selectedData = GetGridDataOfType(selectedElementIndex);

                        nodeToAdd.buildingElementSO = elementDatabase.objectsData[1].buildingElementSO;
                        nodeToAdd.occupiedCellVectorsList = selectedData.CalculatePositions(elementSpawnPosition, new Vector2Int((int)elementScale + 1, elementDatabase.objectsData[selectedElementIndex].Size.y));
                        bridgeGraph2.AddNode(nodeToAdd);

                        nodeToAdd.ConnectNode(firstElementHolder);
                        nodeToAdd.ConnectNode(secondElementHolder);

                        selectedData.AddElementAt(elementSpawnPosition, new Vector2Int((int)elementScale + 1, elementDatabase.objectsData[selectedElementIndex].Size.y), elementDatabase.objectsData[selectedElementIndex].ID, placedGameObjects.Count - 1);
                        ResetPositionHolders();
                        break;
                    }
                    else
                    {
                        ResetPositionHolders();
                    }

                    break;
                }

            case 2:
                {
                    Vector3 cursorPosition = buildingInputManager.GetSelectedMapPosition();
                    if (selectedFirstGridPositionHolder == new Vector3Int(-100, -100, -100) && CheckPlacementValidity(grid.WorldToCell(cursorPosition), selectedElementIndex))
                    {
                        selectedFirstGridPositionHolder = grid.WorldToCell(cursorPosition);
                        break;
                    }
                    else if (selectedSecondGridPositionHolder == new Vector3Int(-100, -100, -100) && (CheckPlacementValidity(selectedFirstGridPositionHolder ,grid.WorldToCell(cursorPosition), selectedElementIndex)) &&  (selectedFirstGridPositionHolder != grid.WorldToCell(cursorPosition)))
                    {
                        selectedSecondGridPositionHolder = grid.WorldToCell(cursorPosition);

                        Vector3Int positionDifferenceVector = selectedSecondGridPositionHolder - selectedFirstGridPositionHolder;
                        //if (selectedFirstGridPositionHolder.x < selectedSecondGridPositionHolder.x)
                        //{
                        //    elementSpawnPosition = selectedFirstGridPositionHolder;
                        //    positionDifferenceVector = selectedSecondGridPositionHolder - selectedFirstGridPositionHolder;

                        //}
                        //else
                        //{ 
                        //    elementSpawnPosition = selectedSecondGridPositionHolder;
                        //    positionDifferenceVector = selectedFirstGridPositionHolder - selectedSecondGridPositionHolder;

                        //}

                        BridgeNodeDataHolder firstElementHolder, secondElementHolder;
                        firstElementHolder = bridgeGraph2.FindBridgeNode(elementDatabase.objectsData[0].buildingElementSO, selectedFirstGridPositionHolder);
                        if (firstElementHolder == null)
                        {
                            firstElementHolder = bridgeGraph2.FindBridgeNode(elementDatabase.objectsData[1].buildingElementSO, selectedFirstGridPositionHolder);
                        }

                        secondElementHolder = bridgeGraph2.FindBridgeNode(elementDatabase.objectsData[0].buildingElementSO, selectedSecondGridPositionHolder);
                        if (secondElementHolder == null)
                        {
                            secondElementHolder = bridgeGraph2.FindBridgeNode(elementDatabase.objectsData[1].buildingElementSO, selectedSecondGridPositionHolder);
                        }


                        GameObject elementToSpawn = Instantiate(elementDatabase.objectsData[selectedElementIndex].Prefab);
                        elementToSpawn.transform.position = grid.CellToWorld(selectedFirstGridPositionHolder);
                        GameObject cubeHolder = elementToSpawn.GetComponentInChildren<MeshRenderer>().gameObject;
                        ModifyTransformPropertiesOfObject(cubeHolder, grid.CellToWorld(positionDifferenceVector));

                        placedGameObjects.Add(elementToSpawn);

                        ModifyLineRendererPositions(elementToSpawn.GetComponentInChildren<LineRenderer>(), grid.CellToWorld(selectedFirstGridPositionHolder), grid.CellToWorld(selectedSecondGridPositionHolder));

                        BridgeNodeDataHolder nodeToAdd = new BridgeNodeDataHolder();
                        GridData selectedData = GetGridDataOfType(selectedElementIndex);

                        nodeToAdd.buildingElementSO = elementDatabase.objectsData[2].buildingElementSO;
                        nodeToAdd.occupiedCellVectorsList.Add(selectedFirstGridPositionHolder);
                        nodeToAdd.occupiedCellVectorsList.Add(selectedSecondGridPositionHolder);

                        bridgeGraph2.AddNode(nodeToAdd);

                        nodeToAdd.ConnectNode(firstElementHolder);
                        nodeToAdd.ConnectNode(secondElementHolder);

                        selectedData.AddElementAt(selectedFirstGridPositionHolder, elementDatabase.objectsData[selectedElementIndex].Size, elementDatabase.objectsData[selectedElementIndex].ID, placedGameObjects.Count - 1);
                        selectedData.AddElementAt(selectedSecondGridPositionHolder, elementDatabase.objectsData[selectedElementIndex].Size, elementDatabase.objectsData[selectedElementIndex].ID, placedGameObjects.Count - 1);
                        ResetPositionHolders();
                    }
                    else
                    {
                        ResetPositionHolders();
                    }
                        break;
                }
            case 3:
                {
                    Vector3 cursorPosition = buildingInputManager.GetSelectedMapPosition();
                    Vector3Int gridPosition = grid.WorldToCell(cursorPosition);

                    bool placementValidity = CheckPlacementValidity(gridPosition, selectedElementIndex);
                    if (placementValidity == false)
                        return;
                    GameObject elementToSpawn = Instantiate(elementDatabase.objectsData[selectedElementIndex].Prefab);
                    elementToSpawn.transform.position = grid.CellToWorld(gridPosition);
                    placedGameObjects.Add(elementToSpawn);
                    GridData selectedData = GetGridDataOfType(selectedElementIndex);

                    BridgeNodeDataHolder nodeToAdd = new BridgeNodeDataHolder();
                    nodeToAdd.buildingElementSO = elementDatabase.objectsData[3].buildingElementSO;
                    nodeToAdd.occupiedCellVectorsList = selectedData.CalculatePositions(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size);
                    bridgeGraph2.AddNode(nodeToAdd);

                    selectedData.AddElementAt(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size, elementDatabase.objectsData[selectedElementIndex].ID, placedGameObjects.Count - 1);
                    break;
                }
            case 4:
                {
                    Vector3 cursorPosition = buildingInputManager.GetSelectedMapPosition();
                    Vector3Int gridPosition = grid.WorldToCell(cursorPosition);

                    bool placementValidity = CheckPlacementValidity(gridPosition, selectedElementIndex);
                    if (placementValidity == false)
                        return;
                    GameObject elementToSpawn = Instantiate(elementDatabase.objectsData[selectedElementIndex].Prefab);
                    elementToSpawn.transform.position = grid.CellToWorld(gridPosition);
                    placedGameObjects.Add(elementToSpawn);
                    GridData selectedData = GetGridDataOfType(selectedElementIndex);

                    BridgeNodeDataHolder nodeToAdd = new BridgeNodeDataHolder();
                    nodeToAdd.buildingElementSO = elementDatabase.objectsData[4].buildingElementSO;
                    nodeToAdd.occupiedCellVectorsList = selectedData.CalculatePositions(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size);
                    bridgeGraph2.AddNode(nodeToAdd);

                    selectedData.AddElementAt(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size, elementDatabase.objectsData[selectedElementIndex].ID, placedGameObjects.Count - 1);
                    break;
                }
            case 5:
                {
                    Vector3 cursorPosition = buildingInputManager.GetSelectedMapPosition();
                    Vector3Int gridPosition = grid.WorldToCell(cursorPosition);

                    if (selectedFirstGridPositionHolder != gridPosition)
                    {
                        // Check, if there is any object on selected gridCell - encapsulate this in other method

                        selectedFirstGridPositionHolder = gridPosition;
                    }

                    else if(selectedFirstGridPositionHolder == gridPosition)
                    {
                        Dictionary<string, bool> objectPresenceList = CheckObjectsPresence(gridPosition);
                        if (objectPresenceList["Connector"] == true)
                        {
                            BridgeNodeDataHolder firstElementHolder;
                            firstElementHolder = bridgeGraph2.FindBridgeNode(elementDatabase.objectsData[2].buildingElementSO, selectedFirstGridPositionHolder);
                            if (firstElementHolder != null)
                            {
                                ConnectorData.RemoveObjectPlacedOnGridPosition(gridPosition);
                                bridgeGraph2.DeleteNode(firstElementHolder);
                                Vector3 mousePos = Input.mousePosition;
                                DestroyObject(mousePos, true);
                            }
                            
                            selectedFirstGridPositionHolder = new Vector3Int(-100, -100, -100);
                        }
                        else if (objectPresenceList["Span"] == true)
                        {
                            BridgeNodeDataHolder firstElementHolder;
                            firstElementHolder = bridgeGraph2.FindBridgeNode(elementDatabase.objectsData[1].buildingElementSO, selectedFirstGridPositionHolder);
                            if (firstElementHolder != null)
                            {
                                SpanData.RemoveObjectPlacedOnGridPosition(gridPosition);
                                bridgeGraph2.DeleteNode(firstElementHolder);
                                Vector3 mousePos = Input.mousePosition;
                                DestroyObject(mousePos, false);
                            }

                            selectedFirstGridPositionHolder = new Vector3Int(-100, -100, -100);
                        }
                        else if (objectPresenceList["Support"] == true)
                        {
                            BridgeNodeDataHolder firstElementHolder;
                            firstElementHolder = bridgeGraph2.FindBridgeNode(elementDatabase.objectsData[0].buildingElementSO, selectedFirstGridPositionHolder);
                            if (firstElementHolder != null)
                            {
                                SupportData.RemoveObjectPlacedOnGridPosition(gridPosition);
                                bridgeGraph2.DeleteNode(firstElementHolder);
                                Vector3 mousePos = Input.mousePosition;
                                DestroyObject(mousePos, false);
                            }

                            selectedFirstGridPositionHolder = new Vector3Int(-100, -100, -100);
                        }
                        else if (objectPresenceList["SideMount"] == true)
                        {
                            BridgeNodeDataHolder firstElementHolder;
                            firstElementHolder = bridgeGraph2.FindBridgeNode(elementDatabase.objectsData[4].buildingElementSO, selectedFirstGridPositionHolder);
                            if (firstElementHolder != null)
                            {
                                SideMountData.RemoveObjectPlacedOnGridPosition(gridPosition);
                                bridgeGraph2.DeleteNode(firstElementHolder);
                                Vector3 mousePos = Input.mousePosition;
                                DestroyObject(mousePos, false);
                            }

                            selectedFirstGridPositionHolder = new Vector3Int(-100, -100, -100);
                        }
                        else if (objectPresenceList["GroundMount"] == true)
                        {
                            BridgeNodeDataHolder firstElementHolder;
                            firstElementHolder = bridgeGraph2.FindBridgeNode(elementDatabase.objectsData[3].buildingElementSO, selectedFirstGridPositionHolder);
                            if (firstElementHolder != null)
                            {
                                GroundMountData.RemoveObjectPlacedOnGridPosition(gridPosition);
                                bridgeGraph2.DeleteNode(firstElementHolder);
                                Vector3 mousePos = Input.mousePosition;
                                DestroyObject(mousePos, false);
                            }

                            selectedFirstGridPositionHolder = new Vector3Int(-100, -100, -100);
                        }
                    }
                    break;
                }
            default:
                {
                    Vector3 cursorPosition = buildingInputManager.GetSelectedMapPosition();
                    Vector3Int gridPosition = grid.WorldToCell(cursorPosition);

                    bool placementValidity = CheckPlacementValidity(gridPosition, selectedElementIndex);
                    if (placementValidity == false)
                        return;
                    GameObject elementToSpawn = Instantiate(elementDatabase.objectsData[selectedElementIndex].Prefab);
                    elementToSpawn.transform.position = grid.CellToWorld(gridPosition);
                    placedGameObjects.Add(elementToSpawn);
                    GridData selectedData = GetGridDataOfType(selectedElementIndex);
                    selectedData.AddElementAt(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size, elementDatabase.objectsData[selectedElementIndex].ID, placedGameObjects.Count - 1);
                    break;
                }

        }


    }

    private Dictionary<string, bool> CheckObjectsPresence(Vector3Int gridPosition)
    {
        bool isConnectorPlaced = !ConnectorData.CanPlaceObjectAt(gridPosition, Vector2Int.one, -1);
        bool isSpanPlaced = !SpanData.CanPlaceObjectAt(gridPosition, Vector2Int.one, -1);
        bool isSupportPlaced = !SupportData.CanPlaceObjectAt(gridPosition, Vector2Int.one, -1);
        bool isGroundMountPlaced = !GroundMountData.CanPlaceObjectAt(gridPosition, Vector2Int.one, -1);
        bool isSideMountPlaced = !SideMountData.CanPlaceObjectAt(gridPosition, Vector2Int.one, -1);

        Dictionary<string, bool> objectPresenceList = new Dictionary<string, bool>();
        objectPresenceList.Add("Connector", isConnectorPlaced);
        objectPresenceList.Add("Span", isSpanPlaced);
        objectPresenceList.Add("Support", isSupportPlaced);
        objectPresenceList.Add("GroundMount", isGroundMountPlaced);
        objectPresenceList.Add("SideMount", isSideMountPlaced);

        Debug.Log(isConnectorPlaced + " " + isSpanPlaced + " " + isSupportPlaced + " " + isGroundMountPlaced + " " + isSideMountPlaced);
        return objectPresenceList;
    }

    public void DestroyObject(Vector3 mousePosition, bool isConnector)
    {
        mousePosition.z = Camera.main.nearClipPlane;
        Ray ray = Camera.main.ScreenPointToRay(mousePosition);
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 100, bridgeObjectsMask))
        {
            if(isConnector)
                Destroy(hit.collider.transform.parent.gameObject.transform.parent.gameObject);
            else
                Destroy(hit.collider.transform.parent.gameObject);
        }
    }

    private void ModifyLineRendererPositions(LineRenderer lineRenderer, Vector3 firstPoint, Vector3 secondPoint)
    {
        Vector3[] positionVectorsTable = new Vector3[2];
        Vector3 offsetVector = new Vector3(0.5f, 1f, 0.5f);
        positionVectorsTable[0] = firstPoint + offsetVector;
        positionVectorsTable[1] = secondPoint + offsetVector;
        lineRenderer.SetPositions(positionVectorsTable);
    }

    /// <summary>
    /// Modify object's transform properties - change scale and position of object. Apply to object itself.
    /// </summary>
    private void ModifyTransformPropertiesOfObject(GameObject objectToModify, float scaleToApply)
    {
        objectToModify.transform.localScale = new Vector3(scaleToApply + 1, objectToModify.transform.localScale.y, objectToModify.transform.localScale.z);
        objectToModify.transform.localPosition = new Vector3((scaleToApply / 2 + 0.5f), objectToModify.transform.localPosition.y, objectToModify.transform.localPosition.z);

    }

    /// <summary>
    /// Modify object's transform properties.
    /// </summary>
    private void ModifyTransformPropertiesOfObject(GameObject objectToModify, Vector3 differenceVector)
    {
        objectToModify.transform.localPosition = differenceVector;
        objectToModify.transform.localPosition = objectToModify.transform.localPosition + new Vector3(0, 0.2f, 0);
    }

    //Zmieniæ selectedElementIndex na BridgeElementType
    private bool CheckPlacementValidity(Vector3Int gridPosition, int selectedElementIndex)
    {
        GridData selectedData = new();
        GridData additionalData = new();
        GridData additionalSecondData = new();
        switch(selectedElementIndex)
        {
            case 0:
                {
                    selectedData = GetGridDataOfType(selectedElementIndex);
                    additionalData = GetGridDataOfType(3); // 3 is GroundMountElement ID - temporary, to change for BridgeElementType

                    return selectedData.CanPlaceObjectAt(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex,additionalData);

                }
            case 1:
                {
                        selectedData = GetGridDataOfType(selectedElementIndex);
                        additionalData = GetGridDataOfType(0);
                        additionalSecondData = GetGridDataOfType(4);
                        bool isPlacedOnSupport = selectedData.CanPlaceObjectAt(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex, additionalData);
                        bool isPlacedOnSideMount = selectedData.CanPlaceObjectAt(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex, additionalSecondData);
                        return (isPlacedOnSupport || isPlacedOnSideMount) ? true : false;  
                }
            case 2:
                {
                    selectedData = GetGridDataOfType(selectedElementIndex);
                    additionalData = GetGridDataOfType(0);
                    additionalSecondData = GetGridDataOfType(1);
                    bool isPlacedOnSupport = selectedData.CanPlaceObjectAt(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex, additionalData);
                    bool isPlacedOnSpan = selectedData.CanPlaceObjectAt(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex, additionalSecondData);
                    return (isPlacedOnSupport || isPlacedOnSpan) ? true : false;
                }
            default:
                {
                    selectedData = GetGridDataOfType(selectedElementIndex);
                    return selectedData.CanPlaceObjectAt(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex);
                }
        }

    }

    private bool CheckPlacementValidity(Vector3Int gridPosition, Vector3Int secondGridPosition, int selectedElementIndex)
    {
        GridData selectedData = new();
        GridData additionalFirstData = new();
        GridData additionalSecondData = new();
        switch (selectedElementIndex)
        {
            case 1:
                {
                    selectedData = GetGridDataOfType(selectedElementIndex);
                    additionalFirstData = GetGridDataOfType(0); // 0 is SupportElement ID - temporary, to change for BridgeElementType
                    additionalSecondData = GetGridDataOfType(4); // 4 is SideMountElement...

                    bool isPlacedOnSupport = selectedData.CanPlaceObjectAt(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex, additionalFirstData);
                    bool isPlacedOnSideMount = selectedData.CanPlaceObjectAt(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex, additionalSecondData);
                    bool isSupportPlacementValid = true;

                    if(isPlacedOnSupport)
                    {
                        if (gridPosition.x > secondGridPosition.x)
                        {
                            isSupportPlacementValid = !selectedData.CanPlaceObjectAt(gridPosition - new Vector3Int(1, 0, 0), elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex, additionalFirstData);
                        }
                        else if (gridPosition.x < secondGridPosition.x)
                        {
                            isSupportPlacementValid = !selectedData.CanPlaceObjectAt(gridPosition + new Vector3Int(1, 0, 0), elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex, additionalFirstData);
                        }
                        if (!isSupportPlacementValid) return false;
                    }
                    else
                    {
                        if (gridPosition.x < secondGridPosition.x)
                        {
                            isSupportPlacementValid = !selectedData.CanPlaceObjectAt(secondGridPosition - new Vector3Int(1, 0, 0), elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex, additionalFirstData);
                        }
                        else if (gridPosition.x > secondGridPosition.x)
                        {
                            isSupportPlacementValid = !selectedData.CanPlaceObjectAt(secondGridPosition + new Vector3Int(1, 0, 0), elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex, additionalFirstData);
                        }
                        if (!isSupportPlacementValid) return false;
                    }

                    return (isPlacedOnSupport || isPlacedOnSideMount) ? true : false;

                }
            case 2:
                {
                    selectedData = GetGridDataOfType(selectedElementIndex);
                    additionalFirstData = GetGridDataOfType(0);
                    additionalSecondData = GetGridDataOfType(1);

                    bool isFirstElementPlacedOnSupport = selectedData.CanPlaceObjectAt(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex, additionalFirstData);
                    bool isFirstElementPlacedOnSpan = selectedData.CanPlaceObjectAt(gridPosition, elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex, additionalSecondData);
                    bool isSecondElementPlacedOnSupport = selectedData.CanPlaceObjectAt(secondGridPosition, elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex, additionalFirstData);
                    bool isSecondElementPlacedOnSpan = selectedData.CanPlaceObjectAt(secondGridPosition, elementDatabase.objectsData[selectedElementIndex].Size, selectedElementIndex, additionalSecondData);

                    if ((isFirstElementPlacedOnSpan && isSecondElementPlacedOnSpan) || (isFirstElementPlacedOnSupport && isSecondElementPlacedOnSupport))
                        return false;

                    return ((isFirstElementPlacedOnSupport || isFirstElementPlacedOnSpan) && (isSecondElementPlacedOnSpan || isSecondElementPlacedOnSupport)) ? true : false;
                }
            default:
                {
                    return false;
                }
        }

    }

    private GridData GetGridDataOfType(int selectedElementIndex)
    {
        GridData selectedData = new();
        switch (selectedElementIndex)
        {
            case (0):
                {
                    selectedData = SupportData;
                    break;
                }
            case (1):
                {
                    selectedData = SpanData;
                    break;
                }
            case (2):
                {
                    selectedData = ConnectorData;
                    break;
                }
            case (3):
                {
                    selectedData = GroundMountData;
                    break;
                }
            case (4):
                {
                    selectedData = SideMountData;
                    break;
                }
            default:
                {
                    break;
                }
        }

        return selectedData;
    }

    private void StopPlacement()
    {
        selectedElementIndex = -1;
        gridVisualization.SetActive(false);
        cellIndicator.SetActive(false);
        buildingInputManager.OnClicked -= PlaceElement;
        buildingInputManager.OnExit -= StopPlacement;
        bridgeGraph2.ValidateBridge();
    }

    private void Update()
    {
        if (selectedElementIndex < 0)
            return;
        Vector3 cursorPosition = buildingInputManager.GetSelectedMapPosition();
        Vector3Int gridPosition = grid.WorldToCell(cursorPosition);

        bool placementValidity = CheckPlacementValidity(gridPosition, selectedElementIndex);
        previewRenderer.material.color = placementValidity ? Color.white : Color.red;

        cursorIndicator.transform.position = cursorPosition;
        cellIndicator.transform.position = grid.CellToWorld(gridPosition);
    }
}
