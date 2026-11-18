using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Opt-in, owner-local observer. Instantiate only from the Stage02 scene's tutorial wiring.
/// Tick from its local presenter; this object never requests work or writes gameplay/network state.
/// </summary>
public sealed class BridgeGuidanceSource : IDisposable
{
    public const float ReadInterval = 0.2f;
    private readonly GameplayManager manager;
    private readonly PlayerInteractionNew interaction;
    private readonly PlayerActionController actions;
    private readonly PlayerWheelbarrowController wheelbarrowControls;
    private readonly PlayerSpiritLevelController spiritLevel;
    private readonly PlayerHealth health;
    private readonly PlayerSingleCarryPlacementController placement;
    private readonly NetworkObject playerNetworkObject;
    private readonly List<BridgeComponent> components = new List<BridgeComponent>();
    private readonly List<BridgeGuidanceBookStep> bookSteps = new List<BridgeGuidanceBookStep>();
    private readonly List<BridgeGuidanceCandidate> candidates = new List<BridgeGuidanceCandidate>();
    private readonly List<BridgeComponent> prerequisites = new List<BridgeComponent>();
    private readonly List<string> keys = new List<string>();
    private ConstructionBookController book;
    private BridgeConstructionSite rememberedSite, actionSite;
    private WheelbarrowController usedWheelbarrow;
    private ConcreteMixerController usedMixer;
    private BridgeDeckPanelMounting usedPanel;
    private MountingNail usedNail;
    private Vector3 rememberedInteractionPoint;
    private PortableSubstanceContainer observedBucket;
    private float observedBucketUnits;
    private float nextRead;
    private bool dirty = true, disposed;
    public BridgeGuidanceSnapshot Snapshot
    {
        get; private set;
    }
    public event Action<BridgeGuidanceSnapshot> Changed;

    public BridgeGuidanceSource(GameplayManager manager, PlayerInteractionNew localInteraction)
    {
        this.manager = manager;
        interaction = localInteraction;
        if (interaction != null)
        {
            interaction.TryGetComponent(out actions);
            interaction.TryGetComponent(out wheelbarrowControls);
            interaction.TryGetComponent(out spiritLevel);
            interaction.TryGetComponent(out health);
            interaction.TryGetComponent(out placement);
            interaction.TryGetComponent(out playerNetworkObject);
            interaction.LocalInteractionTargetObserved += ObserveLocalTarget;
            interaction.OnHeldObjectChanged += Invalidate;
            if (actions != null)
            {
                actions.LocalActionTargetObserved += ObserveActionTarget;
                actions.OnToolActionStarted += OnActionStarted;
            }
            if (health != null)
                health.OnDownedStateChanged += OnDownedChanged;
        }
        if (manager != null)
        {
            manager.OnBridgeRequirementsChanged += Invalidate;
            manager.OnConstructionStageChanged += OnStageChanged;
            manager.ConstructionBookAvailabilityChanged += OnBookAvailable;
            SetBook(manager.RegisteredConstructionBook);
        }
    }

    public void Tick(float unscaledTime)
    {
        if (disposed || !dirty && unscaledTime < nextRead)
            return;
        dirty = false;
        nextRead = unscaledTime + ReadInterval;
        BridgeGuidanceSnapshot next = Read();
        dirty = false;
        if (Snapshot.Equals(next))
            return;
        Snapshot = next;
        Changed?.Invoke(next);
    }

    public void ResetLocalContext()
    {
        rememberedSite = null;
        actionSite = null;
        usedWheelbarrow = null;
        usedMixer = null;
        usedPanel = null;
        usedNail = null;
        observedBucket = null;
        dirty = true;
    }

    private bool IsLocal => interaction != null && interaction.isActiveAndEnabled &&
        (playerNetworkObject != null && playerNetworkObject.IsSpawned ? playerNetworkObject.IsOwner
            : NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening);

    private BridgeGuidanceSnapshot Read()
    {
        if (!IsLocal || manager == null || !manager.IsBridgeProgressInitialized)
        {
            ResetLocalContext();
            return default;
        }
        manager.CopyGuidanceComponents(components);
        if (book != manager.RegisteredConstructionBook)
            SetBook(manager.RegisteredConstructionBook);
        bool downed = health != null && health.IsDowned;
        if (downed)
            ResetLocalContext();
        else
            ObserveAutomaticBucketCollection();
        if (book != null)
            book.CopyGuidanceSteps(LocalClientId, bookSteps);
        else
            bookSteps.Clear();
        candidates.Clear();
        int required = 0, complete = 0;
        string goal = "Build the bridge";
        foreach (BridgeComponent component in components)
        {
            if (component == null || component.ConstructionSite == null)
                continue;
            BridgeConstructionSite site = component.ConstructionSite;
            bool currentStage = manager.IsComponentInCurrentBridgeStage(component.ComponentID);
            bool done = BridgeGuidanceSteps.IsComplete(site);
            if (currentStage || manager.IsFullyAssembled)
            {
                required++;
                if (done)
                    complete++;
                if (currentStage && component.GetBridgeComponentSO() != null)
                    goal = "Complete " + component.GetBridgeComponentSO().componentName;
            }
            if (done)
                continue;
            BridgeGuidanceInstruction instruction = BridgeGuidanceSteps.Read(site);
            int order = GetBookOrder(component.GetBridgeComponentSO());
            candidates.Add(new BridgeGuidanceCandidate(ApplyAuthoredText(site, instruction), order, 0,
                currentStage, false, BridgeGuidanceSteps.IsActionable(site), false, Distance(site)));
            if (!currentStage || !HasIncompleteAssignment(site, instruction.StepKey))
                continue;
            BridgeConstructionSite actionable = ResolvePrerequisite(site, 0);
            if (actionable == null)
                continue;
            BridgeGuidanceInstruction prerequisiteInstruction = BridgeGuidanceSteps.Read(actionable);
            candidates.Add(new BridgeGuidanceCandidate(ApplyAuthoredText(actionable, prerequisiteInstruction),
                GetBookOrder(actionable.BridgeComponent.GetBridgeComponentSO()), 0, true, true,
                BridgeGuidanceSteps.IsActionable(actionable), false, Distance(actionable)));
        }
        if (manager.IsFullyAssembled)
            return new BridgeGuidanceSnapshot(true, true, manager.CurrentBridgeStageIndex, complete, required, "Bridge complete", default);

        if (downed)
            return new BridgeGuidanceSnapshot(true, false, manager.CurrentBridgeStageIndex, complete, required,
                goal, Device(null, "player.recover", "Recover to continue bridge work", "Wait for help or use the available respawn interaction.", "Interact"));

        BridgeConstructionSite activeSite = FindActiveSite();
        BridgeGuidanceInstruction device = ReadActiveDevice(activeSite);
        BridgeConstructionSite failureSite = activeSite != null ? activeSite : rememberedSite != null && RememberedDistance <= 6f ? rememberedSite : null;
        BridgeGuidanceInstruction failure = ReadLocalFailure(failureSite);
        BridgeGuidanceInstruction selected = BridgeGuidanceSelection.Select(candidates,
            Id(activeSite), Id(rememberedSite), failure, device);
        if (usedNail != null && activeSite != null && selected.ComponentId == Id(activeSite) && usedNail.RequiredHitCount > 0)
            selected = new BridgeGuidanceInstruction(selected.ComponentId, selected.FocusName, selected.StepKey,
                selected.Progress, selected.ShortText,
                selected.DetailedHelp + $" This nail: {usedNail.HitCount}/{usedNail.RequiredHitCount} strikes.",
                selected.Tool, selected.Action, selected.CompletedWork, selected.RequiredWork,
                selected.RemainingSeconds, selected.Actions, usedNail.HitCount, usedNail.RequiredHitCount);
        return new BridgeGuidanceSnapshot(true, false, manager.CurrentBridgeStageIndex, complete, required, goal, selected);
    }

    private void ObserveAutomaticBucketCollection()
    {
        GameObject held = interaction.GetPickedUpGameObject();
        PortableSubstanceContainer bucket = held != null ? held.GetComponent<PortableSubstanceContainer>() : null;
        if (bucket != null && bucket == observedBucket && bucket.CurrentUnits > observedBucketUnits &&
            bucket.CurrentSubstance != null && bucket.CurrentSubstance.IsSoil &&
            FoundationLooseSoilField.TryFindCollectionField(interaction.transform.position, out FoundationLooseSoilField field) &&
            field.TryGetConstructionSite(out BridgeConstructionSite site) && ValidateSite(site) != null)
        {
            rememberedSite = site;
            rememberedInteractionPoint = interaction.transform.position;
            usedMixer = null;
            usedWheelbarrow = null;
        }
        observedBucket = bucket;
        observedBucketUnits = bucket != null ? bucket.CurrentUnits : 0f;
    }

    private ulong LocalClientId => playerNetworkObject != null && playerNetworkObject.IsSpawned ? playerNetworkObject.OwnerClientId : 0;
    private float RememberedDistance => Vector3.Distance(interaction.transform.position, rememberedInteractionPoint);
    private float Distance(BridgeConstructionSite site) => site == null ? float.PositiveInfinity : site == rememberedSite
        ? RememberedDistance : Vector3.Distance(interaction.transform.position, site.GetClosestInteractionPoint(interaction.transform.position));
    private static int Id(BridgeConstructionSite site) => site != null && site.BridgeComponent != null ? site.BridgeComponent.ComponentID : -1;

    private int GetBookOrder(BridgeComponentSO type)
    {
        foreach (BridgeGuidanceBookStep step in bookSteps)
            if (step.Component == type)
                return step.SpreadIndex;
        return int.MaxValue;
    }

    private bool HasIncompleteAssignment(BridgeConstructionSite site, string currentKey)
    {
        BridgeGuidanceSteps.CopyAuthoringKeys(site, keys);
        foreach (BridgeGuidanceBookStep step in bookSteps)
        {
            if (step.Component != site.BridgeComponent.GetBridgeComponentSO() || !step.IsAssigned || string.IsNullOrEmpty(step.Key))
                continue;
            if (BridgeGuidanceSteps.IsAssignedStepIncomplete(keys, currentKey, step.Key, BridgeGuidanceSteps.IsWaiting(site)))
                return true;
        }
        return false;
    }

    private BridgeGuidanceInstruction ApplyAuthoredText(BridgeConstructionSite site, BridgeGuidanceInstruction instruction)
    {
        string text = instruction.ShortText;
        string help = instruction.DetailedHelp;
        foreach (BridgeGuidanceBookStep step in bookSteps)
            if (step.Component == site.BridgeComponent.GetBridgeComponentSO() && step.Key == instruction.StepKey)
            {
                bool liveShortText = instruction.StepKey == "foundation.dry" ||
                    site is BridgeGirderConstructionSite girder && girder.IsFastenerPairWindowActive && instruction.StepKey == "girder.fasten";
                if (!liveShortText && !string.IsNullOrWhiteSpace(step.ShortText))
                    text = step.ShortText;
                if (!string.IsNullOrWhiteSpace(step.DetailedHelp))
                {
                    help = step.DetailedHelp;
                    if (instruction.StepKey == "foundation.dry")
                        help += $" Remaining: {instruction.RemainingSeconds:0} s; drying duration: {site.ConcreteDryingDuration:0.#} s.";
                    if (site is BridgeGirderConstructionSite pairedGirder && instruction.StepKey == "girder.fasten")
                    {
                        help += $" Pair window: {pairedGirder.FastenerPairWindowDuration:0.#} s.";
                        if (pairedGirder.IsFastenerPairWindowActive)
                            help += $" Strike the indicated diagonal partner before the remaining {instruction.RemainingSeconds:0} s expire.";
                    }
                }
                break;
            }
        return new BridgeGuidanceInstruction(instruction.ComponentId, FocusName(site), instruction.StepKey,
            instruction.Progress, text, help, instruction.Tool, instruction.Action, instruction.CompletedWork,
            instruction.RequiredWork, instruction.RemainingSeconds, instruction.Actions, instruction.NailHits, instruction.RequiredNailHits);
    }

    private string FocusName(BridgeConstructionSite site)
    {
        if (site == null || site.BridgeComponent == null)
            return "Bridge equipment";
        BridgeComponentSO type = site.BridgeComponent.GetBridgeComponentSO();
        int ordinal = 1;
        foreach (BridgeComponent component in components)
            if (component != null && component.GetBridgeComponentSO() == type && component.ComponentID < Id(site))
                ordinal++;
        return (type != null ? type.componentName : "Bridge part") + " " + ordinal;
    }

    private BridgeConstructionSite ResolvePrerequisite(BridgeConstructionSite site, int depth)
    {
        if (depth > components.Count || site == null || BridgeGuidanceSteps.IsComplete(site))
            return null;
        if (!BridgeGuidanceSteps.IsWaiting(site))
            return site;
        site.CopyGuidancePrerequisites(prerequisites);
        // Copy before recursion: each site can own multiple supports and the scratch list is reused.
        BridgeComponent[] dependencies = prerequisites.ToArray();
        BridgeConstructionSite best = null;
        foreach (BridgeComponent dependency in dependencies)
        {
            if (dependency == null || !components.Contains(dependency))
                continue;
            BridgeConstructionSite result = ResolvePrerequisite(dependency.ConstructionSite, depth + 1);
            if (result == null)
                continue;
            if (best == null || GetBookOrder(result.BridgeComponent.GetBridgeComponentSO()) < GetBookOrder(best.BridgeComponent.GetBridgeComponentSO()) ||
                GetBookOrder(result.BridgeComponent.GetBridgeComponentSO()) == GetBookOrder(best.BridgeComponent.GetBridgeComponentSO()) && Id(result) < Id(best))
                best = result;
        }
        return best;
    }

    private BridgeConstructionSite FindActiveSite()
    {
        if (interaction.ActiveFlexibleSapling != null)
        {
            BridgeConstructionSite clearingSite = ResolveSite(interaction.ActiveFlexibleSapling);
            if (clearingSite != null)
                return clearingSite;
        }
        if (spiritLevel != null && spiritLevel.IsMeasuring)
            foreach (BridgeComponent component in components)
                if (component != null && component.ComponentID == spiritLevel.MeasuredComponentId)
                    return component.ConstructionSite;
        if (interaction.IsBucketActionHoldActive)
        {
            BridgeConstructionSite bucketSite = ResolveSite(interaction.BucketActionTarget);
            if (bucketSite != null)
                return bucketSite;
        }
        if (actions != null && actions.IsActionInProgress && actionSite != null && Distance(actionSite) <= 6f)
            return actionSite;
        WheelbarrowController wheelbarrow = wheelbarrowControls != null ? wheelbarrowControls.CurrentWheelbarrow : null;
        if (wheelbarrow != null && wheelbarrow.CurrentDock != null && wheelbarrow.CurrentDock.FoundationSite != null)
            return ValidateSite(wheelbarrow.CurrentDock.FoundationSite);
        GameObject held = interaction.GetPickedUpGameObject();
        if (held == null)
            return null;
        BridgeConstructionSite physicalPanelSite = ResolveSite(held.transform);
        if (physicalPanelSite != null)
            return physicalPanelSite;
        if (!held.TryGetComponent(out MountableBridgeComponent part))
            return null;
        MountableBridgeComponentSO partDefinition = part.GetMountableBridgeComponentSO();
        BridgeConstructionSite best = null;
        foreach (BridgeComponent component in components)
        {
            if (component == null || component.ConstructionSite == null || partDefinition == null ||
                component.GetBridgeComponentSO() != partDefinition.bridgeComponentSO || BridgeGuidanceSteps.IsComplete(component.ConstructionSite))
                continue;
            if (best == null || IsBetterHeldTarget(component.ConstructionSite, best, part))
                best = component.ConstructionSite;
        }
        return best;
    }

    private bool IsBetterHeldTarget(BridgeConstructionSite site, BridgeConstructionSite other, MountableBridgeComponent part)
    {
        bool actionable = !BridgeGuidanceSteps.IsWaiting(site);
        bool otherActionable = !BridgeGuidanceSteps.IsWaiting(other);
        if (actionable != otherActionable)
            return actionable;
        BridgeMountSocket socket = site.GetComponent<BridgeMountSocket>();
        BridgeMountSocket otherSocket = other.GetComponent<BridgeMountSocket>();
        NetworkObject partNetworkObject = part.GetComponent<NetworkObject>();
        bool candidate = socket != null && partNetworkObject != null && partNetworkObject.IsSpawned && socket.CurrentCandidateNetworkObjectId == partNetworkObject.NetworkObjectId;
        bool otherCandidate = otherSocket != null && partNetworkObject != null && partNetworkObject.IsSpawned && otherSocket.CurrentCandidateNetworkObjectId == partNetworkObject.NetworkObjectId;
        if (candidate != otherCandidate)
            return candidate;
        float distance = Vector3.SqrMagnitude((socket != null ? socket.TargetPose.position : site.transform.position) - part.transform.position);
        float otherDistance = Vector3.SqrMagnitude((otherSocket != null ? otherSocket.TargetPose.position : other.transform.position) - part.transform.position);
        return distance < otherDistance || Mathf.Approximately(distance, otherDistance) && Id(site) < Id(other);
    }

    private BridgeGuidanceInstruction ReadActiveDevice(BridgeConstructionSite activeSite)
    {
        WheelbarrowController wheelbarrow = wheelbarrowControls != null ? wheelbarrowControls.CurrentWheelbarrow : null;
        if (wheelbarrow != null)
            usedWheelbarrow = wheelbarrow;
        else if (usedWheelbarrow != null && usedWheelbarrow.HasConcrete && usedWheelbarrow.IsDocked &&
            usedWheelbarrow.CurrentDock != null && usedWheelbarrow.CurrentDock.FoundationSite != null &&
            Vector3.Distance(interaction.transform.position, usedWheelbarrow.transform.position) <= 6f &&
            (activeSite == null || activeSite == usedWheelbarrow.CurrentDock.FoundationSite))
        {
            wheelbarrow = usedWheelbarrow;
            activeSite = ValidateSite(wheelbarrow.CurrentDock.FoundationSite);
        }
        WheelbarrowOccupantRole role = wheelbarrowControls != null ? wheelbarrowControls.CurrentRole : WheelbarrowOccupantRole.None;
        bool concreteContext = wheelbarrow != null && !wheelbarrow.HasResourceCargo &&
            role != WheelbarrowOccupantRole.Passenger &&
            (wheelbarrow.HasConcrete || activeSite != null && activeSite.CurrentStage == BridgeConstructionStage.ConcretePouring ||
                rememberedSite != null && rememberedSite.CurrentStage == BridgeConstructionStage.ConcretePouring ||
                wheelbarrow.CurrentDock != null && wheelbarrow.CurrentDock.DockType == WheelbarrowDockType.MixerLoading && usedMixer != null);
        if (concreteContext)
        {
            if (wheelbarrow.CurrentDock != null && ValidateSite(wheelbarrow.CurrentDock.FoundationSite) != null)
            {
                activeSite = wheelbarrow.CurrentDock.FoundationSite;
                rememberedSite = activeSite;
                rememberedInteractionPoint = wheelbarrow.CurrentDock.TargetPose != null
                    ? wheelbarrow.CurrentDock.TargetPose.position : wheelbarrow.CurrentDock.transform.position;
            }
            if (activeSite != null && (activeSite.CurrentStage == BridgeConstructionStage.ConcreteDrying ||
                activeSite.CurrentStage == BridgeConstructionStage.ReadyForMount || activeSite.HasActiveConcreteFailure))
                return default;
            if (role is WheelbarrowOccupantRole.PourLeft or WheelbarrowOccupantRole.PourRight)
            {
                WheelbarrowPouringMinigame pour = wheelbarrow.CurrentDock != null ? wheelbarrow.CurrentDock.GetComponent<WheelbarrowPouringMinigame>() : null;
                bool waiting = pour == null || pour.State == WheelbarrowPouringState.WaitingForPlayers;
                return Device(activeSite, waiting ? "concrete.wait-partner" : "concrete.pour",
                    waiting ? "Wait for a partner at the other pour slot" : "Pour together and keep control",
                    "Use the two foundation pour slots. Wait until both players join, then coordinate your controls to keep both sides in the safe zone.", "Pour");
            }
            if (wheelbarrow.IsDocked && wheelbarrow.CurrentDock != null && wheelbarrow.CurrentDock.FoundationSite != null)
                return Device(activeSite, "concrete.join", "Take a pour slot with your partner",
                    "The wheelbarrow is docked at this foundation. Each player takes one pour slot; wait until both players join before pouring.", "Interact");
            if (wheelbarrow.HasConcrete)
                return Device(activeSite, "concrete.transport", "Transport concrete to the foundation",
                "Drive the loaded wheelbarrow into the foundation docking area, align it and release the handles to dock. Join a pour slot with your partner.", "Interact");
            return Device(activeSite, "concrete.load", "Dock and load the ready concrete batch",
                "Dock this empty wheelbarrow at the mixer and load the provided ready concrete batch, then transport it to the foundation.", "Interact");
        }
        if (interaction.IsBucketActionHoldActive &&
            (activeSite != null && activeSite.CurrentStage == BridgeConstructionStage.Digging ||
                rememberedSite != null && rememberedSite.CurrentStage == BridgeConstructionStage.Digging &&
                interaction.ActiveBucket != null && interaction.ActiveBucket.CurrentSubstance != null && interaction.ActiveBucket.CurrentSubstance.IsSoil))
            return Device(activeSite, "bucket.hold", "Keep holding the bucket action",
                "Maintain the bucket action until it completes. Collect loosened soil while walking and empty a full bucket outside the excavation.", "ActionAlt", interaction.BucketActionHoldProgress);
        if (activeSite == null && rememberedSite == null && usedMixer != null && Vector3.Distance(interaction.transform.position, usedMixer.transform.position) <= 6f &&
            usedMixer.BatchState == ConcreteMixerBatchState.ConcreteReady)
            return Device(activeSite, "concrete.load", "Load the ready concrete into a docked wheelbarrow",
                "Use the ready batch already provided. Bring an empty wheelbarrow to the mixer dock and transfer the concrete; no recipe or gathering is needed.", "Interact");
        GameObject held = interaction.GetPickedUpGameObject();
        if (held != null && held.TryGetComponent(out PortableSubstanceContainer bucket) && bucket.CurrentUnits >= bucket.Capacity &&
            rememberedSite != null && rememberedSite.CurrentStage == BridgeConstructionStage.Digging)
            return Device(rememberedSite, "bucket.empty", "Empty the full bucket outside the excavation",
                "Leave the foundation excavation with the full bucket. Hold the alternate action to empty it outside, then return to collect more soil.", "ActionAlt");
        return default;
    }

    private BridgeGuidanceInstruction ReadLocalFailure(BridgeConstructionSite site)
    {
        if (site == null && usedWheelbarrow != null && usedWheelbarrow.CurrentDock != null &&
            Vector3.Distance(interaction.transform.position, usedWheelbarrow.transform.position) <= 6f)
            site = ValidateSite(usedWheelbarrow.CurrentDock.FoundationSite);
        if (site != null && site.HasActiveConcreteFailure)
        {
            string key = site.ConcreteFailureState == FoundationConcreteFailureState.AwaitingWheelbarrowExit ? "foundation.exit"
                : site.ConcreteFailureState == FoundationConcreteFailureState.HardenedFailure ? "foundation.recover" : "foundation.failure-wait";
            return ApplyAuthoredText(site, BridgeGuidanceSteps.Describe(site, key,
                site.FailedConcreteWorkRequired > 0 ? site.FailedConcreteBreakProgress / site.FailedConcreteWorkRequired : 0));
        }
        if (usedWheelbarrow != null && (site == null || site == rememberedSite) && Vector3.Distance(interaction.transform.position, usedWheelbarrow.transform.position) <= 6f &&
            usedWheelbarrow.State is WheelbarrowState.Tipped or WheelbarrowState.Righting)
            return Device(site, "concrete.right", "Right the wheelbarrow",
                "Use the wheelbarrow's righting interaction, then resume transporting the concrete.", "Interact", usedWheelbarrow.NormalizedRightingProgress);
        if (site != null && usedPanel != null && Distance(site) <= 6f &&
            BridgeDeckPanelSectionNetworkState.TryGetAssessmentFor(usedPanel, out BridgeDeckPanelReplicatedState assessment, out BridgeDeckPanelConstructionSite panelSite) &&
            panelSite == site && !assessment.Complete && assessment.Failure != (byte)BridgeDeckPanelPlacementFailure.None &&
            assessment.Failure != (byte)BridgeDeckPanelPlacementFailure.MissingQuarters)
            return Device(site, "panel.correct", "Correct the panel pose before nailing",
                "The panel assessment rejects its pose. Remove incorrect nails with the Crowbar, reset it and use hand placement to correct position, height and orientation before adding nails.", "Interact");
        if (site != null && placement != null && placement.IsActive && !placement.LastValidation.IsValid &&
            interaction.GetPickedUpGameObject() != null && interaction.GetPickedUpGameObject().GetComponent<BridgeDeckPanelMounting>() != null)
            return Device(site, "panel.obstructed", "Clear the panel placement obstruction",
                "Move or rotate the held panel away from the blocking geometry before confirming placement.", "Placement");
        return default;
    }

    private BridgeGuidanceInstruction Device(BridgeConstructionSite site, string key, string text, string help,
        string action, float progress = 0f) => new BridgeGuidanceInstruction(Id(site), site != null
            ? FocusName(site) : "Bridge equipment", key, progress, text, help, "", action, actions:
                action == "Interact" ? BridgeGuidanceActions.Interact : action == "ActionAlt" ? BridgeGuidanceActions.Secondary :
                action == "Pour" ? BridgeGuidanceActions.PourControl : action == "Placement" ? BridgeGuidanceActions.PlacementToggle |
                    BridgeGuidanceActions.PlacementConfirm | BridgeGuidanceActions.PlacementRotation : BridgeGuidanceActions.None);

    private BridgeConstructionSite ValidateSite(BridgeConstructionSite site) => site != null && site.BridgeComponent != null &&
        components.Contains(site.BridgeComponent) ? site : null;

    private BridgeConstructionSite ResolveSite(Component target)
    {
        if (target == null)
            return null;
        BridgeConstructionSite site = target.GetComponentInParent<BridgeConstructionSite>();
        if (site != null)
            return ValidateSite(site);
        FoundationLooseSoilField field = target.GetComponentInParent<FoundationLooseSoilField>();
        if (field != null && field.TryGetConstructionSite(out site))
            return ValidateSite(site);
        FoundationFailedConcreteTarget failed = target.GetComponentInParent<FoundationFailedConcreteTarget>();
        if (failed != null)
            return ValidateSite(failed.ConstructionSite);
        BridgeDeckPanelMounting panel = target.GetComponentInParent<BridgeDeckPanelMounting>();
        if (panel != null && panel.IsPhysicalPanel && BridgeDeckPanelSectionNetworkState.TryGetAssessmentFor(panel, out _, out BridgeDeckPanelConstructionSite panelSite))
            return ValidateSite(panelSite);
        MountingNail nail = target.GetComponentInParent<MountingNail>();
        if (nail != null && nail.TryGetAttachmentInfo(out MountingNailAttachmentInfo info))
        {
            // Follow the physical panel rather than a nearby aimed ghost.
            Component attachedPanel = info.FirstTarget != null && info.FirstTarget.GetComponent<BridgeDeckPanelMounting>() != null
                ? info.FirstTarget : info.SecondTarget;
            if (attachedPanel != null && attachedPanel != target)
                return ResolveSite(attachedPanel);
        }
        foreach (BridgeComponent component in components)
            if (component != null && component.ConstructionSite != null && component.ConstructionSite.IsGuidanceClearingTarget(target))
                return component.ConstructionSite;
        return null;
    }

    private void ObserveActionTarget(Component target)
    {
        if (!IsLocal || disposed)
            return;
        ObserveLocalTarget(target);
        actionSite = ResolveSite(target);
    }
    private void ObserveLocalTarget(Component target)
    {
        if (disposed || !IsLocal || manager == null)
            return;
        manager.CopyGuidanceComponents(components);
        BridgeConstructionSite site = ResolveSite(target);
        if (site != null)
        {
            rememberedSite = site;
            Collider collider = target as Collider;
            if (collider == null)
                collider = target.GetComponent<Collider>();
            rememberedInteractionPoint = collider != null ? collider.ClosestPoint(interaction.transform.position) : target.transform.position;
            usedWheelbarrow = null;
            usedMixer = null;
        }
        usedPanel = target != null ? target.GetComponentInParent<BridgeDeckPanelMounting>() : null;
        usedNail = target != null ? target.GetComponentInParent<MountingNail>() : null;
        if (usedPanel == null && usedNail != null && usedNail.TryGetAttachmentInfo(out MountingNailAttachmentInfo attachment))
        {
            if (attachment.FirstTarget != null)
                usedPanel = attachment.FirstTarget.GetComponentInParent<BridgeDeckPanelMounting>();
            if (usedPanel == null && attachment.SecondTarget != null)
                usedPanel = attachment.SecondTarget.GetComponentInParent<BridgeDeckPanelMounting>();
        }
        WheelbarrowInteractionPoint point = target != null ? target.GetComponentInParent<WheelbarrowInteractionPoint>() : null;
        WheelbarrowController wheelbarrow = point != null ? point.Wheelbarrow : target != null ? target.GetComponentInParent<WheelbarrowController>() : null;
        WheelbarrowPourGripInteraction grip = target != null ? target.GetComponentInParent<WheelbarrowPourGripInteraction>() : null;
        if (grip != null && grip.Minigame != null)
            wheelbarrow = grip.Minigame.Wheelbarrow;
        if (wheelbarrow != null)
        {
            usedWheelbarrow = wheelbarrow;
            if (wheelbarrow.CurrentDock != null && ValidateSite(wheelbarrow.CurrentDock.FoundationSite) != null)
            {
                rememberedSite = wheelbarrow.CurrentDock.FoundationSite;
                rememberedInteractionPoint = target.transform.position;
            }
        }
        ConcreteMixerController mixer = target != null ? target.GetComponentInParent<ConcreteMixerController>() : null;
        if (mixer != null)
        {
            usedMixer = mixer;
            rememberedSite = null;
            usedWheelbarrow = null;
        }
        dirty = true;
    }

    private void Invalidate(object sender, EventArgs args) => dirty = true;
    private void OnStageChanged(object sender, BridgeConstructionStageChangedEventArgs args) => dirty = true;
    private void OnActionStarted(object sender, PlayerActionController.ToolActionEventArgs args)
    {
        actionSite = null;
        dirty = true;
    }
    private void OnDownedChanged(object sender, EventArgs args) => ResetLocalContext();
    private void OnBookAvailable(ConstructionBookController available)
    {
        SetBook(available);
        dirty = true;
    }
    private void OnBookChanged() => dirty = true;
    private void OnBookDespawned()
    {
        SetBook(null);
        dirty = true;
    }
    private void SetBook(ConstructionBookController value)
    {
        if (book == value)
            return;
        if (book != null)
        {
            book.AssignmentsChanged -= OnBookChanged;
            book.Despawned -= OnBookDespawned;
        }
        book = value;
        if (book != null)
        {
            book.AssignmentsChanged += OnBookChanged;
            book.Despawned += OnBookDespawned;
        }
    }
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        SetBook(null);
        if (interaction != null)
        {
            interaction.LocalInteractionTargetObserved -= ObserveLocalTarget;
            interaction.OnHeldObjectChanged -= Invalidate;
        }
        if (actions != null)
        {
            actions.LocalActionTargetObserved -= ObserveActionTarget;
            actions.OnToolActionStarted -= OnActionStarted;
        }
        if (health != null)
            health.OnDownedStateChanged -= OnDownedChanged;
        if (manager != null)
        {
            manager.OnBridgeRequirementsChanged -= Invalidate;
            manager.OnConstructionStageChanged -= OnStageChanged;
            manager.ConstructionBookAvailabilityChanged -= OnBookAvailable;
        }
        ResetLocalContext();
        Changed = null;
    }
}
