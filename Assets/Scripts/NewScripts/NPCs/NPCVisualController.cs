using UnityEngine;

public class NPCVisualController : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Transform modelRoot;
    [SerializeField] private Renderer[] renderers;
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip walkClip;
    [SerializeField] private AnimationClip noticeClip;
    [SerializeField] private AnimationClip actionClip;
    [SerializeField] private AnimationClip hitReactionClip;

    public Animator Animator => animator;
    public Transform ModelRoot => modelRoot;
    public Renderer[] Renderers => renderers;
    public AnimationClip IdleClip => idleClip;
    public AnimationClip WalkClip => walkClip;
    public AnimationClip NoticeClip => noticeClip;
    public AnimationClip ActionClip => actionClip;
    public AnimationClip HitReactionClip => hitReactionClip;

    private void Awake()
    {
        CacheReferences();
    }

    private void OnValidate()
    {
        CacheReferences();
    }

    public void SetAnimator(Animator targetAnimator)
    {
        animator = targetAnimator;
    }

    public void SetAnimationClips(AnimationClip idle, AnimationClip walk, AnimationClip notice, AnimationClip action, AnimationClip hitReaction)
    {
        idleClip = idle;
        walkClip = walk;
        noticeClip = notice;
        actionClip = action;
        hitReactionClip = hitReaction;
    }

    public void CacheReferences()
    {
        if (modelRoot == null)
        {
            Transform existingModelRoot = transform.Find("ModelRoot");
            modelRoot = existingModelRoot != null ? existingModelRoot : transform;
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        if (renderers == null || renderers.Length == 0)
        {
            renderers = GetComponentsInChildren<Renderer>(true);
        }
    }
}
