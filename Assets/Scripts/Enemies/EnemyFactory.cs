using UnityEngine;
using System.Collections.Generic;
using Photon.Pun;

public static class EnemyFactory
{
    private static Dictionary<string, EnemyData> enemyDataCache = new Dictionary<string, EnemyData>();
    
    /// <summary>
    /// Create an enemy AI component based on the enemy type
    /// </summary>
    public static BaseEnemyAI CreateEnemyAI(GameObject enemyObject, EnemyType enemyType)
    {
        BaseEnemyAI enemyAI = null;
        
        switch (enemyType)
        {
            case EnemyType.Aswang:
                enemyAI = enemyObject.AddComponent<AswangUnitAI>();
                break;
            case EnemyType.Tikbalang:
                enemyAI = enemyObject.AddComponent<TikbalangAI>();
                break;
            case EnemyType.Bakunawa:
                enemyAI = enemyObject.AddComponent<BakunawaAI>();
                break;
            case EnemyType.BadDiwataPunso:
                enemyAI = enemyObject.AddComponent<BadDiwataPunsoAI>();
                break;
            case EnemyType.Minokawa:
                enemyAI = enemyObject.AddComponent<MinokawaAI>();
                break;
            case EnemyType.Berberoka:
                enemyAI = enemyObject.AddComponent<BerberokaAI>();
                break;
            case EnemyType.Amomongo:
                enemyAI = enemyObject.AddComponent<AmomongoAI>();
                break;
            case EnemyType.Tiyanak:
                enemyAI = enemyObject.AddComponent<TiyanakAI>();
                break;
            case EnemyType.AswangQueen:
                enemyAI = enemyObject.AddComponent<AswangQueenAI>();
                break;
            case EnemyType.Wakwak:
                enemyAI = enemyObject.AddComponent<WakwakAI>();
                break;
            case EnemyType.Sarimanok:
                enemyAI = enemyObject.AddComponent<SarimanokAI>();
                break;
            case EnemyType.Pugot:
                enemyAI = enemyObject.AddComponent<PugotAI>();
                break;
            case EnemyType.Sirena:
                enemyAI = enemyObject.AddComponent<SirenaAI>();
                break;
            case EnemyType.Sigbin:
                enemyAI = enemyObject.AddComponent<SigbinAI>();
                break;
            case EnemyType.Manananggal:
                enemyAI = enemyObject.AddComponent<ManananggalAI>();
                break;
            case EnemyType.Kapre:
                enemyAI = enemyObject.AddComponent<KapreAI>();
                break;
            case EnemyType.Busaw:
                enemyAI = enemyObject.AddComponent<BusawAI>();
                break;
            case EnemyType.Bungisngis:
                enemyAI = enemyObject.AddComponent<BungisngisAI>();
                break;
            case EnemyType.ShadowTouchedDiwata:
                enemyAI = enemyObject.AddComponent<ShadowTouchedDiwataAI>();
                break;
            default:
                Debug.LogError($"[EnemyFactory] Unknown enemy type: {enemyType}");
                break;
        }
        
        return enemyAI;
    }
    
    /// <summary>
    /// Get enemy data for a specific enemy type
    /// </summary>
    public static EnemyData GetEnemyData(EnemyType enemyType)
    {
        string dataPath = GetEnemyDataPath(enemyType);
        
        if (enemyDataCache.ContainsKey(dataPath))
        {
            return enemyDataCache[dataPath];
        }
        
        EnemyData data = Resources.Load<EnemyData>(dataPath);
        if (data != null)
        {
            enemyDataCache[dataPath] = data;
        }
        else
        {
            Debug.LogWarning($"[EnemyFactory] Enemy data not found at path: {dataPath}");
        }
        
        return data;
    }
    
    /// <summary>
    /// Create a complete enemy prefab with all necessary components
    /// </summary>
    public static GameObject CreateEnemyPrefab(EnemyType enemyType, Vector3 position, Quaternion rotation)
    {
        // Create base GameObject
        GameObject enemyObject = new GameObject(GetEnemyName(enemyType));
        enemyObject.transform.position = position;
        enemyObject.transform.rotation = rotation;
        
        // Add required components
        CharacterController controller = enemyObject.AddComponent<CharacterController>();
        Animator animator = enemyObject.AddComponent<Animator>();
        PhotonView photonView = enemyObject.GetComponent<PhotonView>();
        if (photonView == null)
            photonView = enemyObject.AddComponent<PhotonView>();
        
        // Create enemy AI
        BaseEnemyAI enemyAI = CreateEnemyAI(enemyObject, enemyType);
        
        // Load and assign enemy data
        EnemyData enemyData = GetEnemyData(enemyType);
        if (enemyData != null)
        {
            enemyAI.enemyData = enemyData;
        }
        
        // Configure components
        controller.radius = 0.5f;
        controller.height = 2f;
        controller.center = new Vector3(0, 1f, 0);
        
        return enemyObject;
    }
    
    /// <summary>
    /// Get the resource path for enemy data
    /// </summary>
    private static string GetEnemyDataPath(EnemyType enemyType)
    {
        return $"EnemyData/{enemyType}Data";
    }
    
    /// <summary>
    /// Get the display name for an enemy type
    /// </summary>
    private static string GetEnemyName(EnemyType enemyType)
    {
        switch (enemyType)
        {
            case EnemyType.Aswang: return "Aswang";
            case EnemyType.Tikbalang: return "Tikbalang";
            case EnemyType.Bakunawa: return "Bakunawa";
            case EnemyType.BadDiwataPunso: return "Bad Diwata-Punso";
            case EnemyType.Minokawa: return "Minokawa";
            case EnemyType.Berberoka: return "Berberoka";
            case EnemyType.Amomongo: return "Amomongo";
            case EnemyType.Tiyanak: return "Tiyanak";
            case EnemyType.AswangQueen: return "Aswang Queen";
            case EnemyType.Wakwak: return "Wakwak";
            case EnemyType.Sarimanok: return "Sarimanok";
            case EnemyType.Pugot: return "Pugot";
            case EnemyType.Sirena: return "Sirena";
            case EnemyType.Sigbin: return "Sigbin";
            case EnemyType.Manananggal: return "Manananggal";
            case EnemyType.Kapre: return "Kapre";
            case EnemyType.Busaw: return "Busaw";
            case EnemyType.Bungisngis: return "Bungisngis";
            case EnemyType.ShadowTouchedDiwata: return "Shadow Touched Diwata";
            default: return "Unknown Enemy";
        }
    }
    
    /// <summary>
    /// Clear the enemy data cache
    /// </summary>
    public static void ClearCache()
    {
        enemyDataCache.Clear();
    }
}

public enum EnemyType
{
    Aswang,
    Tikbalang,
    Bakunawa,
    BadDiwataPunso,
    Minokawa,
    Berberoka,
    Amomongo,
    Tiyanak,
    AswangQueen,
    Wakwak,
    Sarimanok,
    Pugot,
    Sirena,
    Sigbin,
    Manananggal,
    Kapre,
    Busaw,
    Bungisngis,
    ShadowTouchedDiwata
}
