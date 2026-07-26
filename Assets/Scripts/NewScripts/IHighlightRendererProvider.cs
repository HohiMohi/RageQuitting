using System.Collections.Generic;
using UnityEngine;

public interface IHighlightRendererProvider
{
    void GetHighlightRenderers(List<Renderer> renderers);
}
