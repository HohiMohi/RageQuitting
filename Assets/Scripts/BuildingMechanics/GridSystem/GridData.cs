using System;
using System.Collections.Generic;
using UnityEngine;


public class GridData
{
    Dictionary<Vector3Int, PlacementData> placedElements = new();

    public void AddElementAt(Vector3Int gridPosition,
                             Vector2Int objectSize,
                             int ID,
                             int objectIndex)
    {
        List<Vector3Int> positionToOccupy = CalculatePositions(gridPosition, objectSize);
        PlacementData data = new PlacementData(positionToOccupy, ID, objectIndex);
        foreach (var pos in positionToOccupy)
        {
            if (placedElements.ContainsKey(pos))
                throw new Exception($"Dictionary already contains this cell position {pos}");
            placedElements[pos] = data;
        }
    }

    private List<Vector3Int> CalculatePositions(Vector3Int gridPosition, Vector2Int objectSize)
    {
        List<Vector3Int> returnVal = new();
        for (int x = 0; x < objectSize.x; x++)
        {
            for (int y = 0; y < objectSize.y; y++)
            {
                returnVal.Add(gridPosition + new Vector3Int(x, y, 0));
            }
        }

        return returnVal;
    }

    public bool CanPlaceObjectAt(Vector3Int gridPosition, Vector2Int objectSize, int elementID)
    {
        switch (elementID)
        {
            case 3://Ground Mount
                {
                    List<Vector3Int> positionToOccupy = CalculatePositions(gridPosition, objectSize);
                    foreach (var pos in positionToOccupy)
                    {
                        if (placedElements.ContainsKey(pos) || (gridPosition.y != -5)) // Do zmiany, przerobiæ na pobieranie rozmiaru grida a nie hardcode
                            return false;
                    }

                    return true;
                }
            case 4://Side Mount
                {
                    List<Vector3Int> positionToOccupy = CalculatePositions(gridPosition, objectSize);
                    foreach (var pos in positionToOccupy)
                    {
                        if (placedElements.ContainsKey(pos) || ((gridPosition.x != -5 && gridPosition.x != 4))) // Do zmiany, przerobiæ na pobieranie rozmiaru grida a nie hardcode
                            return false;
                    }

                    return true;
                }
            default:
                {
                    List<Vector3Int> positionToOccupy = CalculatePositions(gridPosition, objectSize);
                    foreach (var pos in positionToOccupy)
                    {
                        if (placedElements.ContainsKey(pos))
                            return false;
                    }

                    return true;
                }
        }

    }

    public bool CanPlaceObjectAt(Vector3Int gridPosition, Vector2Int objectSize, int elementID, GridData additionalGridData)
    {
        switch (elementID)
        {
            case 0:
                {
                    if (additionalGridData.placedElements.ContainsKey(gridPosition) && !additionalGridData.placedElements.ContainsKey(gridPosition - new Vector3Int(1, 0, 0)))
                    {
                        if ((additionalGridData.placedElements[gridPosition].ID == 3) && (CanPlaceObjectAt(gridPosition, objectSize, elementID)))
                            return true;
                    }
                    return false;
                }
            case 1:
                {
                    if(additionalGridData.placedElements.ContainsKey(gridPosition))
                    {
                        if ((additionalGridData.placedElements[gridPosition].ID == 0 || additionalGridData.placedElements[gridPosition].ID == 4) && (CanPlaceObjectAt(gridPosition, objectSize, elementID)))
                            return true;
                    }

                    return false;
                }
            case 2:
                {
                    if (additionalGridData.placedElements.ContainsKey(gridPosition))
                    {
                        if ((additionalGridData.placedElements[gridPosition].ID == 0 || additionalGridData.placedElements[gridPosition].ID == 1) && (CanPlaceObjectAt(gridPosition, objectSize, elementID)))
                            return true;
                    }
                    return false;
                }

            default:
                break;
        }
        return true;
    }
    public bool CanPlaceObjectAt(Vector3Int gridPosition, Vector3Int gridSecondPosition, Vector2Int objectSize, int elementID, GridData additionalGridData, GridData additionalSecondGridData)
    {
        switch (elementID)
        {
            case 2:
                {
                    if (additionalGridData.placedElements.ContainsKey(gridPosition))
                    {
                        if ((additionalGridData.placedElements[gridPosition].ID == 0) && (CanPlaceObjectAt(gridPosition, objectSize, elementID)))
                            return true;
                    }
                    if (additionalSecondGridData.placedElements.ContainsKey(gridPosition))
                    {
                        if ((additionalGridData.placedElements[gridPosition].ID == 1) && (CanPlaceObjectAt(gridPosition, objectSize, elementID)))
                            return true;
                    }

                    return false;
                }
            default:
                break;
        }
        return true;
    }
}


public class PlacementData
{
    public List<Vector3Int> occupiedPositions;

    public int ID { get; private set; }
    public int PlacedObjectIndex { get; private set; }

    public PlacementData(List<Vector3Int> occupiedPositions, int iD, int placedObjectIndex)
    {
        this.occupiedPositions = occupiedPositions;
        ID = iD;
        PlacedObjectIndex = placedObjectIndex;
    }
}