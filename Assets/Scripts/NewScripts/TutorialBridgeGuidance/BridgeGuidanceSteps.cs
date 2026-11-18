using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stable catalog keys and default English help. Author Stage02 book steps with these keys;
/// a future assigned key resolves to the site's current prerequisite, never skips gameplay.
/// Empty/unknown keys preserve old books and simply do not participate in tutorial assignment selection.
/// </summary>
public static class BridgeGuidanceSteps
{
    public static string Kind(BridgeConstructionSite site) => site is BridgeDeckPanelConstructionSite ? "panel"
        : site is BridgeAbutmentConstructionSite ? "abutment" : site is BridgeGirderConstructionSite ? "girder"
        : site is BridgeCrossBeamConstructionSite ? "crossbeam" : site is BridgeDiagonalBracingConstructionSite ? "brace" : "foundation";

    public static void CopyAuthoringKeys(BridgeConstructionSite site, List<string> keys)
    {
        CopyAuthoringKeys(Kind(site), site.RequiredDiggingCycles, keys);
    }

    public static void CopyAuthoringKeys(string kind, int diggingCycles, List<string> keys)
    {
        keys.Clear();
        switch (kind)
        {
            case "foundation":
                keys.Add("foundation.clear");
                for (int cycle = 1; cycle <= Mathf.Max(1, diggingCycles); cycle++)
                {
                    keys.Add($"foundation.soften.{cycle}");
                    keys.Add($"foundation.collect.{cycle}");
                }
                keys.AddRange(new[] { "foundation.concrete", "foundation.dry", "foundation.mount", "foundation.hammer" });
                break;
            case "abutment":
                keys.AddRange(new[] { "abutment.mount", "abutment.level", "abutment.anchor", "abutment.backfill" });
                break;
            case "girder":
                keys.AddRange(new[] { "girder.mount", "girder.level", "girder.fasten" });
                break;
            case "crossbeam":
                keys.AddRange(new[] { "crossbeam.mount", "crossbeam.align", "crossbeam.clamp", "crossbeam.fasten" });
                break;
            case "brace":
                keys.AddRange(new[] { "brace.mount", "brace.align", "brace.fix", "brace.fasten" });
                break;
            case "panel":
                keys.AddRange(new[] { "panel.place", "panel.nail" });
                break;
        }
    }

    public static bool IsComplete(BridgeConstructionSite site)
    {
        if (site == null)
            return false;
        if (site is BridgeDeckPanelConstructionSite panel && panel.UsesPhysicalPanelPlacement)
            return panel.IsComplete || panel.DeckSection != null && panel.DeckSection.PhysicalPanelState != null &&
                panel.DeckSection.PhysicalPanelState.IsSlotComplete(panel.SlotIndex);
        return site.CurrentStage == BridgeConstructionStage.Complete || site.BridgeComponent != null && site.BridgeComponent.IsAssembled;
    }

    public static bool IsWaiting(BridgeConstructionSite site) => site.CurrentStage is
        BridgeConstructionStage.WaitingForFoundation or BridgeConstructionStage.WaitingForSupports or
        BridgeConstructionStage.WaitingForGirders or BridgeConstructionStage.WaitingForCrossBeams or BridgeConstructionStage.WaitingForPrevious;

    public static bool IsActionable(BridgeConstructionSite site) => site != null && !IsComplete(site) &&
        !IsWaiting(site) && site.CurrentStage != BridgeConstructionStage.ConcreteDrying;

    public static bool IsAssignedStepIncomplete(IReadOnlyList<string> orderedKeys, string currentKey,
        string assignedKey, bool waitingForPrerequisite)
    {
        if (string.IsNullOrEmpty(assignedKey))
            return false;
        int current = -1;
        int assigned = -1;
        for (int i = 0; i < orderedKeys.Count; i++)
        {
            if (orderedKeys[i] == currentKey)
                current = i;
            if (orderedKeys[i] == assignedKey)
                assigned = i;
        }
        if (current < 0 && waitingForPrerequisite)
            current = 0;
        return current >= 0 && assigned >= current;
    }

    public static BridgeGuidanceInstruction Read(BridgeConstructionSite site)
    {
        if (site == null || IsComplete(site))
            return default;
        string kind = Kind(site), key;
        BridgeComponentSO definition = Definition(site);
        BridgeAbutmentConstructionWorkflowSO abutmentSettings = definition != null ? definition.abutmentConstructionWorkflow : null;
        BridgeGirderConstructionWorkflowSO girderSettings = definition != null ? definition.girderConstructionWorkflow : null;
        BridgeCrossBeamConstructionWorkflowSO crossBeamSettings = definition != null ? definition.crossBeamConstructionWorkflow : null;
        BridgeDiagonalBracingConstructionWorkflowSO braceSettings = definition != null ? definition.diagonalBracingConstructionWorkflow : null;
        float progress = Fraction(site.CurrentWorkProgress, site.RequiredWorkProgress);
        if (site is BridgeDeckPanelConstructionSite panel && panel.UsesPhysicalPanelPlacement)
        {
            BridgeDeckPanelSectionNetworkState state = panel.DeckSection != null ? panel.DeckSection.PhysicalPanelState : null;
            if (state != null && state.TryGetSlotAssessment(panel.SlotIndex, out BridgeDeckPanelReplicatedState assessment))
            {
                bool correct = assessment.Failure == (byte)BridgeDeckPanelPlacementFailure.None ||
                    assessment.Failure == (byte)BridgeDeckPanelPlacementFailure.MissingQuarters;
                int quarters = 0;
                for (int i = 0; i < 4; i++)
                    if ((assessment.QuarterMask & (1 << i)) != 0)
                        quarters++;
                return Describe(site, correct ? "panel.nail" : "panel.place", quarters / 4f);
            }
            return Describe(site, "panel.place", 0f);
        }
        switch (site.CurrentStage)
        {
            case BridgeConstructionStage.Clearing:
                key = "foundation.clear";
                progress = 0f;
                break;
            case BridgeConstructionStage.Digging:
                bool collect = site.DiggingSubstage != FoundationDiggingSubstage.Loosening;
                key = $"foundation.{(collect ? "collect" : "soften")}.{site.DiggingCycleIndex + 1}";
                break;
            case BridgeConstructionStage.ConcretePouring:
                key = "foundation.concrete";
                progress = Fraction(site.ConcreteLoadsPoured, site.RequiredConcreteLoads);
                break;
            case BridgeConstructionStage.ConcreteDrying:
                key = "foundation.dry";
                progress = Fraction(site.ConcreteDryingDuration - site.RemainingConcreteDryingTime, site.ConcreteDryingDuration);
                break;
            case BridgeConstructionStage.ReadyForMount:
                key = kind + ".mount";
                progress = 0f;
                break;
            case BridgeConstructionStage.Hammering:
                key = kind + ".hammer";
                break;
            case BridgeConstructionStage.Leveling:
                key = kind + ".level";
                progress = 0f;
                break;
            case BridgeConstructionStage.Anchoring:
                key = "abutment.anchor";
                if (site is BridgeAbutmentConstructionSite abutment)
                    progress = Mean(abutment.AnchorProgress,
                    abutmentSettings != null ? abutmentSettings.AnchorProgressNeeded : 100f);
                break;
            case BridgeConstructionStage.Backfilling:
                key = "abutment.backfill";
                break;
            case BridgeConstructionStage.Aligning:
                key = kind + ".align";
                progress = 0f;
                break;
            case BridgeConstructionStage.Clamping:
                key = "crossbeam.clamp";
                if (site is BridgeCrossBeamConstructionSite cross)
                    progress = Fraction(cross.LeftClampProgress + cross.RightClampProgress,
                    2f * (crossBeamSettings != null ? crossBeamSettings.ClampProgressNeeded : 100f));
                break;
            case BridgeConstructionStage.TemporaryFixing:
                key = "brace.fix";
                if (site is BridgeDiagonalBracingConstructionSite brace)
                    progress = Fraction(brace.StartTemporaryFixProgress + brace.EndTemporaryFixProgress,
                    2f * (braceSettings != null ? braceSettings.TemporaryFixProgressNeeded : 100f));
                break;
            case BridgeConstructionStage.Fastening:
                key = kind + ".fasten";
                if (site is BridgeGirderConstructionSite girder)
                    progress = Mean(girder.FastenerProgress, girderSettings != null ? girderSettings.FastenerProgressNeeded : 100f);
                else if (site is BridgeCrossBeamConstructionSite beam)
                    progress = Mean(beam.FastenerProgress, crossBeamSettings != null ? crossBeamSettings.FastenerProgressNeeded : 100f);
                else if (site is BridgeDiagonalBracingConstructionSite bracing)
                    progress = Mean(bracing.FastenerProgress, braceSettings != null ? braceSettings.FastenerProgressNeeded : 100f);
                break;
            default:
                key = kind + ".wait";
                progress = 0f;
                break;
        }
        return Describe(site, key, progress);
    }

    public static BridgeGuidanceInstruction Describe(BridgeConstructionSite site, string key, float progress = 0f)
    {
        int id = site != null && site.BridgeComponent != null ? site.BridgeComponent.ComponentID : -1;
        BridgeComponentSO definition = Definition(site);
        string name = definition != null ? definition.componentName : "Bridge work";
        DefaultText(key, out string text, out string help, out string tool, out string action);
        BridgeGuidanceActions actions = action == "Action" ? BridgeGuidanceActions.Primary
            : action == "ActionAlt" ? BridgeGuidanceActions.Secondary
            : action == "Interact" ? BridgeGuidanceActions.Interact : BridgeGuidanceActions.None;
        if (key.EndsWith(".level", StringComparison.Ordinal))
            actions |= BridgeGuidanceActions.Primary;
        if (key == "foundation.clear")
            actions |= BridgeGuidanceActions.Primary;
        if (key == "panel.place" || key == "panel.nail")
            actions |= BridgeGuidanceActions.PlacementToggle |
            BridgeGuidanceActions.PlacementConfirm | BridgeGuidanceActions.PlacementRotation | BridgeGuidanceActions.Interact | BridgeGuidanceActions.Secondary;
        float current = site != null ? site.CurrentWorkProgress : 0f;
        float required = site != null ? site.RequiredWorkProgress : 0f;
        float seconds = 0f;
        if (site != null && site.CurrentStage == BridgeConstructionStage.Digging)
        {
            text += $" (cycle {site.DiggingCycleIndex + 1}/{site.RequiredDiggingCycles})";
            if (site.DiggingSubstage != FoundationDiggingSubstage.Loosening)
            {
                required = site.RequiredSoilUnits;
                current = Mathf.Clamp(site.RemovedSoilUnits - site.DiggingCycleIndex * required, 0f, required);
                progress = Fraction(current, required);
            }
        }
        if (key.EndsWith(".level", StringComparison.Ordinal) || key.EndsWith(".align", StringComparison.Ordinal) ||
            key.EndsWith(".mount", StringComparison.Ordinal) || key.EndsWith(".wait", StringComparison.Ordinal))
        {
            current = 0f;
            required = 0f;
        }
        if (site != null && key == "foundation.clear")
        {
            text += $" ({site.RemainingObstacleCount} obstacles remaining)";
            current = 0f;
            required = 0f;
        }
        if (key == "abutment.anchor" || key == "girder.fasten" || key == "crossbeam.fasten" || key == "brace.fasten")
        {
            required = 4f;
            current = progress * required;
        }
        if (key == "crossbeam.clamp" || key == "brace.fix")
        {
            required = 2f;
            current = progress * required;
        }
        if (site != null && key == "foundation.recover")
        {
            current = site.FailedConcreteBreakProgress;
            required = site.FailedConcreteWorkRequired;
        }
        if (key == "panel.nail")
        {
            current = Mathf.RoundToInt(progress * 4f);
            required = 4f;
        }
        if (site != null && key == "foundation.dry")
        {
            current = site.ConcreteDryingDuration - site.RemainingConcreteDryingTime;
            required = site.ConcreteDryingDuration;
            seconds = Mathf.Ceil(site.RemainingConcreteDryingTime);
            text = $"Wait for concrete to dry ({seconds:0} s)";
            help += $" Drying duration: {site.ConcreteDryingDuration:0.#} seconds.";
        }
        if (site != null && key == "foundation.concrete")
        {
            current = site.ConcreteLoadsPoured;
            required = site.RequiredConcreteLoads;
        }
        if (site is BridgeGirderConstructionSite girder && key == "girder.fasten")
        {
            seconds = Mathf.Ceil(girder.RemainingFastenerPairTime);
            if (girder.IsFastenerPairWindowActive)
                text = $"Strike the diagonal partner ({seconds:0} s)";
            help += $" Pair window: {girder.FastenerPairWindowDuration:0.#} seconds.";
        }
        return new BridgeGuidanceInstruction(id, name, key, progress, text, help, tool, action,
            current, required, seconds, actions);
    }

    public static void DefaultText(string key, out string text, out string help, out string tool, out string action)
    {
        tool = "";
        action = "";
        if (key.StartsWith("foundation.soften.", StringComparison.Ordinal))
        {
            text = "Soften the next soil layer";
            help = "Use the Shovel on this foundation until the current layer is loosened, then remove its soil. Alternate softening and collection for all configured cycles.";
            tool = "Shovel";
            action = "Action";
            return;
        }
        if (key.StartsWith("foundation.collect.", StringComparison.Ordinal))
        {
            text = "Collect the loosened soil";
            help = "Carry a bucket into the loosened soil; walking through it collects automatically. Hold the alternate action when using the bucket action. When full, leave the excavation and empty it outside, then return. Complete this collection cycle before softening again.";
            tool = "Bucket";
            action = "ActionAlt";
            return;
        }
        if (key.EndsWith(".mount", StringComparison.Ordinal))
        {
            text = "Carry the part into its ghost and hold steady";
            help = "Carry the matching part together when needed. Move it into the ghost, match its position and rotation and keep it still until the socket mounts it.";
            action = "Interact";
            return;
        }
        if (key.EndsWith(".level", StringComparison.Ordinal))
        {
            text = "Measure both axes, adjust, then confirm";
            help = "Use the Spirit Level at the length and width measurement points. Read its visible result, adjust the appropriate points with the Industrial Hammer and measure again. Confirm leveling with interact when both axes are correct.";
            tool = "Spirit Level / Industrial Hammer";
            action = "Interact";
            return;
        }
        switch (key)
        {
            case "foundation.clear":
                text = "Clear the marked foundation site";
                help = "Use the Axe for wooden obstacles in the marked site. Work together to release any flexible sapling, then clear the area before digging.";
                tool = "Axe";
                action = "Interact";
                break;
            case "foundation.concrete":
                text = "Bring ready concrete and pour together";
                help = "Use the provided ready concrete batch. Dock an empty wheelbarrow at the mixer, load it, transport it to this foundation and dock it. Take the two pour slots, wait for a partner and coordinate the pouring controls. No material gathering or mixer recipe is needed.";
                action = "Interact";
                break;
            case "foundation.dry":
                text = "Wait for the concrete to dry";
                help = "You can help at another site while the concrete dries. Mount the foundation part after it becomes ready.";
                break;
            case "foundation.hammer":
                text = "Hammer the mounted foundation";
                help = "Use the Industrial Hammer on the mounted foundation until its assembly is complete.";
                tool = "Industrial Hammer";
                action = "Action";
                break;
            case "foundation.recover":
                text = "Break the failed concrete";
                help = "Use the Pickaxe on the hardened failed concrete. After it breaks, move the same trapped wheelbarrow out of the excavation, then reload and retry the pour.";
                tool = "Pickaxe";
                action = "Action";
                break;
            case "foundation.exit":
                text = "Move the same wheelbarrow out";
                help = "Remove the wheelbarrow from this excavation to finish recovery. Keep using this wheelbarrow; once the site resets, load ready concrete and retry.";
                action = "Interact";
                break;
            case "foundation.failure-wait":
                text = "Wait for failed concrete recovery";
                help = "Wait until the failure sequence settles. Break hardened failed concrete with the Pickaxe when it becomes available, then move the same wheelbarrow out to reset the site.";
                break;
            case "abutment.anchor":
                text = "Hammer all four anchors";
                help = "Work around the abutment and hammer each of its four anchors until every anchor is secure.";
                tool = "Industrial Hammer";
                action = "Action";
                break;
            case "abutment.backfill":
                text = "Backfill the abutment";
                help = "Use the Shovel at the abutment's backfill points until backfilling is complete.";
                tool = "Shovel";
                action = "Action";
                break;
            case "girder.fasten":
                text = "Strike the diagonal fastener pairs";
                help = "Use the Industrial Hammer. After starting a fastener, strike its diagonally opposite partner before the pair timer expires. Coordinate both pairs with your partner.";
                tool = "Industrial Hammer";
                action = "Action";
                break;
            case "crossbeam.align":
                text = "Hammer the cross beam into alignment";
                help = "Strike the indicated alignment points from opposed directions to correct the cross beam's offset. Continue until alignment is accepted.";
                tool = "Industrial Hammer";
                action = "Action";
                break;
            case "crossbeam.clamp":
                text = "Alternate between the two clamps";
                help = "Use the Wrench on the left and right clamps alternately. Keep their progress close; a clamp cannot advance too far ahead of the other.";
                tool = "Wrench";
                action = "Action";
                break;
            case "crossbeam.fasten":
                text = "Tighten the cross beam fasteners";
                help = "Use the Wrench on every indicated fastener until the cross beam is complete.";
                tool = "Wrench";
                action = "Action";
                break;
            case "brace.align":
                text = "Hammer the brace to the right angle";
                help = "Strike the brace's angle adjustment points with the Industrial Hammer until alignment is accepted.";
                tool = "Industrial Hammer";
                action = "Action";
                break;
            case "brace.fix":
                text = "Temporarily fix both brace ends";
                help = "Use the Industrial Hammer at the start and end fixing points. Secure both ends before tightening the fasteners.";
                tool = "Industrial Hammer";
                action = "Action";
                break;
            case "brace.fasten":
                text = "Tighten the brace in the indicated order";
                help = "Use the Wrench and follow the highlighted fastener order: start top, end bottom, start bottom, end top.";
                tool = "Wrench";
                action = "Action";
                break;
            case "panel.place":
                text = "Place the panel by hand and assess its pose";
                help = "Pick up the physical panel, use hand placement over its marked slot and release it to assess the pose. Correct its position, height and orientation. For a bad nailed pose, use the Crowbar to remove the nails and reset the panel before trying again.";
                action = "Interact";
                break;
            case "panel.nail":
                text = "Secure all four panel quarters";
                help = "Place a separate Mounting Nail through the panel into a main girder in each quarter. Each nail must connect both objects and receive its configured number of Industrial Hammer strikes. Nails on the quarter boundaries or into the wrong object do not count. Remove incorrect nails with the Crowbar.";
                tool = "Mounting Nail / Industrial Hammer";
                action = "Action";
                break;
            default:
                text = "Complete the supporting work first";
                help = "This part is waiting for its supporting bridge components. Follow the actionable prerequisite, then return to this part.";
                break;
        }
    }
    private static BridgeComponentSO Definition(BridgeConstructionSite site) => site != null && site.BridgeComponent != null ? site.BridgeComponent.GetBridgeComponentSO() : null;
    private static float Fraction(float current, float required) => required > 0f ? Mathf.Clamp01(current / required) : 0f;
    private static float Mean(IReadOnlyList<float> progress, float required)
    {
        if (progress == null || progress.Count == 0)
            return 0f;
        float sum = 0f;
        foreach (float value in progress)
            sum += Fraction(value, required);
        return sum / progress.Count;
    }
}
