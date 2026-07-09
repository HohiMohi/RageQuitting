using UnityEngine;

public class PlayerSpawnPoint : MonoBehaviour
{
    [SerializeField] private int spawnIndex;

    public int SpawnIndex => spawnIndex;
}
