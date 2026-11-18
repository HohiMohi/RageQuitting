using System;
using System.Collections.Generic;

[Flags]
public enum BridgeGuidanceActions
{
    None = 0,
    Interact = 1,
    Primary = 2,
    Secondary = 4,
    PlacementToggle = 8,
    PlacementConfirm = 16,
    PlacementRotation = 32,
    PourControl = 64
}

/// <summary>Presentation-only book data. The wire still identifies assignments by spread/step indices.</summary>
public readonly struct BridgeGuidanceBookStep
{
    public readonly BridgeComponentSO Component;
    public readonly int SpreadIndex, StepIndex;
    public readonly string Key, ShortText, DetailedHelp;
    public readonly bool IsAssigned;
    public BridgeGuidanceBookStep(BridgeComponentSO component, int spread, int step, string key,
        string shortText, string help, bool assigned)
    {
        Component = component;
        SpreadIndex = spread;
        StepIndex = step;
        Key = key;
        ShortText = shortText;
        DetailedHelp = help;
        IsAssigned = assigned;
    }
}

public readonly struct BridgeGuidanceInstruction : IEquatable<BridgeGuidanceInstruction>
{
    public readonly int ComponentId;
    public readonly string FocusName, StepKey, ShortText, DetailedHelp, Tool, Action;
    public readonly float Progress;
    public readonly float CompletedWork, RequiredWork, RemainingSeconds;
    public readonly BridgeGuidanceActions Actions;
    public readonly int NailHits, RequiredNailHits;
    public bool HasValue => !string.IsNullOrEmpty(StepKey);
    public BridgeGuidanceInstruction(int id, string focus, string key, float progress, string text,
        string help, string tool = "", string action = "", float completedWork = 0f,
        float requiredWork = 0f, float remainingSeconds = 0f, BridgeGuidanceActions actions = BridgeGuidanceActions.None,
        int nailHits = 0, int requiredNailHits = 0)
    {
        ComponentId = id;
        FocusName = focus ?? "";
        StepKey = key ?? "";
        Progress = UnityEngine.Mathf.Clamp01(progress);
        ShortText = text ?? "";
        DetailedHelp = help ?? "";
        Tool = tool ?? "";
        Action = action ?? "";
        CompletedWork = completedWork;
        RequiredWork = requiredWork;
        RemainingSeconds = remainingSeconds;
        Actions = actions;
        NailHits = nailHits;
        RequiredNailHits = requiredNailHits;
    }
    public bool Equals(BridgeGuidanceInstruction other) => ComponentId == other.ComponentId &&
        FocusName == other.FocusName && StepKey == other.StepKey && Progress == other.Progress &&
        ShortText == other.ShortText && DetailedHelp == other.DetailedHelp && Tool == other.Tool && Action == other.Action &&
        CompletedWork == other.CompletedWork && RequiredWork == other.RequiredWork && RemainingSeconds == other.RemainingSeconds && Actions == other.Actions &&
        NailHits == other.NailHits && RequiredNailHits == other.RequiredNailHits;
    public override bool Equals(object obj) => obj is BridgeGuidanceInstruction other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ComponentId, StepKey, Progress, ShortText);
}

public readonly struct BridgeGuidanceSnapshot : IEquatable<BridgeGuidanceSnapshot>
{
    public readonly bool IsReady, IsComplete;
    public readonly int StageIndex, CompletedCount, RequiredCount;
    public readonly string Goal;
    public readonly BridgeGuidanceInstruction Instruction;
    public BridgeGuidanceSnapshot(bool ready, bool complete, int stage, int completed, int required,
        string goal, BridgeGuidanceInstruction instruction)
    {
        IsReady = ready;
        IsComplete = complete;
        StageIndex = stage;
        CompletedCount = completed;
        RequiredCount = required;
        Goal = goal ?? "";
        Instruction = instruction;
    }
    public bool Equals(BridgeGuidanceSnapshot other) => IsReady == other.IsReady && IsComplete == other.IsComplete &&
        StageIndex == other.StageIndex && CompletedCount == other.CompletedCount && RequiredCount == other.RequiredCount &&
        Goal == other.Goal && Instruction.Equals(other.Instruction);
    public override bool Equals(object obj) => obj is BridgeGuidanceSnapshot other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(IsReady, IsComplete, StageIndex, CompletedCount, RequiredCount, Goal, Instruction);
}

/// <summary>Pure selection input, derived afresh from live gameplay; it owns no bridge progress.</summary>
public readonly struct BridgeGuidanceCandidate
{
    public readonly BridgeGuidanceInstruction Instruction;
    public readonly int BookOrder, StepOrder;
    public readonly bool IsCurrentStage, IsAssigned, IsActionable, IsComplete;
    public readonly float InteractionDistance;
    public int ComponentId => Instruction.ComponentId;
    public BridgeGuidanceCandidate(BridgeGuidanceInstruction instruction, int bookOrder, int stepOrder,
        bool currentStage, bool assigned, bool actionable, bool complete, float distance)
    {
        Instruction = instruction;
        BookOrder = bookOrder;
        StepOrder = stepOrder;
        IsCurrentStage = currentStage;
        IsAssigned = assigned;
        IsActionable = actionable;
        IsComplete = complete;
        InteractionDistance = distance;
    }
}

public static class BridgeGuidanceSelection
{
    /// <summary>Failure must already be local and relevant. Aim is deliberately absent from this API.</summary>
    public static BridgeGuidanceInstruction Select(IReadOnlyList<BridgeGuidanceCandidate> candidates,
        int activeComponentId, int rememberedComponentId, BridgeGuidanceInstruction localFailure = default,
        BridgeGuidanceInstruction activeInstruction = default)
    {
        if (localFailure.HasValue)
            return localFailure;
        if (activeInstruction.HasValue)
            return activeInstruction;
        int best = -1, bestPriority = int.MaxValue;
        for (int i = 0; i < candidates.Count; i++)
        {
            BridgeGuidanceCandidate candidate = candidates[i];
            if (candidate.IsComplete || !candidate.Instruction.HasValue)
                continue;
            int priority = candidate.ComponentId == activeComponentId ? 0
                : candidate.ComponentId == rememberedComponentId && candidate.InteractionDistance <= 6f ? 1
                : candidate.IsCurrentStage && candidate.IsAssigned && candidate.IsActionable ? 2
                : candidate.IsCurrentStage ? 3 : int.MaxValue;
            if (priority == int.MaxValue)
                continue;
            if (best < 0 || priority < bestPriority || priority == bestPriority && ComesBefore(candidate, candidates[best]))
            {
                best = i;
                bestPriority = priority;
            }
        }
        return best >= 0 ? candidates[best].Instruction : default;
    }
    private static bool ComesBefore(BridgeGuidanceCandidate first, BridgeGuidanceCandidate second)
    {
        if (first.BookOrder != second.BookOrder)
            return first.BookOrder < second.BookOrder;
        if (first.ComponentId != second.ComponentId)
            return first.ComponentId < second.ComponentId;
        return first.StepOrder < second.StepOrder;
    }
}
