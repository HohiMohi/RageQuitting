using System.Collections.Generic;
using UnityEngine;

public interface ICarriedObjectImpactTargetProvider
{
    bool IsActivelyCarried { get; }
    void CollectActiveCarrierRoots(ICollection<GameObject> targets);
}
