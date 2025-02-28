using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Scriptable Objects/BuildingElements/BuildingElementSO")]
public class BuildingElementSO : ScriptableObject
{
    public string elementName;
    public BridgeElementType elementType;
    public int maxConnectorJoints;
    public int maxSpanJoints;
    public int maxSupportJoints;
    public int maxGroundMountJoints;
    public int maxSideMountJoints;
    public GameObject buildingElementPrefab;
    public List<ConstructionMaterialSO> constructionMaterialList;

    #region Validation
#if UNITY_EDITOR
    private void OnValidate()
    {
        HelperUtilities.ValidateCheckEmptyString(this, nameof(elementName), elementName);
        HelperUtilities.ValidateCheckPositiveValue(this, nameof(maxConnectorJoints), maxConnectorJoints, true);
        HelperUtilities.ValidateCheckPositiveValue(this, nameof(maxSpanJoints), maxSpanJoints, true);
        HelperUtilities.ValidateCheckPositiveValue(this, nameof(maxSupportJoints), maxSupportJoints, true);
        HelperUtilities.ValidateCheckPositiveValue(this, nameof(maxGroundMountJoints), maxGroundMountJoints, true);
        HelperUtilities.ValidateCheckPositiveValue(this, nameof(maxSideMountJoints), maxSideMountJoints, true);
        HelperUtilities.ValidateCheckNullValue(this, nameof(buildingElementPrefab), buildingElementPrefab);

    }


#endif
    #endregion
}
