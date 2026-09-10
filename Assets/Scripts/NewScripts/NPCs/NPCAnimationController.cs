using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.AI;
using UnityEngine.Playables;

public enum NPCAnimationState
{
    Idle,
    Walk
}

public enum NPCAnimationTrigger
{
    None,
    Notice,
    Action,
    HitReaction
}

public struct NPCAnimationTriggerEvent : INetworkSerializable, System.IEquatable<NPCAnimationTriggerEvent>
{
    public NPCAnimationTrigger Trigger;
    public int Sequence;
    public float PlaybackSpeed;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Trigger);
        serializer.SerializeValue(ref Sequence);
        serializer.SerializeValue(ref PlaybackSpeed);
    }

    public bool Equals(NPCAnimationTriggerEvent other)
    {
        return Trigger == other.Trigger && Sequence == other.Sequence && PlaybackSpeed.Equals(other.PlaybackSpeed);
    }
}

public class NPCAnimationController : NetworkBehaviour
{
    public const float MinimumOneShotBlendDuration = 0.01f;
    public const float MaximumOneShotBlendDuration = 1f;

    [SerializeField] private Animator animator;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private NPCVisualController visualController;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private NPCCarrier carrier;
    [SerializeField] private NPCHealth health;
    [SerializeField] private float walkSpeedReference = 3.5f;
    [SerializeField] private float idleSpeedThreshold = 0.05f;
    [SerializeField] private float speedDampTime = 0.12f;
    [SerializeField, Range(MinimumOneShotBlendDuration, MaximumOneShotBlendDuration)]
    private float oneShotBlendInDuration = 0.08f;
    [SerializeField, Range(MinimumOneShotBlendDuration, MaximumOneShotBlendDuration)]
    private float oneShotBlendOutDuration = 0.1f;

    private readonly NetworkVariable<float> speedNormalizedNetwork = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<NPCAnimationState> stateNetwork = new NetworkVariable<NPCAnimationState>(
        NPCAnimationState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<NPCAnimationTriggerEvent> triggerEventNetwork = new NetworkVariable<NPCAnimationTriggerEvent>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private float localSpeedNormalized;
    private float speedVelocity;
    private bool hasExternalMovementSpeedOverride;
    private float externalMovementSpeedNormalized;
    private int lastHandledTriggerSequence;
    private PlayableGraph playableGraph;
    private AnimationMixerPlayable rootMixer;
    private AnimationMixerPlayable locomotionMixer;
    private AnimationClipPlayable idlePlayable;
    private AnimationClipPlayable walkPlayable;
    private AnimationClipPlayable oneShotPlayable;
    private AnimationClip idleClip;
    private AnimationClip walkClip;
    private float oneShotTimer;
    private float activeOneShotDuration;
    private float oneShotElapsedTime;
    private float oneShotStartWeight;
    private bool hasPlayableGraph;

    private bool IsNetworkSessionActive => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned;

    public void SetAnimator(Animator targetAnimator)
    {
        animator = targetAnimator;
    }

    public void PlayNotice()
    {
        PlayTrigger(NPCAnimationTrigger.Notice);
    }

    public void PlayAction(float playbackSpeed = 1f)
    {
        PlayTrigger(NPCAnimationTrigger.Action, playbackSpeed);
    }

    public void PlayHitReaction()
    {
        PlayTrigger(NPCAnimationTrigger.HitReaction);
    }

    public void SetExternalMovementSpeedNormalized(float normalizedSpeed)
    {
        hasExternalMovementSpeedOverride = true;
        externalMovementSpeedNormalized = Mathf.Clamp01(normalizedSpeed);
    }

    public void ClearExternalMovementSpeedOverride()
    {
        hasExternalMovementSpeedOverride = false;
        externalMovementSpeedNormalized = 0f;
    }

    private void Awake()
    {
        CacheReferences();
    }

    public override void OnNetworkSpawn()
    {
        triggerEventNetwork.OnValueChanged += TriggerEventNetwork_OnValueChanged;

        if (IsServer)
        {
            UpdateNetworkAnimationState();
        }

        ApplyCurrentNetworkState();
    }

    public override void OnNetworkDespawn()
    {
        triggerEventNetwork.OnValueChanged -= TriggerEventNetwork_OnValueChanged;
    }

    private void Update()
    {
        if (!EnsureAnimatorReady())
        {
            return;
        }

        EnsurePlayableGraph();

        if (IsNetworkSessionActive)
        {
            if (IsServer)
            {
                UpdateNetworkAnimationState();
            }

            ApplyCurrentNetworkState();
            return;
        }

        ApplyLocalAnimationState();
    }

    private void CacheReferences()
    {
        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        if (carrier == null)
        {
            carrier = GetComponent<NPCCarrier>();
        }

        if (health == null)
        {
            health = GetComponent<NPCHealth>();
        }

        if (visualController == null)
        {
            visualController = GetComponentInChildren<NPCVisualController>(true);
            if (visualController != null && visualRoot == null)
            {
                visualRoot = visualController.transform;
            }
        }
    }

    private bool EnsureAnimatorReady()
    {
        if (animator == null)
        {
            if (visualController == null)
            {
                visualController = GetComponentInChildren<NPCVisualController>(true);
            }

            if (visualController != null)
            {
                animator = visualController.Animator;
            }
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        return animator != null && animator.isActiveAndEnabled && animator.gameObject.activeInHierarchy;
    }

    private void UpdateNetworkAnimationState()
    {
        float speedNormalized = CalculateTargetSpeedNormalized();
        speedNormalizedNetwork.Value = Mathf.Clamp01(speedNormalized);
        stateNetwork.Value = speedNormalized > idleSpeedThreshold ? NPCAnimationState.Walk : NPCAnimationState.Idle;
    }

    private void ApplyCurrentNetworkState()
    {
        localSpeedNormalized = speedNormalizedNetwork.Value;
        ApplyPlayableWeights();
    }

    private void ApplyLocalAnimationState()
    {
        float targetSpeedNormalized = CalculateTargetSpeedNormalized();
        localSpeedNormalized = Mathf.SmoothDamp(localSpeedNormalized, targetSpeedNormalized, ref speedVelocity, speedDampTime);
        ApplyPlayableWeights();
    }

    private float CalculateTargetSpeedNormalized()
    {
        if (health != null && health.IsDead)
        {
            return 0f;
        }

        if (hasExternalMovementSpeedOverride)
        {
            return externalMovementSpeedNormalized;
        }

        Vector3 velocity = agent != null ? agent.velocity : Vector3.zero;
        velocity.y = 0f;
        float speed = velocity.magnitude;

        if (speed <= idleSpeedThreshold)
        {
            return 0f;
        }

        float speedReference = Mathf.Max(idleSpeedThreshold, walkSpeedReference);
        return Mathf.Clamp01(speed / speedReference);
    }

    private void PlayTrigger(NPCAnimationTrigger trigger, float playbackSpeed = 1f)
    {
        if (trigger == NPCAnimationTrigger.None)
        {
            return;
        }

        if (IsNetworkSessionActive)
        {
            if (IsServer)
            {
                SetNetworkTrigger(trigger, playbackSpeed);
            }
            else
            {
                RequestTriggerServerRpc(trigger, playbackSpeed);
            }

            return;
        }

        ApplyTrigger(trigger, playbackSpeed);
    }

    private void SetNetworkTrigger(NPCAnimationTrigger trigger, float playbackSpeed)
    {
        NPCAnimationTriggerEvent current = triggerEventNetwork.Value;
        triggerEventNetwork.Value = new NPCAnimationTriggerEvent
        {
            Trigger = trigger,
            Sequence = current.Sequence + 1,
            PlaybackSpeed = NPCActionTiming.ClampPlaybackSpeed(playbackSpeed)
        };
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestTriggerServerRpc(NPCAnimationTrigger trigger, float playbackSpeed)
    {
        if (trigger == NPCAnimationTrigger.None || !System.Enum.IsDefined(typeof(NPCAnimationTrigger), trigger))
        {
            return;
        }

        SetNetworkTrigger(trigger, NPCActionTiming.ClampPlaybackSpeed(playbackSpeed));
    }

    private void TriggerEventNetwork_OnValueChanged(NPCAnimationTriggerEvent previousValue, NPCAnimationTriggerEvent newValue)
    {
        if (newValue.Sequence == lastHandledTriggerSequence)
        {
            return;
        }

        lastHandledTriggerSequence = newValue.Sequence;
        ApplyTrigger(newValue.Trigger, newValue.PlaybackSpeed);
    }

    private void ApplyTrigger(NPCAnimationTrigger trigger, float playbackSpeed)
    {
        if (!EnsureAnimatorReady())
        {
            return;
        }

        AnimationClip clip = GetTriggerClip(trigger);
        if (clip != null)
        {
            PlayOneShot(clip, playbackSpeed);
            return;
        }
    }

    private AnimationClip GetTriggerClip(NPCAnimationTrigger trigger)
    {
        if (visualController == null)
        {
            visualController = GetComponentInChildren<NPCVisualController>(true);
        }

        if (visualController == null)
        {
            return null;
        }

        switch (trigger)
        {
            case NPCAnimationTrigger.Notice:
                return visualController.NoticeClip;
            case NPCAnimationTrigger.Action:
                return visualController.ActionClip;
            case NPCAnimationTrigger.HitReaction:
                return visualController.HitReactionClip;
            default:
                return null;
        }
    }

    private void EnsurePlayableGraph()
    {
        if (hasPlayableGraph || animator == null)
        {
            return;
        }

        if (visualController == null)
        {
            visualController = GetComponentInChildren<NPCVisualController>(true);
        }

        if (visualController == null || visualController.IdleClip == null)
        {
            return;
        }

        idleClip = visualController.IdleClip;
        walkClip = visualController.WalkClip;

        playableGraph = PlayableGraph.Create($"{name}_NPCAnimationGraph");
        playableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        rootMixer = AnimationMixerPlayable.Create(playableGraph, 2, true);
        locomotionMixer = AnimationMixerPlayable.Create(playableGraph, 2, true);
        idlePlayable = AnimationClipPlayable.Create(playableGraph, idleClip);
        idlePlayable.SetApplyFootIK(false);
        idlePlayable.SetDuration(double.PositiveInfinity);

        playableGraph.Connect(idlePlayable, 0, locomotionMixer, 0);
        locomotionMixer.SetInputWeight(0, 1f);

        if (walkClip != null)
        {
            walkPlayable = AnimationClipPlayable.Create(playableGraph, walkClip);
            walkPlayable.SetApplyFootIK(false);
            walkPlayable.SetDuration(double.PositiveInfinity);
            playableGraph.Connect(walkPlayable, 0, locomotionMixer, 1);
            locomotionMixer.SetInputWeight(1, 0f);
        }

        playableGraph.Connect(locomotionMixer, 0, rootMixer, 0);
        rootMixer.SetInputWeight(0, 1f);
        rootMixer.SetInputWeight(1, 0f);

        AnimationPlayableOutput output = AnimationPlayableOutput.Create(playableGraph, "NPCAnimation", animator);
        output.SetSourcePlayable(rootMixer);
        playableGraph.Play();
        hasPlayableGraph = true;
        ApplyPlayableWeights();
    }

    private void ApplyPlayableWeights()
    {
        if (!hasPlayableGraph)
        {
            return;
        }

        LoopPlayable(idlePlayable, idleClip);
        LoopPlayable(walkPlayable, walkClip);

        float walkWeight = Mathf.Clamp01(localSpeedNormalized);
        locomotionMixer.SetInputWeight(0, 1f - walkWeight);
        if (walkPlayable.IsValid())
        {
            locomotionMixer.SetInputWeight(1, walkWeight);
        }

        if (oneShotTimer > 0f)
        {
            oneShotElapsedTime = Mathf.Min(activeOneShotDuration, oneShotElapsedTime + Time.deltaTime);
            oneShotTimer = Mathf.Max(0f, activeOneShotDuration - oneShotElapsedTime);
            float oneShotWeight = CalculateOneShotBlendWeight(
                oneShotElapsedTime,
                activeOneShotDuration,
                oneShotBlendInDuration,
                oneShotBlendOutDuration,
                oneShotStartWeight);
            rootMixer.SetInputWeight(0, 1f - oneShotWeight);
            rootMixer.SetInputWeight(1, oneShotWeight);
            return;
        }

        rootMixer.SetInputWeight(0, 1f);
        rootMixer.SetInputWeight(1, 0f);
    }

    public static float CalculateOneShotBlendWeight(
        float elapsedTime,
        float duration,
        float blendInDuration,
        float blendOutDuration,
        float startWeight)
    {
        float safeDuration = Mathf.Max(0f, duration);
        if (safeDuration <= 0f)
        {
            return 0f;
        }

        float safeBlendIn = Mathf.Clamp(
            blendInDuration,
            MinimumOneShotBlendDuration,
            MaximumOneShotBlendDuration);
        float safeBlendOut = Mathf.Clamp(
            blendOutDuration,
            MinimumOneShotBlendDuration,
            MaximumOneShotBlendDuration);
        float combinedBlendDuration = safeBlendIn + safeBlendOut;
        if (combinedBlendDuration > safeDuration && combinedBlendDuration > 0f)
        {
            float scale = safeDuration / combinedBlendDuration;
            safeBlendIn *= scale;
            safeBlendOut *= scale;
        }

        float elapsed = Mathf.Clamp(elapsedTime, 0f, safeDuration);
        float initialWeight = Mathf.Clamp01(startWeight);
        if (safeBlendIn > 0f && elapsed < safeBlendIn)
        {
            float progress = Mathf.SmoothStep(0f, 1f, elapsed / safeBlendIn);
            return Mathf.Lerp(initialWeight, 1f, progress);
        }

        float fadeOutStart = safeDuration - safeBlendOut;
        if (safeBlendOut > 0f && elapsed >= fadeOutStart)
        {
            float progress = Mathf.SmoothStep(0f, 1f, (elapsed - fadeOutStart) / safeBlendOut);
            return 1f - progress;
        }

        return elapsed >= safeDuration ? 0f : 1f;
    }

    private void LoopPlayable(AnimationClipPlayable playable, AnimationClip clip)
    {
        if (!playable.IsValid() || clip == null || clip.length <= 0f)
        {
            return;
        }

        double time = playable.GetTime();
        if (time < clip.length)
        {
            return;
        }

        playable.SetTime(time % clip.length);
    }

    private void PlayOneShot(AnimationClip clip, float playbackSpeed)
    {
        EnsurePlayableGraph();
        if (!hasPlayableGraph || clip == null)
        {
            return;
        }

        float currentWeight = oneShotPlayable.IsValid()
            ? Mathf.Clamp01(rootMixer.GetInputWeight(1))
            : 0f;
        if (oneShotPlayable.IsValid())
        {
            playableGraph.Disconnect(rootMixer, 1);
            oneShotPlayable.Destroy();
        }

        float speed = NPCActionTiming.ClampPlaybackSpeed(playbackSpeed);
        activeOneShotDuration = clip.length / speed;
        oneShotTimer = activeOneShotDuration;
        oneShotElapsedTime = 0f;
        oneShotStartWeight = currentWeight;
        oneShotPlayable = AnimationClipPlayable.Create(playableGraph, clip);
        oneShotPlayable.SetApplyFootIK(false);
        oneShotPlayable.SetSpeed(speed);
        oneShotPlayable.SetTime(0d);
        playableGraph.Connect(oneShotPlayable, 0, rootMixer, 1);
        rootMixer.SetInputWeight(0, 1f - currentWeight);
        rootMixer.SetInputWeight(1, currentWeight);
    }

    private void OnDisable()
    {
        DestroyPlayableGraph();
    }

    private void OnDestroy()
    {
        DestroyPlayableGraph();
    }

    private void DestroyPlayableGraph()
    {
        if (!hasPlayableGraph)
        {
            return;
        }

        playableGraph.Destroy();
        hasPlayableGraph = false;
    }
}
