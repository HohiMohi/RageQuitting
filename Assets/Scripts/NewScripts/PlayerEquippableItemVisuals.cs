using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerEquippableItemVisuals : NetworkBehaviour
{
    [Serializable]
    private struct AttachmentPose
    {
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 localScale;
    }

    [Serializable]
    private struct ToolAttachmentOverride
    {
        public EquippableItemType itemType;
        public AttachmentPose handPose;
        public AttachmentPose backPose;
    }

    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private PlayerGoblinVisualSetup goblinVisualSetup;
    [SerializeField] private EquippableToolVisualBuilder.ToolVisualMaterials toolVisualMaterials;
    [SerializeField] private string rightHandBoneName = "R_Hand";
    [SerializeField] private string backBoneName = "Spine02";
    [SerializeField] private string backBoneFallbackName = "Spine01";
    [SerializeField] private AttachmentPose defaultHandPose = new AttachmentPose
    {
        localPosition = new Vector3(0.02f, 0.02f, 0.08f),
        localEulerAngles = new Vector3(90f, 20f, 0f),
        localScale = new Vector3(0.55f, 0.55f, 0.55f)
    };
    [SerializeField] private AttachmentPose defaultBackPose = new AttachmentPose
    {
        localPosition = new Vector3(0f, 0.18f, -0.08f),
        localEulerAngles = new Vector3(18f, 0f, 14f),
        localScale = new Vector3(0.65f, 0.65f, 0.65f)
    };
    [SerializeField] private Vector3 handToolLocalEulerAngles = new Vector3(0f, 90f, 0f);
    [SerializeField] private Vector3 backToolLocalEulerAngles = Vector3.zero;
    [SerializeField] private ToolAttachmentOverride[] attachmentOverrides;

    private Transform rightHandBone;
    private Transform backBone;
    private GameObject handVisual;
    private GameObject backVisual;
    private int currentHandItemTypeValue = -2;
    private int currentBackItemTypeValue = -2;

    private void Awake()
    {
        if (playerInventory == null)
        {
            playerInventory = GetComponent<PlayerInventory>();
        }

        if (goblinVisualSetup == null)
        {
            goblinVisualSetup = GetComponent<PlayerGoblinVisualSetup>();
        }
    }

    private void OnEnable()
    {
        SubscribeInventory();
        RefreshVisuals();
    }

    private void Start()
    {
        ResolveBones();
        RefreshVisuals();
    }

    public override void OnNetworkSpawn()
    {
        SubscribeInventory();
        RefreshVisuals();
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeInventory();
    }

    private void OnDisable()
    {
        UnsubscribeInventory();
    }

    private void OnDestroy()
    {
        ClearVisual(ref handVisual);
        ClearVisual(ref backVisual);
    }

    private void Update()
    {
        if ((rightHandBone == null || backBone == null) && ResolveBones())
        {
            RefreshVisuals();
        }
    }

    private void SubscribeInventory()
    {
        if (playerInventory == null)
        {
            return;
        }

        playerInventory.OnInventorySlotsChanged -= PlayerInventory_OnInventorySlotsChanged;
        playerInventory.OnInventorySlotsChanged += PlayerInventory_OnInventorySlotsChanged;
    }

    private void UnsubscribeInventory()
    {
        if (playerInventory == null)
        {
            return;
        }

        playerInventory.OnInventorySlotsChanged -= PlayerInventory_OnInventorySlotsChanged;
    }

    private void PlayerInventory_OnInventorySlotsChanged(object sender, EventArgs e)
    {
        RefreshVisuals();
    }

    private void RefreshVisuals()
    {
        if (playerInventory == null || !ResolveBones())
        {
            return;
        }

        int handItemTypeValue = GetSlotItemTypeValue(0);
        int backItemTypeValue = GetSlotItemTypeValue(1);

        if (handItemTypeValue != currentHandItemTypeValue)
        {
            currentHandItemTypeValue = handItemTypeValue;
            RebuildSlotVisual(ref handVisual, rightHandBone, handItemTypeValue, true);
        }

        if (backItemTypeValue != currentBackItemTypeValue)
        {
            currentBackItemTypeValue = backItemTypeValue;
            RebuildSlotVisual(ref backVisual, backBone, backItemTypeValue, false);
        }
    }

    private int GetSlotItemTypeValue(int slotIndex)
    {
        if (playerInventory.IsNetworkStateActive() && !IsOwner)
        {
            return playerInventory.GetNetworkSlotItemTypeValue(slotIndex);
        }

        EquippableItemSO item = playerInventory.GetItemInSlot(slotIndex);
        return item != null ? (int)item.itemType : -1;
    }

    private void RebuildSlotVisual(ref GameObject slotVisual, Transform parentBone, int itemTypeValue, bool isHandSlot)
    {
        ClearVisual(ref slotVisual);

        if (parentBone == null || itemTypeValue < 0)
        {
            return;
        }

        EquippableItemType itemType = (EquippableItemType)itemTypeValue;
        if (!IsSupportedToolType(itemType))
        {
            return;
        }

        slotVisual = new GameObject(isHandSlot ? "HeldEquippableItemVisual" : "BackEquippableItemVisual");
        slotVisual.transform.SetParent(parentBone, false);

        AttachmentPose pose = GetPose(itemType, isHandSlot);
        slotVisual.transform.localPosition = pose.localPosition;
        slotVisual.transform.localRotation = Quaternion.Euler(pose.localEulerAngles);
        slotVisual.transform.localScale = pose.localScale;

        GameObject generatedToolVisual = EquippableToolVisualBuilder.BuildVisual(itemType, slotVisual.transform, toolVisualMaterials);
        if (generatedToolVisual != null)
        {
            generatedToolVisual.transform.localRotation = Quaternion.Euler(isHandSlot ? handToolLocalEulerAngles : backToolLocalEulerAngles);
        }
    }

    private AttachmentPose GetPose(EquippableItemType itemType, bool isHandSlot)
    {
        if (attachmentOverrides != null)
        {
            foreach (ToolAttachmentOverride attachmentOverride in attachmentOverrides)
            {
                if (attachmentOverride.itemType == itemType)
                {
                    return isHandSlot ? attachmentOverride.handPose : attachmentOverride.backPose;
                }
            }
        }

        return isHandSlot ? defaultHandPose : defaultBackPose;
    }

    private bool ResolveBones()
    {
        GameObject visualRoot = goblinVisualSetup != null ? goblinVisualSetup.SpawnedVisual : null;
        if (visualRoot == null)
        {
            return false;
        }

        if (rightHandBone == null)
        {
            rightHandBone = FindDeepChild(visualRoot.transform, rightHandBoneName);
        }

        if (backBone == null)
        {
            backBone = FindDeepChild(visualRoot.transform, backBoneName);
            if (backBone == null)
            {
                backBone = FindDeepChild(visualRoot.transform, backBoneFallbackName);
            }

            if (backBone == null)
            {
                backBone = visualRoot.transform;
            }
        }

        return rightHandBone != null && backBone != null;
    }

    private static Transform FindDeepChild(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
        {
            return null;
        }

        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName)
            {
                return child;
            }
        }

        return null;
    }

    private static bool IsSupportedToolType(EquippableItemType itemType)
    {
        return itemType == EquippableItemType.Axe ||
               itemType == EquippableItemType.Pickaxe ||
               itemType == EquippableItemType.Shovel ||
               itemType == EquippableItemType.IndustrialHammer;
    }

    private static void ClearVisual(ref GameObject visual)
    {
        if (visual == null)
        {
            return;
        }

        Destroy(visual);
        visual = null;
    }
}
