using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Grid2 
{
    private int width;
    private int height;
    private float cellSize;
    private int[,] gridArray;
    private TextMesh[,] debugTextArray;

    public Grid2(int width, int height, float cellSize)
    {
        this.width = width;
        this.height = height;
        this.cellSize = cellSize;

        gridArray = new int[width, height];
        debugTextArray = new TextMesh[width, height];

        for (int x = 0; x < gridArray.GetLength(0); x++)
        {
            for (int y = 0; y < gridArray.GetLength(1); y++)
            {
                debugTextArray[x, y] = HelperUtilities.CreateWorldText(null, gridArray[x, y].ToString(), GetWorldPosition(x, y) + new Vector3(cellSize, cellSize) * 0.5f, 30, Color.white, TextAnchor.MiddleCenter, TextAlignment.Center, 0); ;
                Debug.DrawLine(GetWorldPosition(x, y), GetWorldPosition(x, y + 1), Color.white, 100f);
                Debug.DrawLine(GetWorldPosition(x, y), GetWorldPosition(x + 1, y), Color.white, 100f);

            }
        }
        Debug.DrawLine(GetWorldPosition(width, 0), GetWorldPosition(width, height), Color.white, 100f);
        Debug.DrawLine(GetWorldPosition(0, height), GetWorldPosition(width, height), Color.white, 100f);

        ModifyValue(2, 1, 10);
        ModifyValue(2, 1, -5);
    }

    private Vector3 GetWorldPosition(int x, int y)
    {
        return new Vector3(x, y) * cellSize;
    }

    private void GetXY(Vector3 worldPosition, out int x, out int y)
    {
        x = Mathf.FloorToInt(worldPosition.x / cellSize);
        y = Mathf.FloorToInt(worldPosition.y / cellSize);
    }


    public void SetValue(int x, int y, int value)
    {
        if (x >= 0 && y >= 0 && x < width && y < height)
        {
            gridArray[x, y] = value;
            UpdateText(x, y, gridArray[x, y].ToString());
        }
    }



    public void ModifyValue(int x, int y, int value)
    {
        if (x >= 0 && y >= 0 && x < width && y < height)
        {
            gridArray[x, y] += value;
            UpdateText(x, y, gridArray[x, y].ToString());
        }
    }

    public void ModifyValue(Vector3 worldPosition, int value)
    {
        int x, y;
        GetXY(worldPosition, out x, out y);

        ModifyValue(x, y, value);
    }

    public void UpdateText(int x, int y, string text)
    {
        debugTextArray[x, y].text = text;
    }
}
