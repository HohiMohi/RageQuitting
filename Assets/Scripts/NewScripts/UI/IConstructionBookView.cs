using System;
using System.Collections.Generic;

/// <summary>
/// A surface the <see cref="ConstructionBookController"/> can draw the current spread on: the world-space pages of the
/// physical book (<see cref="ConstructionBookView"/>) or the full-screen book overlay (<see cref="ConstructionBookScreenView"/>).
/// </summary>
public interface IConstructionBookView
{
    void RenderWithCompletionSummary(ConstructionBookCatalogSO.Entry entry, int count, int pageIndex, int pageCount,
        IReadOnlyList<ConstructionBookMaterialLine> materials, bool materialsAvailable,
        IReadOnlyList<ConstructionBookRosterMember> roster, ulong localClientId,
        Func<int, int, ulong, bool> isAssigned, Action<int, int, ulong, bool> requestAssignment,
        Func<bool> isPageTransitioning, bool worldView, string completionSummary, bool perStockPlate = false);
}
