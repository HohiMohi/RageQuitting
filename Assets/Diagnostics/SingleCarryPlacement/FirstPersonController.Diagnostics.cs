#if UNITY_EDITOR && RQ_SINGLE_CARRY_DIAGNOSTICS
using UnityEngine;

namespace StarterAssets
{
	public partial class FirstPersonController
	{
		partial void RecordSingleCarryPlacementMovementDiagnostic()
		{
			if (!SingleCarryStateDivergenceTrace.IsRecording)
				return;

			Vector2 movementInput = _playerInputNew != null ? _playerInputNew.GetMoveVectorValue() : Vector2.zero;
			bool placementActive = _playerInputNew != null && _playerInputNew.IsSingleCarryPlacementActive;
			bool movementBlocked = _playerInputNew != null && _playerInputNew.IsSingleCarryPlacementMovementBlocked;
			SingleCarryStateDivergenceTrace.RecordMovementForGameObject(gameObject, transform.position,
				transform.rotation, _horizontalVelocity, movementInput, _playerInputNew != null,
				placementActive, movementBlocked);
		}
	}
}
#endif
