using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(200)]
public class PlayerTargetHighlightController : MonoBehaviour
{
    [SerializeField] private PlayerInteractionNew playerInteraction;
    [SerializeField] private LookingAtComponentUI lookingAtUi;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private Material outlineMaterial;
    [SerializeField] private Color outlineColor = Color.white;
    [SerializeField] private Color unavailableOutlineColor = Color.red;
    [SerializeField, Range(0f, 8f)] private float outlineWidth = 3f;
    [SerializeField, Min(0.01f)] private float fadeInDuration = 0.12f;
    [SerializeField, Min(0.01f)] private float fadeOutDuration = 0.18f;
    [SerializeField, Min(0.01f)] private float unavailableFlashDuration = 0.35f;

    private readonly List<OutlineTarget> targets = new List<OutlineTarget>();
    private MonoBehaviour currentTarget;
    private MonoBehaviour unavailableFlashTarget;
    private float unavailableFlashEndsAt;

    private void Awake()
    {
        playerInteraction ??= GetComponent<PlayerInteractionNew>();
        playerHealth ??= GetComponent<PlayerHealth>();
        lookingAtUi ??= GetComponentInChildren<LookingAtComponentUI>(true);
    }

    private void OnEnable()
    {
        if (playerInteraction != null)
        {
            playerInteraction.OnSharedCarryPickupRejected += HandleSharedCarryPickupRejected;
        }
    }

    private void LateUpdate()
    {
        if (unavailableFlashTarget != null && Time.unscaledTime >= unavailableFlashEndsAt)
        {
            unavailableFlashTarget = null;
        }

        MonoBehaviour desiredTarget = GetDesiredTarget();
        if (desiredTarget != currentTarget)
        {
            ChangeTarget(desiredTarget);
        }

        float deltaTime = Time.unscaledDeltaTime;
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            OutlineTarget target = targets[i];
            float duration = target.IsFadingOut ? fadeOutDuration : fadeInDuration;
            float direction = target.IsFadingOut ? -1f : 1f;
            target.Intensity = Mathf.Clamp01(target.Intensity + direction * deltaTime / duration);
            Color color = target.Target == unavailableFlashTarget
                ? unavailableOutlineColor
                : outlineColor;
            target.Apply(color, outlineWidth);

            if (target.IsFadingOut && target.Intensity <= 0f)
            {
                target.Dispose();
                targets.RemoveAt(i);
            }
        }
    }

    private MonoBehaviour GetDesiredTarget()
    {
        if (!isActiveAndEnabled || outlineMaterial == null || playerInteraction == null ||
            (playerHealth != null && playerHealth.IsDowned))
        {
            return null;
        }

        if (unavailableFlashTarget != null)
        {
            return unavailableFlashTarget;
        }

        MonoBehaviour target = playerInteraction.CurrentTarget;
        return target != null &&
               lookingAtUi != null &&
               lookingAtUi.EvaluatedTarget == target &&
               lookingAtUi.CurrentTargetHasActionablePrompt
            ? target
            : null;
    }

    public void FlashUnavailable(MonoBehaviour target = null)
    {
        unavailableFlashTarget = target != null ? target : playerInteraction != null ? playerInteraction.CurrentTarget : null;
        unavailableFlashEndsAt = Time.unscaledTime + unavailableFlashDuration;
    }

    private void HandleSharedCarryPickupRejected(SharedCarryPickupRejectedEventArgs args)
    {
        if (args.Reason == SharedCarryPickupFailureReason.NoAvailableAnchor)
        {
            FlashUnavailable(args.Target);
        }
    }

    private void ChangeTarget(MonoBehaviour target)
    {
        foreach (OutlineTarget existingTarget in targets)
        {
            existingTarget.IsFadingOut = true;
        }

        currentTarget = target;
        if (target == null)
        {
            return;
        }

        OutlineTarget existing = targets.Find(candidate => candidate.Target == target);
        if (existing != null)
        {
            existing.IsFadingOut = false;
            return;
        }

        OutlineTarget newTarget = OutlineTarget.Create(target, outlineMaterial);
        if (newTarget != null)
        {
            targets.Add(newTarget);
        }
    }

    private void OnDisable()
    {
        if (playerInteraction != null)
        {
            playerInteraction.OnSharedCarryPickupRejected -= HandleSharedCarryPickupRejected;
        }

        currentTarget = null;
        unavailableFlashTarget = null;
        foreach (OutlineTarget target in targets)
        {
            target.Dispose();
        }
        targets.Clear();
    }

    private sealed class OutlineTarget
    {
        private readonly List<Renderer> proxyRenderers = new List<Renderer>();

        public MonoBehaviour Target { get; private set; }
        public float Intensity;
        public bool IsFadingOut;

        public static OutlineTarget Create(MonoBehaviour target, Material material)
        {
            List<Renderer> sourceRenderers = new List<Renderer>();
            CollectRenderers(target, sourceRenderers);
            if (sourceRenderers.Count == 0)
            {
                return null;
            }

            OutlineTarget outlineTarget = new OutlineTarget { Target = target };
            foreach (Renderer source in sourceRenderers)
            {
                Renderer proxy = CreateProxy(source, material);
                if (proxy != null)
                {
                    outlineTarget.proxyRenderers.Add(proxy);
                }
            }

            if (outlineTarget.proxyRenderers.Count == 0)
            {
                outlineTarget.Dispose();
                return null;
            }
            return outlineTarget;
        }

        public void Apply(Color color, float width)
        {
            for (int rendererIndex = proxyRenderers.Count - 1; rendererIndex >= 0; rendererIndex--)
            {
                Renderer renderer = proxyRenderers[rendererIndex];
                if (renderer == null)
                {
                    proxyRenderers.RemoveAt(rendererIndex);
                    continue;
                }

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                for (int materialIndex = 0; materialIndex < renderer.sharedMaterials.Length; materialIndex++)
                {
                    renderer.GetPropertyBlock(block, materialIndex);
                    block.SetColor("_OutlineColor", color);
                    block.SetFloat("_OutlineWidth", width);
                    block.SetFloat("_OutlineIntensity", Intensity);
                    renderer.SetPropertyBlock(block, materialIndex);
                }
            }
        }

        public void Dispose()
        {
            foreach (Renderer renderer in proxyRenderers)
            {
                if (renderer != null)
                {
                    Object.Destroy(renderer.gameObject);
                }
            }
            proxyRenderers.Clear();
            Target = null;
        }

        private static void CollectRenderers(MonoBehaviour target, List<Renderer> results)
        {
            if (target is IHighlightRendererProvider provider)
            {
                provider.GetHighlightRenderers(results);
                results.RemoveAll(renderer => !IsSupported(renderer));
                return;
            }

            Transform root = target.transform;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (IsSupported(renderer) && renderer.gameObject.activeInHierarchy && renderer.enabled)
                {
                    results.Add(renderer);
                }
            }
        }

        private static bool IsSupported(Renderer renderer)
        {
            return renderer != null &&
                   (renderer is MeshRenderer || renderer is SkinnedMeshRenderer) &&
                   renderer.GetComponentInParent<Canvas>() == null &&
                   renderer.GetComponent<ParticleSystem>() == null &&
                   renderer.GetComponent<TrailRenderer>() == null &&
                   renderer.GetComponent<LineRenderer>() == null;
        }

        private static Renderer CreateProxy(Renderer source, Material material)
        {
            Mesh mesh = null;
            if (source is MeshRenderer)
            {
                MeshFilter filter = source.GetComponent<MeshFilter>();
                mesh = filter != null ? filter.sharedMesh : null;
            }
            else if (source is SkinnedMeshRenderer skinnedSource)
            {
                mesh = skinnedSource.sharedMesh;
            }

            if (mesh == null)
            {
                return null;
            }

            GameObject proxyObject = new GameObject($"{source.gameObject.name}_TargetOutline");
            proxyObject.layer = source.gameObject.layer;
            proxyObject.transform.SetParent(source.transform, false);
            proxyObject.transform.localPosition = Vector3.zero;
            proxyObject.transform.localRotation = Quaternion.identity;
            proxyObject.transform.localScale = Vector3.one;

            Renderer proxy;
            if (source is SkinnedMeshRenderer skinned)
            {
                SkinnedMeshRenderer skinnedProxy = proxyObject.AddComponent<SkinnedMeshRenderer>();
                skinnedProxy.sharedMesh = mesh;
                skinnedProxy.bones = skinned.bones;
                skinnedProxy.rootBone = skinned.rootBone;
                skinnedProxy.localBounds = skinned.localBounds;
                skinnedProxy.updateWhenOffscreen = skinned.updateWhenOffscreen;
                proxy = skinnedProxy;
            }
            else
            {
                proxyObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                proxy = proxyObject.AddComponent<MeshRenderer>();
            }

            int materialCount = Mathf.Max(1, mesh.subMeshCount);
            Material[] materials = new Material[materialCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            proxy.sharedMaterials = materials;
            proxy.shadowCastingMode = ShadowCastingMode.Off;
            proxy.receiveShadows = false;
            proxy.lightProbeUsage = LightProbeUsage.Off;
            proxy.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return proxy;
        }
    }
}
