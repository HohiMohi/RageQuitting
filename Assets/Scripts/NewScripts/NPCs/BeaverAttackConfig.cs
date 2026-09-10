using System;
using UnityEngine;

public static class NPCActionTiming
{
    public const float MinimumPlaybackSpeed = 0.1f;
    public const float MaximumPlaybackSpeed = 4f;

    public static float ClampPlaybackSpeed(float playbackSpeed)
    {
        return Mathf.Clamp(playbackSpeed, MinimumPlaybackSpeed, MaximumPlaybackSpeed);
    }

    public static float CalculateEffectiveImpactDelay(float canonicalDelay, float playbackSpeed)
    {
        return Mathf.Max(0f, canonicalDelay) / ClampPlaybackSpeed(playbackSpeed);
    }
}

[Serializable]
public struct BeaverStrikeConfig
{
    [Min(0f)] public float damageMultiplier;
    [Range(NPCActionTiming.MinimumPlaybackSpeed, NPCActionTiming.MaximumPlaybackSpeed)] public float animationSpeed;
    [Min(0f)] public float gapAfterImpact;

    public BeaverStrikeConfig(float damageMultiplier, float animationSpeed, float gapAfterImpact)
    {
        this.damageMultiplier = damageMultiplier;
        this.animationSpeed = animationSpeed;
        this.gapAfterImpact = gapAfterImpact;
    }

    public float DamageMultiplier => Mathf.Max(0f, damageMultiplier);
    public float AnimationSpeed => NPCActionTiming.ClampPlaybackSpeed(animationSpeed);
    public float GapAfterImpact => Mathf.Max(0f, gapAfterImpact);
}

[Serializable]
public struct BeaverAttackPatternConfig
{
    public string name;
    [Min(0f)] public float weight;
    [Min(0f)] public float prepareDuration;
    public BeaverStrikeConfig[] strikes;
    [Min(0f)] public float recoveryDuration;

    public BeaverAttackPatternConfig(
        string name,
        float weight,
        float prepareDuration,
        BeaverStrikeConfig[] strikes,
        float recoveryDuration)
    {
        this.name = name;
        this.weight = weight;
        this.prepareDuration = prepareDuration;
        this.strikes = strikes;
        this.recoveryDuration = recoveryDuration;
    }

    public float Weight => Mathf.Max(0f, weight);
    public float PrepareDuration => Mathf.Max(0f, prepareDuration);
    public float RecoveryDuration => Mathf.Max(0f, recoveryDuration);
    public int StrikeCount => strikes != null ? strikes.Length : 0;

    public BeaverStrikeConfig GetStrike(int index)
    {
        return strikes != null && index >= 0 && index < strikes.Length
            ? strikes[index]
            : new BeaverStrikeConfig(1f, 1f, 0f);
    }

    public float GetNominalDamage(float baseDamage)
    {
        float total = 0f;
        for (int i = 0; i < StrikeCount; i++)
        {
            total += Mathf.Max(0f, baseDamage) * GetStrike(i).DamageMultiplier;
        }

        return total;
    }
}

[Serializable]
public struct BeaverLungeConfig
{
    [Min(0f)] public float launchExtraRange;
    [Min(0f)] public float telegraphDuration;
    [Min(0f)] public float speed;
    [Min(0f)] public float maxTravelDistance;
    [Range(NPCActionTiming.MinimumPlaybackSpeed, NPCActionTiming.MaximumPlaybackSpeed)] public float animationSpeed;
    [Min(0f)] public float damageMultiplier;
    [Min(0f)] public float recoveryDuration;
    [Min(0.01f)] public float chaseRefreshInterval;
    [Min(0.1f)] public float unreachableTargetTimeout;
    [Min(0f)] public float collisionSkin;
    public LayerMask obstacleLayers;
    public ExternalImpulseProfileSO impulseProfile;

    public BeaverLungeConfig(
        float launchExtraRange,
        float telegraphDuration,
        float speed,
        float maxTravelDistance,
        float animationSpeed,
        float damageMultiplier,
        float recoveryDuration,
        ExternalImpulseProfileSO impulseProfile = null,
        float chaseRefreshInterval = 0.1f,
        float unreachableTargetTimeout = 5f,
        float collisionSkin = 0.02f,
        int obstacleLayers = ~0)
    {
        this.launchExtraRange = launchExtraRange;
        this.telegraphDuration = telegraphDuration;
        this.speed = speed;
        this.maxTravelDistance = maxTravelDistance;
        this.animationSpeed = animationSpeed;
        this.damageMultiplier = damageMultiplier;
        this.recoveryDuration = recoveryDuration;
        this.chaseRefreshInterval = chaseRefreshInterval;
        this.unreachableTargetTimeout = unreachableTargetTimeout;
        this.collisionSkin = collisionSkin;
        this.obstacleLayers = obstacleLayers;
        this.impulseProfile = impulseProfile;
    }

    public float LaunchExtraRange => Mathf.Max(0f, launchExtraRange);
    public float TelegraphDuration => Mathf.Max(0f, telegraphDuration);
    public float Speed => Mathf.Max(0f, speed);
    public float MaxTravelDistance => Mathf.Max(0f, maxTravelDistance);
    public float AnimationSpeed => NPCActionTiming.ClampPlaybackSpeed(animationSpeed);
    public float DamageMultiplier => Mathf.Max(0f, damageMultiplier);
    public float RecoveryDuration => Mathf.Max(0f, recoveryDuration);
    public float ChaseRefreshInterval => Mathf.Max(0.01f, chaseRefreshInterval);
    public float UnreachableTargetTimeout => Mathf.Max(0.1f, unreachableTargetTimeout);
    public float CollisionSkin => Mathf.Max(0f, collisionSkin);
    public int ObstacleLayers => obstacleLayers.value;
    public ExternalImpulseProfileSO ImpulseProfile => impulseProfile;
}
