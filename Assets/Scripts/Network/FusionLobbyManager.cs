using UnityEngine;
using Fusion;
using Fusion.Sockets;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

public class FusionLobbyManager : MonoBehaviour, INetworkRunnerCallbacks
{
    public static FusionLobbyManager Instance { get; private set; }
    
    [Header("Fusion Settings")]
    [SerializeField] private GameObject roomPlayerPrefab;

    // Public set to be read by UI
    public NetworkRunner Runner { get; private set; }
    public string LocalPlayerName { get; private set; } = "Player";
    public bool IsHost => Runner && Runner.IsServer;
    
    // Callback registered by LobbyUIController
    public Action OnConnectedToLobby;
    public Action OnDisconnectedFromLobby;
    public Action<List<SessionInfo>> OnSessionListUpdate;
    public Action<string> OnJoinedRoom;
    public Action OnLeftRoom;
    public Action<string> OnError;
    
    private readonly Dictionary<PlayerRef, RoomPlayer> _spawnedPlayers = new();
    private const string DefaultLobbyName = "DefaultLobby";

    private void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
    
    #region Public API
    /// <summary>
    /// Connect to Photon and enter the lobby (session browser)
    /// Must be called before creating or joining a room
    /// </summary>
    public async Task ConnectToLobbyAsync(string playerName)
    {
        LocalPlayerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName;
        Debug.Log($"[Lobby] Connecting as '{LocalPlayerName}'...");

        await RecreateRunnerAsync();
        
        var result = await Runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.AutoHostOrClient,
            SessionName = $"_lobby_{Guid.NewGuid():N}",
            CustomLobbyName = DefaultLobbyName,
            IsOpen = false,
            IsVisible = false,
            Scene = null,
            SceneManager = null,
        });

        if (result.Ok)
        {
            Debug.Log("[Lobby] Connected to Photon lobby, session list active.");
            OnConnectedToLobby?.Invoke();
        }
        else
        {
            Debug.LogError($"[Lobby] Connection failed: {result.ShutdownReason}");
            OnError?.Invoke($"Connection failed: {result.ShutdownReason}");
        }
    }
    
    /// <summary>
    /// Create a new room and become the host.
    /// Only valid after ConnectToLobbyAsync succeeds.
    /// </summary>
    public async Task CreateRoomAsync(string roomName, int maxPlayers)
    {
        Debug.Log($"[Lobby] Creating room '{roomName}' (max {maxPlayers})...");
        maxPlayers = Mathf.Clamp(maxPlayers, 2, 6);

        await RecreateRunnerAsync(); // ← add this

        var result = await Runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Host,
            SessionName = roomName,
            PlayerCount = maxPlayers,
            CustomLobbyName = DefaultLobbyName,
        });

        if (result.Ok)
        {
            Debug.Log($"[Lobby] Room '{roomName}' created. You are the host.");
            OnJoinedRoom?.Invoke(roomName);
        }
        else
        {
            Debug.LogError($"[Lobby] Create room failed: {result.ShutdownReason}");
            OnError?.Invoke($"Create room failed: {result.ShutdownReason}");
        }
    }

    /// <summary>
    /// Join an existing session by name.
    /// </summary>
    public async Task JoinRoomAsync(string roomName)
    {
        Debug.Log($"[Lobby] Joining room '{roomName}'...");

        await RecreateRunnerAsync(); // ← add this

        var result = await Runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Client,
            SessionName = roomName,
            CustomLobbyName = DefaultLobbyName,
        });

        if (result.Ok)
        {
            Debug.Log($"[Lobby] Joined room '{roomName}'.");
            OnJoinedRoom?.Invoke(roomName);
        }
        else
        {
            Debug.LogError($"[Lobby] Join failed: {result.ShutdownReason}");
            OnError?.Invoke($"Join failed: {result.ShutdownReason}");
        }
    }
    
    /// <summary>
    /// Leave the current session and return to lobby browser.
    /// </summary>
    public async Task LeaveRoomAsync()
    {
        Debug.Log("[Lobby] Leaving room...");
        // ConnectToLobbyAsync calls RecreateRunnerAsync internally, which shuts down the current runner before creating a new one.
        await ConnectToLobbyAsync(LocalPlayerName);
        OnLeftRoom?.Invoke();
    }

    /// <summary>
    /// Disconnect entirely from Photon.
    /// </summary>
    public async Task DisconnectAsync()
    {
        Debug.Log("[Lobby] Disconnecting...");
        if (Runner)
        {
            Runner.RemoveCallbacks(this);
            await Runner.Shutdown(destroyGameObject: true);
            Runner = null;
        }
        OnDisconnectedFromLobby?.Invoke();
        Debug.Log("[Lobby] Disconnected.");
    }

    /// <summary>
    /// Host-only: start the game session when all players are ready.
    /// </summary>
    public void StartGame()
    {
        if (!IsHost)
        {
            Debug.LogWarning("[Lobby] StartGame called by non-host — ignored.");
            return;
        }
        Debug.Log("[Lobby] Host is starting the game!");
        // TODO: Load game scene here, e.g.:
        // Runner.LoadScene(SceneRef.FromIndex(gameSceneBuildIndex));
    }
    #endregion
    
    #region INetworkRunnerCallbacks
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer)
        {
            var obj = runner.Spawn(
                roomPlayerPrefab,
                Vector3.zero,
                Quaternion.identity,
                player
            );
            
            _spawnedPlayers[player] = obj.GetComponent<RoomPlayer>();
            Debug.Log($"[Room] Player {player.PlayerId} joined. " +
                      $"Total: {runner.SessionInfo.PlayerCount}");
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_spawnedPlayers.TryGetValue(player, out var roomPlayer))
        {
                Debug.Log($"[Room] '{roomPlayer.PlayerName}' left the room.");
                
                if (runner.IsServer)
                    runner.Despawn(roomPlayer.Object);
                
                _spawnedPlayers.Remove(player);
        }
        
        LobbyUIController.Instance?.RefreshStartButton();
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        Debug.Log($"[Lobby] Session list updated: {sessionList.Count} session(s).");
        OnSessionListUpdate?.Invoke(sessionList);
    }
    
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
    {
        Debug.Log($"[Lobby] Runner shutdown: {shutdownReason}");
        _spawnedPlayers.Clear();
        if (shutdownReason != ShutdownReason.Ok)
            OnError?.Invoke($"Disconnected: {shutdownReason}");
    }

    public void OnConnectedToServer(NetworkRunner runner)
        => Debug.Log("[Lobby] OnConnectedToServer");

    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
    {
        Debug.LogWarning($"[Lobby] Disconnected from server: {reason}");
        OnDisconnectedFromLobby?.Invoke();
    }

    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason)
    {
        Debug.LogError($"[Lobby] Connect failed: {reason}");
        OnError?.Invoke($"Connect failed: {reason}");
    }

    // Unused but required by interface
    public void OnInput(NetworkRunner runner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessage message) { }
    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    #endregion

    private async Task RecreateRunnerAsync()
    {
        if (Runner)
        {
            Runner.RemoveCallbacks(this);
            await Runner.Shutdown(destroyGameObject: true);
            Runner = null;
        }
        
        var runnerObject = new GameObject("NetworkRunner");
        DontDestroyOnLoad(runnerObject);
        Runner = runnerObject.AddComponent<NetworkRunner>();
        Runner.ProvideInput = true;
        Runner.AddCallbacks(this);
    }
}