using System;
using System.Collections.Generic;
using UnityEngine;

public class CarpenterDimensionCrank : MonoBehaviour, IInteractableNew, IInteractionPromptProvider
{
    [SerializeField] private CarpenterTableFactory factory;
    [SerializeField] private ComponentDimension dimension;
    [SerializeField] private Transform rotatingVisual;
    [SerializeField] private Vector3 rotationAxis = Vector3.up;
    [SerializeField] private float degreesPerStep = 30f;

    private Quaternion initialVisualRotation;
    private bool initialized;

    public ComponentDimension Dimension => dimension;
    public bool IsAvailable => factory != null && factory.IsDimensionCrankAvailable(dimension);

    private void Awake()
    {
        Initialize(factory != null ? factory : GetComponentInParent<CarpenterTableFactory>());
    }

    private void OnDestroy()
    {
        if (factory != null && initialized)
        {
            factory.OnFactoryStateChanged -= Factory_OnFactoryStateChanged;
        }
    }

    public void Initialize(CarpenterTableFactory ownerFactory)
    {
        if (ownerFactory == null)
        {
            return;
        }

        if (initialized && factory == ownerFactory)
        {
            RefreshVisual();
            return;
        }

        if (factory != null && initialized)
        {
            factory.OnFactoryStateChanged -= Factory_OnFactoryStateChanged;
        }

        factory = ownerFactory;
        if (rotatingVisual == null)
        {
            rotatingVisual = transform;
        }

        initialVisualRotation = rotatingVisual.localRotation;
        factory.OnFactoryStateChanged += Factory_OnFactoryStateChanged;
        initialized = true;
        RefreshVisual();
    }

    public void Interact(Transform interactor)
    {
        factory?.RequestBeginDimensionAdjustment(dimension, interactor);
    }

    public void LookedAt(Transform interactor)
    {
    }

    public void LookedAway(Transform interactor)
    {
    }

    public void GetInteractionPrompts(Transform interactor, List<InteractionPrompt> prompts)
    {
        string dimensionName = dimension == ComponentDimension.Width ? "width" : "length";
        prompts.Add(new InteractionPrompt(
            PlayerInputActionKind.Interact,
            IsAvailable ? $"Adjust {dimensionName}" : "Crank in use"));
    }

    public void RefreshVisual()
    {
        if (factory == null || rotatingVisual == null)
        {
            return;
        }

        Vector3 axis = rotationAxis.sqrMagnitude > 0.0001f ? rotationAxis.normalized : Vector3.up;
        float angle = factory.GetDimensionStepIndex(dimension) * Mathf.Max(1f, degreesPerStep);
        rotatingVisual.localRotation = initialVisualRotation * Quaternion.AngleAxis(angle, axis);
    }

    private void Factory_OnFactoryStateChanged(object sender, EventArgs e)
    {
        RefreshVisual();
    }
}
