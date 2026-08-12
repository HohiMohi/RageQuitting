using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(RopeToolController))]
public sealed class RopeThrowTrajectoryPreview : MonoBehaviour
{
    private readonly List<Vector3> points = new List<Vector3>(48);
    private readonly RaycastHit[] hitBuffer = new RaycastHit[24];
    private RopeToolController rope;
    private LineRenderer trajectoryLine;
    private GameObject landingMarker;
    private Renderer landingMarkerRenderer;
    private Material trajectoryMaterial;
    private Material markerMaterial;

    private void Awake()
    {
        rope = GetComponent<RopeToolController>();
        BuildVisuals();
        SetVisible(false);
    }

    private void LateUpdate()
    {
        if (rope == null || !rope.CanRenderThrowPreviewLocally() || rope.ActiveProfile == null)
        {
            SetVisible(false);
            return;
        }

        DrawPrediction(rope.ActiveProfile);
    }

    private void DrawPrediction(RopeToolProfileSO profile)
    {
        points.Clear();
        Vector3 ropeOrigin = rope.ThrowPreviewStartPosition - rope.ThrowPreviewDirection * 0.25f;
        Vector3 position = rope.ThrowPreviewStartPosition;
        Vector3 velocity = rope.ThrowPreviewDirection * rope.PredictedThrowSpeed;
        float maximumLength = Mathf.Max(0.01f, rope.PredictedThrowLength);
        float radius = Mathf.Max(0.01f, rope.GetEndpointCollisionRadius());
        float step = Mathf.Max(0.005f, profile.trajectoryPreviewTimeStep);
        int segmentCount = Mathf.Max(4, profile.trajectoryPreviewSegments);
        bool terminated = false;
        bool hitGeometry = false;

        points.Add(position);
        for (int i = 0; i < segmentCount; i++)
        {
            Vector3 nextVelocity = velocity + Physics.gravity * step;
            Vector3 nextPosition = position + velocity * step + Physics.gravity * (0.5f * step * step);
            bool reachedLength = TryClampToRopeLength(ropeOrigin, position, nextPosition, maximumLength,
                out Vector3 lengthLimitedPosition);
            if (reachedLength)
            {
                nextPosition = lengthLimitedPosition;
            }

            if (TryFindFirstHit(position, nextPosition, radius, profile.obstructionMask, out RaycastHit hit))
            {
                nextPosition = hit.point + hit.normal * radius;
                hitGeometry = true;
                terminated = true;
            }
            else if (reachedLength)
            {
                terminated = true;
            }

            points.Add(nextPosition);
            position = nextPosition;
            velocity = nextVelocity;
            if (terminated)
            {
                break;
            }
        }

        trajectoryLine.enabled = points.Count > 1;
        trajectoryLine.positionCount = points.Count;
        trajectoryLine.startWidth = trajectoryLine.endWidth = profile.trajectoryPreviewLineWidth;
        trajectoryLine.startColor = trajectoryLine.endColor = profile.trajectoryPreviewColor;
        trajectoryLine.SetPositions(points.ToArray());

        landingMarker.SetActive(points.Count > 1);
        if (points.Count > 1)
        {
            float diameter = profile.trajectoryPreviewMarkerRadius * 2f;
            landingMarker.transform.position = points[points.Count - 1];
            landingMarker.transform.localScale = Vector3.one * diameter;
            markerMaterial.color = hitGeometry ? profile.trajectoryPreviewHitColor : profile.trajectoryPreviewColor;
        }
    }

    private bool TryFindFirstHit(Vector3 start, Vector3 end, float radius, LayerMask mask, out RaycastHit closestHit)
    {
        closestHit = default;
        Vector3 delta = end - start;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return false;
        }

        int hitCount = Physics.SphereCastNonAlloc(start, radius, delta / distance, hitBuffer, distance, mask,
            QueryTriggerInteraction.Ignore);
        float closestDistance = float.PositiveInfinity;
        bool found = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = hitBuffer[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform))
            {
                continue;
            }

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestHit = hit;
                found = true;
            }
        }
        return found;
    }

    private static bool TryClampToRopeLength(Vector3 origin, Vector3 start, Vector3 end, float maximumLength,
        out Vector3 clamped)
    {
        clamped = end;
        if ((end - origin).sqrMagnitude <= maximumLength * maximumLength)
        {
            return false;
        }

        Vector3 segment = end - start;
        Vector3 fromOrigin = start - origin;
        float a = Vector3.Dot(segment, segment);
        float b = 2f * Vector3.Dot(fromOrigin, segment);
        float c = Vector3.Dot(fromOrigin, fromOrigin) - maximumLength * maximumLength;
        float discriminant = b * b - 4f * a * c;
        if (a <= 0.000001f || discriminant < 0f)
        {
            clamped = origin + (end - origin).normalized * maximumLength;
            return true;
        }

        float root = Mathf.Sqrt(discriminant);
        float t = Mathf.Clamp01((-b + root) / (2f * a));
        clamped = Vector3.Lerp(start, end, t);
        return true;
    }

    private void BuildVisuals()
    {
        GameObject lineObject = new GameObject("RopeThrowTrajectory");
        lineObject.layer = LayerMask.NameToLayer("Ignore Raycast");
        lineObject.transform.SetParent(transform, false);
        trajectoryLine = lineObject.AddComponent<LineRenderer>();
        trajectoryLine.useWorldSpace = true;
        trajectoryLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trajectoryLine.receiveShadows = false;
        trajectoryLine.textureMode = LineTextureMode.Stretch;
        trajectoryMaterial = new Material(Shader.Find("Sprites/Default"));
        trajectoryLine.material = trajectoryMaterial;

        landingMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        landingMarker.name = "RopeThrowLandingMarker";
        landingMarker.layer = LayerMask.NameToLayer("Ignore Raycast");
        landingMarker.transform.SetParent(transform, false);
        Collider markerCollider = landingMarker.GetComponent<Collider>();
        if (markerCollider != null)
        {
            markerCollider.enabled = false;
            Destroy(markerCollider);
        }
        landingMarkerRenderer = landingMarker.GetComponent<Renderer>();
        landingMarkerRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        landingMarkerRenderer.receiveShadows = false;
        markerMaterial = new Material(Shader.Find("Sprites/Default"));
        landingMarkerRenderer.material = markerMaterial;
    }

    private void SetVisible(bool visible)
    {
        if (trajectoryLine != null) trajectoryLine.enabled = visible;
        if (landingMarker != null) landingMarker.SetActive(visible);
    }

    private void OnDestroy()
    {
        if (trajectoryMaterial != null) Destroy(trajectoryMaterial);
        if (markerMaterial != null) Destroy(markerMaterial);
    }
}
