using UnityEngine;
using Fusion;

public class RoomPlayer : NetworkBehaviour
{
    [Networked] public NetworkString<_32> PlayerName { get; set; }
    [Networked] public NetworkBool IsReady { get; set; }
    [Networked] public NetworkBool IsHost { get; set; }
    
    private NetworkString<_32> _prevName;
    private bool _prevReady;
    private bool _prevHost;
    private bool _initialised;

    public override void Spawned()
    {
        LobbyUIController.Instance?.RegisterPlayer(this);
        
        if (HasInputAuthority)
        {
            RPC_SetPlayerInfo(
                FusionLobbyManager.Instance.LocalPlayerName,
                Runner.IsServer
            );
            Debug.Log($"[RoomPlayer] Spawned locally, sent info to host.");
        }
        
        _prevName = PlayerName;
        _prevReady = IsReady;
        _prevHost = IsHost;
        _initialised = false;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        Debug.Log($"[RoomPlayer] '{PlayerName}' left the room.");
        LobbyUIController.Instance?.UnregisterPlayer(this);
    }
    
    public override void Render()
    {
        var dirty = !_initialised;

        if (PlayerName != _prevName)
        {
            Debug.Log($"[RoomPlayer] Name changed: '{_prevName}' → '{PlayerName}'");
            _prevName = PlayerName;
            dirty = true;
        }

        if (IsReady != _prevReady)
        {
            Debug.Log($"[RoomPlayer] '{PlayerName}' ready → {(bool)IsReady}");
            _prevReady = IsReady;
            dirty = true;
        }

        if (IsHost != _prevHost)
        {
            _prevHost = IsHost;
            dirty = true;
        }

        if (dirty)
        {
            _initialised = true;
            LobbyUIController.Instance?.RefreshPlayerList();
            LobbyUIController.Instance?.RefreshStartButton();
        }
    }
    
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetPlayerInfo(string playerName, bool isHost)
    {
        PlayerName = playerName;
        IsHost     = isHost;
        Debug.Log($"[RoomPlayer] Host received player info: '{playerName}', IsHost={isHost}");
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    public void RPC_SetReady(bool ready)
    {
        IsReady = ready;
    }
}