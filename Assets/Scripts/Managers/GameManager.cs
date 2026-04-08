using UnityEngine;
using Photon.Pun;
using System;
using System.Collections.Generic;

public class GameManager : MonoBehaviourPun
{
    [Header("Game Configuration")]
    public GameState currentGameState = GameState.Menu;
    public float gameTime = 0f;
    public int score = 0;
    
    [Header("Managers")]
    public NetworkManager networkManager;
    public QuestManager questManager;
    public ShrineManager shrineManager;
    public MovesetManager movesetManager;
    public ItemManager itemManager;
    
    [Header("Player Management")]
    public GameObject localPlayer;
    public List<GameObject> allPlayers = new List<GameObject>();

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogging = false;
    [SerializeField] private bool enableDebugHotkeys = false;
    
    [Header("Game Events")]
    public System.Action<GameState> OnGameStateChanged;
    public System.Action<float> OnGameTimeUpdated;
    public System.Action<int> OnScoreUpdated;
    public System.Action OnGameWon;
    public System.Action OnGameLost;
    
    // Singleton pattern
    public static GameManager Instance { get; private set; }
    
    // Game state management
    private GameState previousGameState;
    private bool isGameActive = false;
    private bool _isApplyingNetworkState = false;
    
    void Awake()
    {
        // Singleton setup
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
        
        // Auto-find managers
        if (networkManager == null)
            networkManager = FindFirstObjectByType<NetworkManager>();
        if (questManager == null)
            questManager = FindFirstObjectByType<QuestManager>();
        if (shrineManager == null)
            shrineManager = FindFirstObjectByType<ShrineManager>();
        if (movesetManager == null)
            movesetManager = FindFirstObjectByType<MovesetManager>();
        if (itemManager == null)
            itemManager = FindFirstObjectByType<ItemManager>();
    }
    
    void Start()
    {
        // Subscribe to network events
        if (networkManager != null)
        {
            networkManager.OnGameStarted += OnGameStarted;
            networkManager.OnGamePaused += OnGamePaused;
            networkManager.OnGameResumed += OnGameResumed;
            networkManager.OnPlayerJoined += OnPlayerJoined;
            networkManager.OnPlayerLeft += OnPlayerLeft;
        }
        
        // Subscribe to quest events
        if (questManager != null)
        {
            questManager.OnQuestCompleted += OnQuestCompleted;
            questManager.OnObjectiveCompleted += OnObjectiveCompleted;
        }
        
        // Initialize game
        InitializeGame();
    }
    
    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        
        // Unsubscribe from network events
        if (networkManager != null)
        {
            networkManager.OnGameStarted -= OnGameStarted;
            networkManager.OnGamePaused -= OnGamePaused;
            networkManager.OnGameResumed -= OnGameResumed;
            networkManager.OnPlayerJoined -= OnPlayerJoined;
            networkManager.OnPlayerLeft -= OnPlayerLeft;
        }
        
        // Unsubscribe from quest events
        if (questManager != null)
        {
            questManager.OnQuestCompleted -= OnQuestCompleted;
            questManager.OnObjectiveCompleted -= OnObjectiveCompleted;
        }

        StopAllCoroutines();
        OnGameStateChanged = null;
        OnGameTimeUpdated = null;
        OnScoreUpdated = null;
        OnGameWon = null;
        OnGameLost = null;
        allPlayers?.Clear();
    }
    
    void Update()
    {
        if (isGameActive && currentGameState == GameState.Playing)
        {
            gameTime += Time.deltaTime;
            OnGameTimeUpdated?.Invoke(gameTime);
        }
        
        HandleInput();
    }
    
    private void InitializeGame()
    {
        // Set initial game state
        SetGameState(GameState.Menu);
        
        // Initialize managers
        InitializeManagers();
    }
    
    private void InitializeManagers()
    {
        // Initialize item manager
        if (itemManager != null)
        {
            itemManager.RefreshItemLookup();
        }
        
        // Spawn initial items
        SpawnInitialItems();
    }
    
    private void SpawnInitialItems()
    {
        if (itemManager == null) return;
        
        // Spawn quest items
        itemManager.SpawnQuestItems();
        
        // Spawn offering items
        itemManager.SpawnOfferingItems();
    }
    
    private void HandleInput()
    {
        if (!enableDebugHotkeys) return;

        // Handle debug commands
        if (Input.GetKeyDown(KeyCode.F1))
        {
            DebugGameState();
        }
        
        if (Input.GetKeyDown(KeyCode.F2))
        {
            SpawnRandomItem();
        }
        
        if (Input.GetKeyDown(KeyCode.F3))
        {
            CompleteCurrentQuest();
        }
    }
    
    public void SetGameState(GameState newState)
    {
        if (currentGameState == newState) return;
        
        previousGameState = currentGameState;
        currentGameState = newState;
        
        if (enableDebugLogging) Debug.Log($"Game state changed from {previousGameState} to {currentGameState}");
        
        // Handle state transitions
        HandleGameStateTransition(previousGameState, currentGameState);
        
        OnGameStateChanged?.Invoke(currentGameState);
        
        // Sync with other players (buffered) only when not applying network state
        if (PhotonNetwork.IsMasterClient && !_isApplyingNetworkState)
        {
            photonView.RPC("RPC_SetGameState", RpcTarget.AllBuffered, (int)newState);
        }
    }
    
    [PunRPC]
    public void RPC_SetGameState(int newState)
    {
        _isApplyingNetworkState = true;
        SetGameState((GameState)newState);
        _isApplyingNetworkState = false;
    }
    
    private void HandleGameStateTransition(GameState from, GameState to)
    {
        switch (to)
        {
            case GameState.Menu:
                isGameActive = false;
                break;
                
            case GameState.Playing:
                isGameActive = true;
                break;
                
            case GameState.Paused:
                isGameActive = false;
                break;
                
            case GameState.GameOver:
                isGameActive = false;
                break;
        }
    }
    
    public void StartGame()
    {
        if (networkManager != null && networkManager.IsMasterClient())
        {
            networkManager.StartGame();
            SetGameState(GameState.Playing);
        }
    }
    
    public void PauseGame()
    {
        if (networkManager != null && networkManager.IsMasterClient())
        {
            networkManager.PauseGame();
            SetGameState(GameState.Paused);
        }
    }
    
    public void ResumeGame()
    {
        if (networkManager != null && networkManager.IsMasterClient())
        {
            networkManager.ResumeGame();
            SetGameState(GameState.Playing);
        }
    }
    
    public void EndGame(bool won)
    {
        SetGameState(GameState.GameOver);
        
        if (won)
        {
            OnGameWon?.Invoke();
        }
        else
        {
            OnGameLost?.Invoke();
        }
    }
    
    public void AddScore(int points)
    {
        score += points;
        OnScoreUpdated?.Invoke(score);
        
        // Sync with other players (buffered)
        if (PhotonNetwork.IsMasterClient)
        {
            photonView.RPC("RPC_AddScore", RpcTarget.AllBuffered, points);
        }
    }
    
    [PunRPC]
    public void RPC_AddScore(int points)
    {
        score += points;
        OnScoreUpdated?.Invoke(score);
    }
    
    private void OnGameStarted()
    {
        SetGameState(GameState.Playing);
    }
    
    private void OnGamePaused()
    {
        SetGameState(GameState.Paused);
    }
    
    private void OnGameResumed()
    {
        SetGameState(GameState.Playing);
    }
    
    private void OnPlayerJoined(Photon.Realtime.Player player)
    {
        if (enableDebugLogging) Debug.Log($"Player {player.NickName} joined the game");
        
        // Sync game state (buffered so new player gets state automatically)
        if (networkManager != null && networkManager.IsMasterClient())
        {
            photonView.RPC("RPC_SyncGameState", RpcTarget.AllBuffered, (int)currentGameState, score, gameTime);
        }
    }
    
    private void OnPlayerLeft(Photon.Realtime.Player player)
    {
        if (enableDebugLogging) Debug.Log($"Player {player.NickName} left the game");
    }
    
    [PunRPC]
    public void RPC_SyncGameState(int gameState, int currentScore, float currentGameTime)
    {
        SetGameState((GameState)gameState);
        score = currentScore;
        gameTime = currentGameTime;
    }
    
    private void OnQuestCompleted(Quest quest)
    {
        if (enableDebugLogging) Debug.Log($"Quest completed: {quest.questName}");
        
        // Add score for quest completion
        AddScore(100);
        
        // Check win condition
        CheckWinCondition();
    }
    
    private void OnObjectiveCompleted(QuestObjective objective)
    {
        if (enableDebugLogging) Debug.Log($"Objective completed: {objective.objectiveName}");
        
        // Add score for objective completion
        AddScore(25);
    }
    
    private void CheckWinCondition()
    {
        if (questManager != null)
        {
            // Check if all quests are completed
            bool allQuestsCompleted = true;
            foreach (var quest in questManager.quests)
            {
                if (!quest.isCompleted)
                {
                    allQuestsCompleted = false;
                    break;
                }
            }
            
            if (allQuestsCompleted)
            {
                EndGame(true);
            }
        }
    }
    
    // Debug methods
    private void DebugGameState()
    {
        if (!enableDebugLogging) return;
        Debug.Log($"=== Game State Debug ===");
        Debug.Log($"Current State: {currentGameState}");
        Debug.Log($"Game Time: {gameTime:F2}s");
        Debug.Log($"Score: {score}");
        Debug.Log($"Players: {allPlayers.Count}");
        Debug.Log($"Network Connected: {networkManager?.IsConnected()}");
        Debug.Log($"In Room: {networkManager?.IsInRoom()}");
        Debug.Log($"Master Client: {networkManager?.IsMasterClient()}");
    }
    
    private void SpawnRandomItem()
    {
        if (itemManager != null)
        {
            Vector3 randomPosition = new Vector3(
                UnityEngine.Random.Range(-10f, 10f),
                1f,
                UnityEngine.Random.Range(-10f, 10f)
            );
            itemManager.SpawnRandomItem(randomPosition);
        }
    }
    
    private void CompleteCurrentQuest()
    {
        if (questManager != null)
        {
            questManager.CompleteQuest(questManager.currentQuestIndex);
        }
    }
    
    // Public getters
    public GameState CurrentGameState => currentGameState;
    public float GameTime => gameTime;
    public int Score => score;
    public bool IsGameActive => isGameActive;
    public GameObject LocalPlayer => localPlayer;
    public List<GameObject> AllPlayers => allPlayers;
}

public enum GameState
{
    Menu,
    Playing,
    Paused,
    GameOver
}

