using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputNew : MonoBehaviour
{
    private PlayerGameInputActions playerGameInputActions;
    public EventHandler<OnSprintArgs> OnSprint;
    

    public class OnSprintArgs : EventArgs
    { 
        public bool IsSprinting;
    };
    public EventHandler OnInteract;
    public EventHandler OnJump;
    public EventHandler OnAction;
    public EventHandler OnActionCanceled;
    public EventHandler OnActionAlt;
    public EventHandler OnActionAltCanceled;
    public EventHandler OnSwapItems;
    public EventHandler OnDropItem;
    public EventHandler OnUI_Up;
    public EventHandler OnUI_Down;
    public EventHandler OnUI_Left;
    public EventHandler OnUI_Right;
    public EventHandler OnUI_Back;

    [SerializeField]
    private bool IsUIOpened;

    [Header("Temp")]
    public bool analogMovement;

    [Header("Mouse Cursor Settings")]
    public bool cursorInputForLook = true;
    private bool cursorLocked = true;


    private void Awake()
    {
        playerGameInputActions = new PlayerGameInputActions();


        playerGameInputActions.Game.Jump.performed += Jump_performed;
        playerGameInputActions.Game.Sprint.performed += Sprint_performed;
        playerGameInputActions.Game.Sprint.canceled += Sprint_canceled;
        playerGameInputActions.Game.Interact.performed += Interact_performed;
        playerGameInputActions.Game.Action.performed += Action_performed;
        playerGameInputActions.Game.Action.canceled += Action_canceled;
        playerGameInputActions.Game.ActionAlt.performed += ActionAlt_performed;
        playerGameInputActions.Game.ActionAlt.canceled += ActionAlt_canceled;
        playerGameInputActions.Game.SwapItems.performed += SwapItems_performed;
        playerGameInputActions.Game.DropItem.performed += DropItem_performed;
        playerGameInputActions.UI.Up.performed += UI_Up_performed;
        playerGameInputActions.UI.Down.performed += UI_Down_performed;
        playerGameInputActions.UI.Left.performed += UI_Left_performed;
        playerGameInputActions.UI.Right.performed += UI_Right_performed;
        playerGameInputActions.UI.Back.performed += UI_Back_performed;
        playerGameInputActions.Game.Enable();
        playerGameInputActions.UI.Enable();

        IsUIOpened = false;

    }

    private void UI_Back_performed(InputAction.CallbackContext context)
    {
        //playerGameInputActions.UI.Disable();
        //playerGameInputActions.Game.Enable();
        //OnUI_Back?.Invoke(this, EventArgs.Empty);
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

    public void Start()
    {
        BaseFactory.OnInteractBaseFactory += BaseFactory_OnInteract;
        FactoryInteractionUI.OnAnyUIClosed += FactoryInteractionUI_OnInteract;
    }

    private void FactoryInteractionUI_OnInteract(object sender, EventArgs e)
    {
        IsUIOpened = false;
    }

    private void BaseFactory_OnInteract(object sender, EventArgs e)
    {
        //playerGameInputActions.Game.Disable();
        //playerGameInputActions.UI.Enable();
        IsUIOpened = true;
    }

    private void DropItem_performed(InputAction.CallbackContext context)
    {
        OnDropItem?.Invoke(this, EventArgs.Empty);
    }

    private void SwapItems_performed(InputAction.CallbackContext context)
    {
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
        OnActionAlt?.Invoke(this, EventArgs.Empty);
    }

    private void Action_performed(InputAction.CallbackContext context)
    {
        OnAction?.Invoke(this, EventArgs.Empty);
    }

    private void Interact_performed(InputAction.CallbackContext context)
    {
        OnInteract?.Invoke(this, EventArgs.Empty);
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
        OnSprint?.Invoke(this, new OnSprintArgs
        {
            IsSprinting = true
        });
    }

    private void Jump_performed(InputAction.CallbackContext context)
    {
        OnJump?.Invoke(this, EventArgs.Empty);
    }

    public Vector2 GetLookDeltaValue()
    {
        if (IsUIOpened)
            return Vector2.zero;

        if (!cursorInputForLook)
        {
            return Vector2.zero;
        }
        Vector2 inputVector = playerGameInputActions.Game.Look.ReadValue<Vector2>();
        return new Vector2(inputVector.x, -inputVector.y);
    }



    public Vector2 GetMoveVectorValue()
    {
        if (IsUIOpened)
            return Vector2.zero;
        return playerGameInputActions.Game.Move.ReadValue<Vector2>();
    }

    private void OnEnable()
    {
        playerGameInputActions.Game.Enable();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        SetCursorState(cursorLocked);
    }

    private void SetCursorState(bool newState)
    {
        Cursor.lockState = newState ? CursorLockMode.Locked : CursorLockMode.None;
    }
}
