using UnityEngine;

public interface IDamageable
{
    public void DamageReceived(EquippableItemSO equippableItemSO, float damage);
}
