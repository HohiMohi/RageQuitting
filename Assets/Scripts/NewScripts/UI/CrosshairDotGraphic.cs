using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasRenderer))]
public class CrosshairDotGraphic : MaskableGraphic
{
    [SerializeField, Min(1f)] private float diameter = 6f;
    [SerializeField, Min(0f)] private float outlineWidth = 1f;
    [SerializeField, Range(8, 64)] private int segments = 24;
    [SerializeField] private Color dotColor = Color.white;
    [SerializeField] private Color outlineColor = new Color(0f, 0f, 0f, 0.8f);

    public float Diameter
    {
        get => diameter;
        set
        {
            diameter = Mathf.Max(1f, value);
            SetVerticesDirty();
        }
    }

    public float OutlineWidth
    {
        get => outlineWidth;
        set
        {
            outlineWidth = Mathf.Max(0f, value);
            SetVerticesDirty();
        }
    }

    public Color DotColor
    {
        get => dotColor;
        set
        {
            dotColor = value;
            SetVerticesDirty();
        }
    }

    public Color OutlineColor
    {
        get => outlineColor;
        set
        {
            outlineColor = value;
            SetVerticesDirty();
        }
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();

        int safeSegments = Mathf.Clamp(segments, 8, 64);
        float innerRadius = diameter * 0.5f;
        float outerRadius = innerRadius + outlineWidth;

        AddCircle(vertexHelper, innerRadius, dotColor, safeSegments);
        if (outlineWidth > 0f)
        {
            AddRing(vertexHelper, innerRadius, outerRadius, outlineColor, safeSegments);
        }
    }

    private static void AddCircle(VertexHelper vertexHelper, float radius, Color32 color, int segmentCount)
    {
        int centerIndex = vertexHelper.currentVertCount;
        vertexHelper.AddVert(Vector3.zero, color, Vector2.zero);

        for (int i = 0; i <= segmentCount; i++)
        {
            float angle = Mathf.PI * 2f * i / segmentCount;
            vertexHelper.AddVert(
                new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius),
                color,
                Vector2.zero);
        }

        for (int i = 0; i < segmentCount; i++)
        {
            vertexHelper.AddTriangle(centerIndex, centerIndex + i + 1, centerIndex + i + 2);
        }
    }

    private static void AddRing(
        VertexHelper vertexHelper,
        float innerRadius,
        float outerRadius,
        Color32 color,
        int segmentCount)
    {
        int startIndex = vertexHelper.currentVertCount;
        for (int i = 0; i <= segmentCount; i++)
        {
            float angle = Mathf.PI * 2f * i / segmentCount;
            float x = Mathf.Cos(angle);
            float y = Mathf.Sin(angle);
            vertexHelper.AddVert(new Vector3(x * innerRadius, y * innerRadius), color, Vector2.zero);
            vertexHelper.AddVert(new Vector3(x * outerRadius, y * outerRadius), color, Vector2.zero);
        }

        for (int i = 0; i < segmentCount; i++)
        {
            int innerA = startIndex + i * 2;
            int outerA = innerA + 1;
            int innerB = innerA + 2;
            int outerB = innerA + 3;
            vertexHelper.AddTriangle(innerA, outerA, outerB);
            vertexHelper.AddTriangle(innerA, outerB, innerB);
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        diameter = Mathf.Max(1f, diameter);
        outlineWidth = Mathf.Max(0f, outlineWidth);
        segments = Mathf.Clamp(segments, 8, 64);
        raycastTarget = false;
        SetVerticesDirty();
    }
#endif
}
