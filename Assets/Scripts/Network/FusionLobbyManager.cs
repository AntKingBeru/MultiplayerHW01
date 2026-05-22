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

        if (Runner)
        {
            Runner.RemoveCallbacks(this);
            await Runner.Shutdown(destroyGameObject: true);
            Runner = null;
        }
        
        var runnerGo = new GameObject("NetworkRunner");
        DontDestroyOnLoad(runnerGo);
        Runner = runnerGo.AddComponent<NetworkRunner>();
        Runner.ProvideInput = true;
        Runner.AddCallbacks(this);

        var result = await Runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = null,
            CustomLobbyName = DefaultLobbyName,
            Scene = null,
            SceneManager = null,
        });
        
        if (result.Ok)
        {
            Debug.Log("[Lobby] Connected. Listening for sessions...");
            OnConnectedToLobby?.Invoke();
        }
        else
        {
            Debug.LogError($"[Lobby] Connect failed: {result.ShutdownReason}");
            OnError?.Invoke($"Connect failed: {result.ShutdownReason}");
        }
    }
    
    /// <summary>
    /// Create a new room and become the host.
    /// Only valid after ConnectToLobbyAsync succeeds.
    /// </summary>
    public async Task CreateRoomAsync(string roomName, int maxPlayers)
    {
        maxPlayers = Mathf.Clamp(maxPlayers, 2, 6);

        if (Runner)
        {
            Runner.RemoveCallbacks(this);
            await Runner.Shutdown(destroyGameObject: true);
            Runner = null;
        }

        var runnerGo = new GameObject("NetworkRunner");
        DontDestroyOnLoad(runnerGo);
        Runner = runnerGo.AddComponent<NetworkRunner>();
        Runner.ProvideInput = true;
        Runner.AddCallbacks(this);

        var result = await Runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = roomName,
            PlayerCount = maxPlayers,
            CustomLobbyName = DefaultLobbyName,
            Scene  = null,
            SceneManager = null,
        });

        if (result.Ok)
        {
            Debug.Log($"[Lobby] Room '{roomName}' created. MasterClient={Runner.IsSharedModeMasterClient}");
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

        if (Runner)
        {
            Runner.RemoveCallbacks(this);
            await Runner.Shutdown(destroyGameObject: true);
            Runner = null;
        }

        var runnerGo = new GameObject("NetworkRunner");
        DontDestroyOnLoad(runnerGo);
        Runner = runnerGo.AddComponent<NetworkRunner>();
        Runner.ProvideInput = true;
        Runner.AddCallbacks(this);

        var result = await Runner.StartGame(new StartGameArgs
        {
            GameMode = GameMode.Shared,
            SessionName = roomName,
            CustomLobbyName = DefaultLobbyName,
            Scene  = null,
            SceneManager = null,
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
        // TODO: Runner.LoadScene(SceneRef.FromIndex(gameSceneBuildIndex));
    }
    #endregion
    
    #region INetworkRunnerCallbacks
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (player == runner.LocalPlayer)
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
        else
        {
            Debug.Log($"[Room] Remote player {player.PlayerId} joined. " +
                      $"Total: {runner.SessionInfo.PlayerCount}");
        }
    }

    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        if (_spawnedPlayers.TryGetValue(player, out var roomPlayer))
        {
                Debug.Log($"[Room] '{roomPlayer.PlayerName}' left the room.");
                
                if (roomPlayer.HasStateAuthority)
                    runner.Despawn(roomPlayer.Object);
                
                _spawnedPlayers.Remove(player);
        }
        
        LobbyUIController.Instance?.RefreshStartButton();
    }

    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList)
    {
        Debug.Log($"[Lobby] RAW session list count: {sessionList.Count}");
        foreach (var s in sessionList)
            Debug.Log($"[Lobby] Session: '{s.Name}' | Open={s.IsOpen} | Visible={s.IsVisible} | Players={s.PlayerCount}/{s.MaxPlayers}");
        
        var visibleSessions = sessionList.FindAll(s => s.IsVisible && s.IsOpen);
        Debug.Log($"[Lobby] Session list updated: {visibleSessions.Count} session(s).");
        OnSessionListUpdate?.Invoke(visibleSessions);
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
}