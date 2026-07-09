using Unity.Netcode;
using UnityEngine;

public enum ActionImpactEffectType
{
    Default = 0,
}

public class ActionImpactEffectSpawner : NetworkBehaviour
{
    [SerializeField] private ParticleSystem defaultImpactEffectPrefab;
    [SerializeField] private float fallbackEffectLifetime = 1.5f;

    public void SpawnImpact(Vector3 position, Vector3 normal, ActionImpactEffectType effectType = ActionImpactEffectType.Default)
    {
        normal = GetSafeNormal(normal);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        {
            if (IsServer)
            {
                SpawnImpactClientRpc(position, normal, effectType);
            }
            else
            {
                RequestSpawnImpactServerRpc(position, normal, effectType);
            }

            return;
        }

        SpawnImpactLocal(position, normal, effectType);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSpawnImpactServerRpc(Vector3 position, Vector3 normal, ActionImpactEffectType effectType)
    {
        SpawnImpactClientRpc(position, GetSafeNormal(normal), effectType);
    }

    [ClientRpc]
    private void SpawnImpactClientRpc(Vector3 position, Vector3 normal, ActionImpactEffectType effectType)
    {
        SpawnImpactLocal(position, GetSafeNormal(normal), effectType);
    }

    private void SpawnImpactLocal(Vector3 position, Vector3 normal, ActionImpactEffectType effectType)
    {
        Quaternion rotation = Quaternion.LookRotation(GetSafeNormal(normal));

        if (defaultImpactEffectPrefab != null)
        {
            ParticleSystem instance = Instantiate(defaultImpactEffectPrefab, position, rotation);
            instance.Play(true);

            ParticleSystem.MainModule main = instance.main;
            if (main.stopAction != ParticleSystemStopAction.Destroy)
            {
                Destroy(instance.gameObject, GetEffectLifetime(instance));
            }

            return;
        }

        SpawnFallbackEffect(position, rotation);
    }

    private void SpawnFallbackEffect(Vector3 position, Quaternion rotation)
    {
        GameObject effectObject = new GameObject("ActionImpactEffect");
        effectObject.transform.SetPositionAndRotation(position, rotation);

        ParticleSystem particleSystem = effectObject.AddComponent<ParticleSystem>();
        particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particleSystem.main;
        main.duration = 0.2f;
        main.loop = false;
        main.startLifetime = 0.35f;
        main.startSpeed = 2.4f;
        main.startSize = 0.08f;
        main.maxParticles = 24;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.Destroy;

        ParticleSystem.EmissionModule emission = particleSystem.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

        ParticleSystem.ShapeModule shape = particleSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 25f;
        shape.radius = 0.04f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particleSystem.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.82f, 0.35f), 0f),
                new GradientColorKey(new Color(0.55f, 0.55f, 0.55f), 1f),
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(0f, 1f),
            });
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer renderer = effectObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;

        particleSystem.Play(true);
    }

    private float GetEffectLifetime(ParticleSystem particleSystem)
    {
        ParticleSystem.MainModule main = particleSystem.main;
        return Mathf.Max(fallbackEffectLifetime, main.duration + main.startLifetime.constantMax + 0.1f);
    }

    private Vector3 GetSafeNormal(Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.0001f)
        {
            return Vector3.up;
        }

        return normal.normalized;
    }
}
