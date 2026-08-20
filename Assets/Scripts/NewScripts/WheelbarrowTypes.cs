public enum WheelbarrowState : byte
{
    Free,
    Driven,
    Docking,
    Docked,
    Pouring,
    Tipped,
    Righting
}

public enum WheelbarrowOccupantRole : byte
{
    None,
    Driver,
    Passenger,
    PourLeft,
    PourRight
}

public enum WheelbarrowDockType : byte
{
    MixerLoading,
    FoundationPouring
}

public enum WheelbarrowPouringState : byte
{
    Inactive,
    WaitingForPlayers,
    Active,
    Success,
    CriticalFailure
}
