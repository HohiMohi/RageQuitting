using UnityEngine;

public interface IHeldObjectHudInfoProvider
{
    string HeldObjectDisplayName { get; }
    Sprite HeldObjectIcon { get; }
}
