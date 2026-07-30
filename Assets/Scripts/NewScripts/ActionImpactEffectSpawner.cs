using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum ActionImpactSurfaceType
{
    Default = 0,
    Wood,
    Stone,
    Metal,
    Soil,
    Flesh
}

public interface IActionImpactSurfaceProvider
{
    ActionImpactSurfaceType ImpactSurfaceType { get; }
}

[Serializable]
public struct ActionImpactFeedbackEntry
{
    public ActionImpactSurfaceType surfaceType;
    public ParticleSystem effectPrefab;
    public AudioClip impactClip;
    [Range(0f, 1f)] public float volume;
}

public class ActionImpactEffectSpawner : NetworkBehaviour
{
    [SerializeField] private ParticleSystem defaultImpactEffectPrefab;
    [SerializeField] private float fallbackEffectLifetime = 1.5f;
    [SerializeField] private ActionImpactFeedbackEntry[] surfaceFeedback;
    [SerializeField, Range(0f, 1f)] private float fallbackAudioVolume = 0.55f;

    private static readonly Dictionary<ActionImpactSurfaceType, AudioClip> fallbackClips =
        new Dictionary<ActionImpactSurfaceType, AudioClip>();

    public void SpawnImpact(
        Vector3 position,
        Vector3 normal,
        ActionImpactSurfaceType surfaceType = ActionImpactSurfaceType.Default,
        float strength = 1f)
    {
        normal = GetSafeNormal(normal);
        strength = Mathf.Max(0f, strength);

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && IsSpawned)
        {
            if (IsServer)
            {
                SpawnImpactClientRpc(position, normal, surfaceType, strength);
            }
            else
            {
                RequestSpawnImpactServerRpc(position, normal, surfaceType, strength);
            }

            return;
        }

        SpawnImpactLocal(position, normal, surfaceType, strength);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSpawnImpactServerRpc(
        Vector3 position,
        Vector3 normal,
        ActionImpactSurfaceType surfaceType,
        float strength)
    {
        SpawnImpactClientRpc(position, GetSafeNormal(normal), surfaceType, Mathf.Clamp(strength, 0f, 2f));
    }

    [ClientRpc]
    private void SpawnImpactClientRpc(
        Vector3 position,
        Vector3 normal,
        ActionImpactSurfaceType surfaceType,
        float strength)
    {
        SpawnImpactLocal(position, GetSafeNormal(normal), surfaceType, Mathf.Clamp(strength, 0f, 2f));
    }

    private void SpawnImpactLocal(
        Vector3 position,
        Vector3 normal,
        ActionImpactSurfaceType surfaceType,
        float strength)
    {
        Quaternion rotation = Quaternion.LookRotation(GetSafeNormal(normal));
        ActionImpactFeedbackEntry feedback = GetFeedback(surfaceType);
        ParticleSystem effectPrefab = feedback.effectPrefab != null
            ? feedback.effectPrefab
            : defaultImpactEffectPrefab;

        if (effectPrefab != null)
        {
            ParticleSystem instance = Instantiate(effectPrefab, position, rotation);
            ParticleSystem.MainModule effectMain = instance.main;
            effectMain.startSizeMultiplier *= Mathf.Lerp(0.75f, 1.25f, Mathf.Clamp01(strength));
            instance.Play(true);

            if (effectMain.stopAction != ParticleSystemStopAction.Destroy)
            {
                Destroy(instance.gameObject, GetEffectLifetime(instance));
            }
        }
        else
        {
            SpawnFallbackEffect(position, rotation, surfaceType, strength);
        }

        PlayImpactAudio(position, surfaceType, feedback, strength);
    }

    private void SpawnFallbackEffect(
        Vector3 position,
        Quaternion rotation,
        ActionImpactSurfaceType surfaceType,
        float strength)
    {
        GameObject effectObject = new GameObject("ActionImpactEffect");
        effectObject.transform.SetPositionAndRotation(position, rotation);

        ParticleSystem particleSystem = effectObject.AddComponent<ParticleSystem>();
        particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = particleSystem.main;
        main.duration = 0.2f;
        main.loop = false;
        main.startLifetime = 0.35f;
        main.startSpeed = 2.4f * Mathf.Lerp(0.8f, 1.2f, Mathf.Clamp01(strength));
        main.startSize = 0.08f * Mathf.Lerp(0.8f, 1.2f, Mathf.Clamp01(strength));
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
        Color startColor = GetSurfaceColor(surfaceType);
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(startColor, 0f),
                new GradientColorKey(Color.Lerp(startColor, Color.gray, 0.65f), 1f),
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

    private ActionImpactFeedbackEntry GetFeedback(ActionImpactSurfaceType surfaceType)
    {
        if (surfaceFeedback != null)
        {
            for (int i = 0; i < surfaceFeedback.Length; i++)
            {
                if (surfaceFeedback[i].surfaceType == surfaceType)
                {
                    return surfaceFeedback[i];
                }
            }
        }

        return new ActionImpactFeedbackEntry
        {
            surfaceType = surfaceType,
            volume = fallbackAudioVolume
        };
    }

    private void PlayImpactAudio(
        Vector3 position,
        ActionImpactSurfaceType surfaceType,
        ActionImpactFeedbackEntry feedback,
        float strength)
    {
        AudioClip clip = feedback.impactClip != null ? feedback.impactClip : GetFallbackClip(surfaceType);
        if (clip == null)
        {
            return;
        }

        float volume = feedback.volume > 0f ? feedback.volume : fallbackAudioVolume;
        AudioSource.PlayClipAtPoint(clip, position, Mathf.Clamp01(volume * Mathf.Max(0.25f, strength)));
    }

    private static AudioClip GetFallbackClip(ActionImpactSurfaceType surfaceType)
    {
        if (fallbackClips.TryGetValue(surfaceType, out AudioClip existing) && existing != null)
        {
            return existing;
        }

        const int sampleRate = 22050;
        const float duration = 0.11f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        AudioClip clip = AudioClip.Create($"Fallback_{surfaceType}_Impact", sampleCount, 1, sampleRate, false);
        float[] samples = new float[sampleCount];
        float frequency = surfaceType switch
        {
            ActionImpactSurfaceType.Metal => 920f,
            ActionImpactSurfaceType.Stone => 260f,
            ActionImpactSurfaceType.Wood => 180f,
            ActionImpactSurfaceType.Soil => 90f,
            ActionImpactSurfaceType.Flesh => 120f,
            _ => 220f
        };

        System.Random random = new System.Random(913 + (int)surfaceType * 101);
        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-30f * t);
            float tone = Mathf.Sin(Mathf.PI * 2f * frequency * t);
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            float noiseMix = surfaceType == ActionImpactSurfaceType.Metal ? 0.15f : 0.45f;
            samples[i] = (tone * (1f - noiseMix) + noise * noiseMix) * envelope * 0.35f;
        }

        clip.SetData(samples, 0);
        fallbackClips[surfaceType] = clip;
        return clip;
    }

    private static Color GetSurfaceColor(ActionImpactSurfaceType surfaceType)
    {
        return surfaceType switch
        {
            ActionImpactSurfaceType.Wood => new Color(0.68f, 0.39f, 0.16f),
            ActionImpactSurfaceType.Stone => new Color(0.55f, 0.57f, 0.6f),
            ActionImpactSurfaceType.Metal => new Color(1f, 0.72f, 0.2f),
            ActionImpactSurfaceType.Soil => new Color(0.42f, 0.27f, 0.12f),
            ActionImpactSurfaceType.Flesh => new Color(0.68f, 0.12f, 0.1f),
            _ => new Color(1f, 0.82f, 0.35f)
        };
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
