using UnityEngine;

/// <summary>Temporarily suppresses only the owner-local rendering of a held object.</summary>
public sealed class SingleCarryPlacementHeldVisual
{
    private readonly Renderer[] renderers;
    private readonly bool[] previousForceRenderingOff;
    private bool disposed;

    public SingleCarryPlacementHeldVisual(GameObject heldObject)
    {
        renderers = heldObject != null ? heldObject.GetComponentsInChildren<Renderer>(true) : System.Array.Empty<Renderer>();
        previousForceRenderingOff = new bool[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null) continue;
            previousForceRenderingOff[i] = renderer.forceRenderingOff;
            renderer.forceRenderingOff = true;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer != null) renderer.forceRenderingOff = previousForceRenderingOff[i];
        }
    }
}
