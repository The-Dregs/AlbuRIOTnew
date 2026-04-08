using UnityEngine;

public static class PlayerSpawnManager
{
    public static Vector3? nextSpawnPosition = null;
    /// <summary>Set to true when the centralized spawn coordinator has completed teleport for the current load/transition flow.</summary>
    public static bool hasTeleportedByLoader = false;
}
