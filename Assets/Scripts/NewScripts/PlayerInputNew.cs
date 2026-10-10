using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputNew : NetworkBehaviour
{
    private PlayerGameInputActions playerGameInputActions;
    private InputAction singleCarryPlacementToggleAction;
    private InputAction singleCarryPlacementRotationAction;
    private bool inputInitialized;
    private bool singleCarryPlacementActive;
    private PlayerSingleCarryPlacementController placementController;

    public EventHandler<OnSprintArgs> OnSprint;

    public class OnSprintArgs : EventArgs
    {
        public bool IsSprinting;
    }

    public EventHandler OnInteract;
    public EventHandler OnInteractCanceled;
    public EventHandler OnJump;
    public EventHandler OnAction;
    public EventHandler OnActionCanceled;
    public EventHandler OnActionAlt;
    public EventHandler OnActionAltCanceled;
    public EventHandler OnSwapItems;
    public EventHandler OnDropItem;
    public EventHandler OnToggleBridgeRequirements;
    public event Action<Vector2> OnBridgeRequirementsScroll;
    public EventHandler OnToggleRestartMenu;
    public EventHandler OnUI_Interact;
    public EventHandler OnUI_Up;
    public EventHandler OnUI_Down;
    public EventHandler OnUI_Left;
    public EventHandler OnUI_Right;
    public EventHandler OnUI_Back;
    public EventHandler OnDismissInfoOverlay;
    public event EventHandler OnToggleSingleCarryPlacement;
    public event EventHandler OnCancelSingleCarryPlacement;
    public event EventHandler OnConfirmSingleCarryPlacement;
    public event EventHandler OnGameplayMenuRequested;
    public event EventHandler OnForcedSingleCarryPlacementExit;

    public bool IsSingleCarryPlacementActive => singleCarryPlacementActive;
    public bool IsLocalGameplayInputActive => IsInputActive() && !IsUIOpened;

    [SerializeField]
    private bool IsUIOpened;

    public bool IsGameplayUiOpen => IsUIOpened;

    [Header("Temp")]
    public bool analogMovement;

    [Header("Mouse Cursor Settings")]
    public bool cursorInputForLook = true;
    private bool cursorLocked = true;

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        InitializeInput();
        SetCursorState(cursorLocked);
    }

    public override void OnNetworkDespawn()
    {
        DisposeInput();
    }

    private void Awake()
    {
        IsUIOpened = false;
    }

    private void Start()
    {
        if (ShouldRunAsLocalPlayer() && !inputInitialized)
        {
            InitializeInput();
            SetCursorState(cursorLocked);
        }

        BaseFactory.OnInteractBaseFactory += BaseFactory_OnInteract;
        FactoryInteractionUI.OnAnyUIClosed += FactoryInteractionUI_OnInteract;
    }

    public override void OnDestroy()
    {
        BaseFactory.OnInteractBaseFactory -= BaseFactory_OnInteract;
        FactoryInteractionUI.OnAnyUIClosed -= FactoryInteractionUI_OnInteract;
        DisposeInput();
        base.OnDestroy();
    }

    private void InitializeInput()
    {
        if (inputInitialized)
        {
            return;
        }

        inputInitialized = true;
        playerGameInputActions = new PlayerGameInputActions();

        playerGameInputActions.Game.Jump.performed += Jump_performed;
        playerGameInputActions.Game.Sprint.performed += Sprint_performed;
        playerGameInputActions.Game.Sprint.canceled += Sprint_canceled;
        playerGameInputActions.Game.Interact.performed += Interact_performed;
        playerGameInputActions.Game.Interact.canceled += Interact_canceled;
        playerGameInputActions.Game.Action.performed += Action_performed;
        playerGameInputActions.Game.Action.canceled += Action_canceled;
        playerGameInputActions.Game.ActionAlt.performed += ActionAlt_performed;
        playerGameInputActions.Game.ActionAlt.canceled += ActionAlt_canceled;
        playerGameInputActions.Game.SwapItems.performed += SwapItems_performed;
        playerGameInputActions.Game.DropItem.performed += DropItem_performed;
        playerGameInputActions.Game.ToggleBridgeRequirements.performed += ToggleBridgeRequirements_performed;
        playerGameInputActions.Game.BridgeRequirementsScroll.performed += BridgeRequirementsScroll_performed;
        playerGameInputActions.Game.ToggleRestartMenu.performed += ToggleRestartMenu_performed;
        playerGameInputActions.UI.Up.performed += UI_Up_performed;
        playerGameInputActions.UI.Down.performed += UI_Down_performed;
        playerGameInputActions.UI.Left.performed += UI_Left_performed;
        playerGameInputActions.UI.Right.performed += UI_Right_performed;
        playerGameInputActions.UI.Back.performed += UI_Back_performed;
        playerGameInputActions.Game.Enable();
        playerGameInputActions.UI.Enable();
        singleCarryPlacementToggleAction = playerGameInputActions.Game.ToggleSingleCarryPlacement;
        singleCarryPlacementToggleAction.performed += SingleCarryPlacementToggle_performed;
        singleCarryPlacementRotationAction = playerGameInputActions.Game.SingleCarryPlacementRotation;
        placementController = null;
        foreach (Component attached in GetComponents<Component>())
        {
            if (attached is PlayerSingleCarryPlacementController controller)
            {
                placementController = controller;
                break;
            }
        }
        if (placementController == null) placementController = gameObject.AddComponent<PlayerSingleCarryPlacementController>();
    }

    private void DisposeInput()
    {
        if (singleCarryPlacementActive)
            OnForcedSingleCarryPlacementExit?.Invoke(this, EventArgs.Empty);
        singleCarryPlacementActive = false;
        if (!inputInitialized || playerGameInputActions == null)
        {
            return;
        }
        playerGameInputActions.Game.Disable();
        playerGameInputActions.UI.Disable();
        if (singleCarryPlacementToggleAction != null)
        {
            singleCarryPlacementToggleAction.performed -= SingleCarryPlacementToggle_performed;
            singleCarryPlacementToggleAction = null;
        }
        singleCarryPlacementRotationAction = null;
        playerGameInputActions.Dispose();
        playerGameInputActions = null;
        inputInitialized = false;
    }

    private bool ShouldRunAsLocalPlayer()
    {
        if (IsSpawned)
        {
            return IsOwner;
        }

        return NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening;
    }

    private bool IsInputActive()
    {
        return inputInitialized && ShouldRunAsLocalPlayer();
    }

    private void UI_Back_performed(InputAction.CallbackContext context)
    {
        if (singleCarryPlacementActive)
        {
            OnCancelSingleCarryPlacement?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (IsUIOpened)
        {
            OnUI_Back?.Invoke(this, EventArgs.Empty);
            return;
        }

        OnDismissInfoOverlay?.Invoke(this, EventArgs.Empty);
    }

    private void UI_Right_performed(InputAction.CallbackContext context)
    {
        OnUI_Right?.Invoke(this, EventArgs.Empty);
    }

    private void UI_Left_performed(InputAction.CallbackContext context)
    {
        OnUI_Left?.Invoke(this, EventArgs.Empty);
    }

    private void UI_Down_performed(InputAction.CallbackContext context)
    {
        OnUI_Down?.Invoke(this, EventArgs.Empty);
    }

    private void UI_Up_performed(InputAction.CallbackContext context)
    {
        OnUI_Up?.Invoke(this, EventArgs.Empty);
    }

    private void FactoryInteractionUI_OnInteract(object sender, EventArgs e)
    {
        SetGameplayUiOpen(false);
    }

    private void BaseFactory_OnInteract(object sender, EventArgs e)
    {
        SetGameplayUiOpen(true);
    }

    private void DropItem_performed(InputAction.CallbackContext context)
    {
        if (IsUIOpened || singleCarryPlacementActive)
        {
            return;
        }

        OnDropItem?.Invoke(this, EventArgs.Empty);
    }

    private void ToggleBridgeRequirements_performed(InputAction.CallbackContext context)
    {
        if (IsUIOpened || singleCarryPlacementActive)
        {
            return;
        }

        OnToggleBridgeRequirements?.Invoke(this, EventArgs.Empty);
    }

    private void BridgeRequirementsScroll_performed(InputAction.CallbackContext context)
    {
        if (singleCarryPlacementActive) return;
        RouteBridgeRequirementsScroll(context.ReadValue<Vector2>());
    }

    private void RouteBridgeRequirementsScroll(Vector2 scroll)
    {
        if (IsUIOpened || !IsInputActive()) return;
        if (Mathf.Abs(scroll.y) > 0.001f)
        {
            OnBridgeRequirementsScroll?.Invoke(scroll);
        }
    }

    private void ToggleRestartMenu_performed(InputAction.CallbackContext context)
    {
        if (singleCarryPlacementActive) OnGameplayMenuRequested?.Invoke(this, EventArgs.Empty);
        OnToggleRestartMenu?.Invoke(this, EventArgs.Empty);
    }

    private void SwapItems_performed(InputAction.CallbackContext context)
    {
        if (IsUIOpened || singleCarryPlacementActive)
        {
            return;
        }

        OnSwapItems?.Invoke(this, EventArgs.Empty);
    }

    private void Action_canceled(InputAction.CallbackContext context)
    {
        OnActionCanceled?.Invoke(this, EventArgs.Empty);
    }

    private void ActionAlt_canceled(InputAction.CallbackContext context)
    {
        OnActionAltCanceled?.Invoke(this, EventArgs.Empty);
    }

    private void ActionAlt_performed(InputAction.CallbackContext context)
    {
        if (IsUIOpened || singleCarryPlacementActive)
        {
            return;
        }

        OnActionAlt?.Invoke(this, EventArgs.Empty);
    }

    private void Action_performed(InputAction.CallbackContext context)
    {
        if (IsUIOpened || singleCarryPlacementActive)
        {
            return;
        }

        OnAction?.Invoke(this, EventArgs.Empty);
    }

    private void Interact_performed(InputAction.CallbackContext context)
    {
        if (singleCarryPlacementActive)
        {
            OnConfirmSingleCarryPlacement?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (IsUIOpened)
        {
            OnUI_Interact?.Invoke(this, EventArgs.Empty);
            return;
        }

        OnInteract?.Invoke(this, EventArgs.Empty);
    }

    private void Interact_canceled(InputAction.CallbackContext context)
    {
        OnInteractCanceled?.Invoke(this, EventArgs.Empty);
    }

    private void Sprint_canceled(InputAction.CallbackContext context)
    {
        OnSprint?.Invoke(this, new OnSprintArgs
        {
            IsSprinting = false
        });
    }

    private void Sprint_performed(InputAction.CallbackContext context)
    {
        if (IsUIOpened || singleCarryPlacementActive)
        {
            return;
        }

        OnSprint?.Invoke(this, new OnSprintArgs
        {
            IsSprinting = true
        });
    }

    private void Jump_performed(InputAction.CallbackContext context)
    {
        if (IsUIOpened || singleCarryPlacementActive)
        {
            return;
        }

        OnJump?.Invoke(this, EventArgs.Empty);
    }

    public void SetGameplayUiOpen(bool isOpen)
    {
        if (!ShouldRunAsLocalPlayer() || IsUIOpened == isOpen)
        {
            return;
        }

        if (isOpen && singleCarryPlacementActive) OnGameplayMenuRequested?.Invoke(this, EventArgs.Empty);
        IsUIOpened = isOpen;
        if (isOpen)
        {
            OnSprint?.Invoke(this, new OnSprintArgs { IsSprinting = false });
            OnActionCanceled?.Invoke(this, EventArgs.Empty);
            OnActionAltCanceled?.Invoke(this, EventArgs.Empty);
        }

        SetCursorState(cursorLocked && !isOpen);
    }

    private void SingleCarryPlacementToggle_performed(InputAction.CallbackContext context)
    {
        if (!isActiveAndEnabled || !IsInputActive() || IsUIOpened) return;
        OnToggleSingleCarryPlacement?.Invoke(this, EventArgs.Empty);
    }

    public void SetSingleCarryPlacementActive(bool active)
    {
        singleCarryPlacementActive = active;
        if (!active) return;
        OnSprint?.Invoke(this, new OnSprintArgs { IsSprinting = false });
        OnActionCanceled?.Invoke(this, EventArgs.Empty);
        OnActionAltCanceled?.Invoke(this, EventArgs.Empty);
        OnInteractCanceled?.Invoke(this, EventArgs.Empty);
    }

    public Vector2 GetLookDeltaValue()
    {
        if (!IsInputActive() || IsUIOpened)
        {
            return Vector2.zero;
        }

        if (!cursorInputForLook)
        {
            return Vector2.zero;
        }

        Vector2 inputVector = playerGameInputActions.Game.Look.ReadValue<Vector2>();
        return new Vector2(inputVector.x, -inputVector.y);
    }

    public Vector2 GetSingleCarryPlacementRotationInput()
    {
        return singleCarryPlacementActive && singleCarryPlacementRotationAction != null
            ? singleCarryPlacementRotationAction.ReadValue<Vector2>() : Vector2.zero;
    }

    public Vector2 GetLookDeltaValueForMinigames()
    {
        if (!IsInputActive())
        {
            return Vector2.zero;
        }

        Vector2 inputVector = playerGameInputActions.Game.Look.ReadValue<Vector2>();
        return new Vector2(inputVector.x, inputVector.y);
    }

    public Vector2 GetMoveVectorValue()
    {
        if (!IsInputActive() || IsUIOpened || singleCarryPlacementActive)
        {
            return Vector2.zero;
        }

        return playerGameInputActions.Game.Move.ReadValue<Vector2>();
    }

    public string GetInputDisplayName(PlayerInputActionKind actionKind)
    {
        return actionKind switch
        {
            PlayerInputActionKind.Interact => "E",
            PlayerInputActionKind.Action => "LMB",
            PlayerInputActionKind.ActionAlt => "RMB",
            _ => string.Empty
        };
    }

    private void OnEnable()
    {
        if (!inputInitialized && ShouldRunAsLocalPlayer())
        {
            InitializeInput();
            SetCursorState(cursorLocked && !IsUIOpened);
            return;
        }
        if (inputInitialized && playerGameInputActions != null)
        {
            playerGameInputActions.Game.Enable();
            playerGameInputActions.UI.Enable();
            SetCursorState(cursorLocked && !IsUIOpened);
        }
        singleCarryPlacementToggleAction?.Enable();
    }

    protected void OnDisable()
    {
        DisposeInput();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (IsInputActive())
        {
            SetCursorState(cursorLocked && !IsUIOpened);
        }
    }

    private void SetCursorState(bool newState)
    {
        Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !newState;
    }
}
