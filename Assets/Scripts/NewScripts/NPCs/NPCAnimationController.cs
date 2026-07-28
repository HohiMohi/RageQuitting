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

public class NPCAnimationController : NetworkBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private NPCVisualController visualController;
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private NPCCarrier carrier;
    [SerializeField] private NPCHealth health;
    [SerializeField] private float walkSpeedReference = 3.5f;
    [SerializeField] private float idleSpeedThreshold = 0.05f;
    [SerializeField] private float speedDampTime = 0.12f;

    private readonly NetworkVariable<float> speedNormalizedNetwork = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<NPCAnimationState> stateNetwork = new NetworkVariable<NPCAnimationState>(
        NPCAnimationState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> triggerSequenceNetwork = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<NPCAnimationTrigger> triggerNetwork = new NetworkVariable<NPCAnimationTrigger>(
        NPCAnimationTrigger.None,
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
    private AnimationClip activeOneShotClip;
    private float oneShotTimer;
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

    public void PlayAction()
    {
        PlayTrigger(NPCAnimationTrigger.Action);
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
        triggerSequenceNetwork.OnValueChanged += TriggerSequenceNetwork_OnValueChanged;

        if (IsServer)
        {
            UpdateNetworkAnimationState();
        }

        ApplyCurrentNetworkState();
    }

    public override void OnNetworkDespawn()
    {
        triggerSequenceNetwork.OnValueChanged -= TriggerSequenceNetwork_OnValueChanged;
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

    private void PlayTrigger(NPCAnimationTrigger trigger)
    {
        if (trigger == NPCAnimationTrigger.None)
        {
            return;
        }

        if (IsNetworkSessionActive)
        {
            if (IsServer)
            {
                SetNetworkTrigger(trigger);
            }
            else
            {
                RequestPlayTriggerServerRpc(trigger);
            }

            return;
        }

        ApplyTrigger(trigger);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPlayTriggerServerRpc(NPCAnimationTrigger trigger)
    {
        SetNetworkTrigger(trigger);
    }

    private void SetNetworkTrigger(NPCAnimationTrigger trigger)
    {
        triggerNetwork.Value = trigger;
        triggerSequenceNetwork.Value++;
    }

    private void TriggerSequenceNetwork_OnValueChanged(int previousValue, int newValue)
    {
        if (newValue == lastHandledTriggerSequence)
        {
            return;
        }

        lastHandledTriggerSequence = newValue;
        ApplyTrigger(triggerNetwork.Value);
    }

    private void ApplyTrigger(NPCAnimationTrigger trigger)
    {
        if (!EnsureAnimatorReady())
        {
            return;
        }

        AnimationClip clip = GetTriggerClip(trigger);
        if (clip != null)
        {
            PlayOneShot(clip);
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
            oneShotTimer -= Time.deltaTime;
            float oneShotWeight = Mathf.Clamp01(oneShotTimer / Mathf.Max(0.01f, activeOneShotClip != null ? activeOneShotClip.length : 0.01f));
            oneShotWeight = Mathf.Sin(oneShotWeight * Mathf.PI);
            rootMixer.SetInputWeight(0, 1f - oneShotWeight);
            rootMixer.SetInputWeight(1, oneShotWeight);
            return;
        }

        rootMixer.SetInputWeight(0, 1f);
        rootMixer.SetInputWeight(1, 0f);
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

    private void PlayOneShot(AnimationClip clip)
    {
        EnsurePlayableGraph();
        if (!hasPlayableGraph || clip == null)
        {
            return;
        }

        if (oneShotPlayable.IsValid())
        {
            playableGraph.Disconnect(rootMixer, 1);
            oneShotPlayable.Destroy();
        }

        activeOneShotClip = clip;
        oneShotTimer = clip.length;
        oneShotPlayable = AnimationClipPlayable.Create(playableGraph, clip);
        oneShotPlayable.SetApplyFootIK(false);
        oneShotPlayable.SetTime(0d);
        playableGraph.Connect(oneShotPlayable, 0, rootMixer, 1);
        rootMixer.SetInputWeight(1, 1f);
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
