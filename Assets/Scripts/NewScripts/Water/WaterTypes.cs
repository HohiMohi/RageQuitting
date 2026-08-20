public enum WaterExposureState
{
    None,
    Wading,
    Unsafe,
    Exhausted
}

public enum EnvironmentalRemovalReason
{
    Unknown,
    RiverBed
}

public enum NPCWaterTraversalMode
{
    None,
    SurfaceSwimmer,
    BottomWalker,
    VolumeSwimmer
}

public enum StaminaDrainSource
{
    Sprint,
    Carry,
    UnderstaffedSharedCarry,
    Water,
    RopeReeling,
    WheelbarrowDriving
}

public enum StaminaExhaustionReason
{
    None,
    SharedCarry,
    Water
}
