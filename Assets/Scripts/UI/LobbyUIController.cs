using UnityEngine;
using UnityEngine.UI;
using Fusion;
using System.Linq;
using System.Collections.Generic;
using TMPro;

public class LobbyUIController : MonoBehaviour
{
    public static LobbyUIController Instance { get; private set; }
    
    [Header("Panels")]
    [SerializeField] private GameObject mainMenuPanel;
    [SerializeField] private GameObject lobbyPanel;
    [SerializeField] private GameObject roomPanel;
    
    [Header("Main Menu")]
    [SerializeField] private TMP_InputField playerNameInput;
    [SerializeField] private Button connectButton;
    [SerializeField] private TextMeshProUGUI mainStatusText;
    
    [Header("Lobby")]
    // Scrollview Content
    [SerializeField] private Transform roomListContent;
    [SerializeField] private GameObject roomListItemPrefab;
    [SerializeField] private TMP_InputField roomNameInput;
    [SerializeField] private Slider maxPlayersSlider;
    [SerializeField] private TextMeshProUGUI maxPlayersLabel;
    [SerializeField] private Button createRoomButton;
    [SerializeField] private TMP_InputField joinByNameInput;
    [SerializeField] private Button joinByNameButton;
    [SerializeField] private Button disconnectButton;
    
    [Header("Room")]
    [SerializeField] private TextMeshProUGUI roomTitleText;
    // Scrollview Content
    [SerializeField] private Transform playerListContent;
    [SerializeField] private GameObject playerListItemPrefab;
    [SerializeField] private Button readyButton;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button leaveRoomButton;

    private readonly List<RoomPlayer> _activePlayers = new();
    private bool _isReady;
    
    private const int MinPlayers = 2;
    private const int MaxPlayers = 6;

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

    private void Start()
    {
        // Wire up slider label
        maxPlayersSlider.minValue = MinPlayers;
        maxPlayersSlider.maxValue = MaxPlayers;
        maxPlayersSlider.wholeNumbers = true;
        maxPlayersSlider.onValueChanged.AddListener(
            v => maxPlayersLabel.text = $"Max Players: {(int)v}"
        );
        maxPlayersSlider.value = (MinPlayers +  MaxPlayers) / 2f;
        
        // Subscribe to FusionLobbyManager events
        var mgr = FusionLobbyManager.Instance;
        mgr.OnConnectedToLobby += HandleConnectedToLobby;
        mgr.OnDisconnectedFromLobby += HandleDisconnectedFromLobby;
        mgr.OnSessionListUpdate += HandleSessionListUpdated;
        mgr.OnJoinedRoom += HandleJoinedRoom;
        mgr.OnLeftRoom += HandleLeftRoom;
        mgr.OnError += HandleError;
        
        // Wire Buttons
        connectButton.onClick.AddListener(OnConnectClicked);
        createRoomButton.onClick.AddListener(OnCreateRoomClicked);
        joinByNameButton.onClick.AddListener(OnJoinByNameClicked);
        disconnectButton.onClick.AddListener(OnDisconnectClicked);
        readyButton.onClick.AddListener(OnReadyClicked);
        startGameButton.onClick.AddListener(OnStartGameClicked);
        leaveRoomButton.onClick.AddListener(OnLeaveRoomClicked);
        
        ShowPanel(mainMenuPanel);
        SetMainMenuButtons(true);
    }

    private void OnDestroy()
    {
        if (!FusionLobbyManager.Instance)
            return;
        
        var mgr = FusionLobbyManager.Instance;
        mgr.OnConnectedToLobby -= HandleConnectedToLobby;
        mgr.OnDisconnectedFromLobby -= HandleDisconnectedFromLobby;
        mgr.OnSessionListUpdate -= HandleSessionListUpdated;
        mgr.OnJoinedRoom -= HandleJoinedRoom;
        mgr.OnLeftRoom -= HandleLeftRoom;
        mgr.OnError -= HandleError;
    }
    
    #region Player List Management
    /// <summary>
    /// Called by RoomPlayer.Spawned() on every client
    /// </summary>
    public void RegisterPlayer(RoomPlayer player)
    {
        if (!_activePlayers.Contains(player))
            _activePlayers.Add(player);
        
        RefreshPlayerList();
    }

    /// <summary>
    /// Called by RoomPlayer.Despawned() on every client
    /// </summary>
    public void UnregisterPlayer(RoomPlayer player)
    {
        _activePlayers.Remove(player);
        RefreshPlayerList();
    }

    /// <summary>
    /// Rebuild the player list UI from current _activePlayers
    /// </summary>
    public void RefreshPlayerList()
    {
        // Destroy old rows
        foreach (Transform child in playerListContent)
            Destroy(child.gameObject);

        foreach (var rp in _activePlayers)
        {
            var item = Instantiate(playerListItemPrefab, playerListContent);
            item.GetComponent<PlayerListItem>().Populate(rp);
        }
        
        LayoutRebuilder.ForceRebuildLayoutImmediate(playerListContent as RectTransform);
    }

    /// <summary>
    /// Enable/Disable Start button based on host status & all-ready check
    /// </summary>
    public void RefreshStartButton()
    {
        var isHost = FusionLobbyManager.Instance && FusionLobbyManager.Instance.IsHost;
        startGameButton.gameObject.SetActive(isHost);
        
        if (!isHost)
            return;
        
        var nonHostPlayers = _activePlayers.FindAll(p => p.IsHost == false);
        
        var hasOtherPlayers = nonHostPlayers.Count > 0;
        var allReady = hasOtherPlayers && nonHostPlayers.TrueForAll(p => p.IsReady == true);
        
        startGameButton.interactable = allReady;
        
        Debug.Log($"[UI] Start button: nonHostPlayers={nonHostPlayers.Count}, " +
                  $"allReady={allReady}");
    }
    #endregion
    
    #region FusionLobbyManager callbacks

    private void HandleConnectedToLobby()
    {
        ShowPanel(lobbyPanel);
        SetLobbyButtons(true);
        mainStatusText.text = "";
        Debug.Log("[UI] Showing Lobby panel.");
    }

    private void HandleDisconnectedFromLobby()
    {
        _activePlayers.Clear();
        RefreshPlayerList();
        ShowPanel(mainMenuPanel);
        SetMainMenuButtons(true);
        mainStatusText.text = "Disconnected.";
    }

    private void HandleSessionListUpdated(List<SessionInfo> sessions)
    {
        // Clear old room list rows
        foreach (Transform child in roomListContent)
            Destroy(child.gameObject);
        
        Debug.Log($"Session: {sessions.Count}");

        foreach (var session in sessions)
        {
            var item = Instantiate(roomListItemPrefab, roomListContent);
            item.GetComponent<RoomListItem>().Populate(session, OnRoomListItemJoinClicked);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(roomListContent as RectTransform);
        Debug.Log($"[UI] Room list refreshed: {sessions.Count} room(s).");
    }

    private void HandleJoinedRoom(string roomName)
    {
        ShowPanel(roomPanel);
        roomTitleText.text = $"Room: {roomName}";
        _isReady = false;
        readyButton.GetComponentInChildren<TextMeshProUGUI>().text = "Ready";
        
        // Host sees Start button (disabled until all are ready), clients don't
        var isHost = FusionLobbyManager.Instance.IsHost;
        startGameButton.gameObject.SetActive(isHost);
        startGameButton.interactable = false;
        leaveRoomButton.interactable = true;
        readyButton.interactable = true;
        Debug.Log($"[UI] Entered room '{roomName}'. IsHost={isHost}");
    }
    
    private void HandleLeftRoom()
    {
        _activePlayers.Clear();
        RefreshPlayerList();
        ShowPanel(lobbyPanel);
        SetLobbyButtons(true);
        Debug.Log("[UI] Left room, back in lobby.");
    }

    private void HandleError(string message)
    {
        mainStatusText.text = $"Error: {message}";
        // Re-enable buttons so the user can retry
        SetMainMenuButtons(true);
        SetLobbyButtons(true);
        Debug.LogWarning($"[UI] Error: {message}");
    }
    #endregion
    
    #region Button Handlers
    private async void OnConnectClicked()
    {
        SetMainMenuButtons(false);
        mainStatusText.text = "Connecting...";
        await FusionLobbyManager.Instance.ConnectToLobbyAsync(playerNameInput.text);
    }

    private async void OnCreateRoomClicked()
    {
        var roomName = roomNameInput.text;
        if (string.IsNullOrWhiteSpace(roomName))
        {
            HandleError("Enter a room name.");
            return;
        }
        SetLobbyButtons(false);
        await FusionLobbyManager.Instance.CreateRoomAsync(roomName, (int)maxPlayersSlider.value);
    }

    private async void OnJoinByNameClicked()
    {
        var lobbyName = joinByNameInput.text;
        if (string.IsNullOrWhiteSpace(lobbyName))
        {
            HandleError("Enter a lobby name.");
            return;
        }
        SetLobbyButtons(false);
        await FusionLobbyManager.Instance.JoinRoomAsync(lobbyName);
    }
    
    // Called by RoomListItem buttons (passed as callback in Populate)
    private async void OnRoomListItemJoinClicked(string sessionName)
    {
        SetLobbyButtons(false);
        await FusionLobbyManager.Instance.JoinRoomAsync(sessionName);
    }

    private async void OnDisconnectClicked()
    {
        SetLobbyButtons(false);
        await FusionLobbyManager.Instance.DisconnectAsync();
    }

    private void OnReadyClicked()
    {
        _isReady = !_isReady;
        readyButton.GetComponentInChildren<TextMeshProUGUI>().text = _isReady ? "Not Ready" : "Ready";
        
        // Find our own RoomPlayer and send RPC to update networked state
        var myPlayer = FindMyRoomPlayer();
        myPlayer?.SetReady(_isReady);
    }

    private void OnStartGameClicked()
    {
        // Guard: only host can reach this (button is host-only)
        if (!FusionLobbyManager.Instance.IsHost)
            return;
        
        var nonHostPlayers = _activePlayers.FindAll(p => p.IsHost == false);
        var allReady = nonHostPlayers.Count > 0 && nonHostPlayers.TrueForAll(p => p.IsReady);
        
        if (!allReady)
        {
            Debug.LogWarning("[UI] Start blocked — not all players are ready.");
            return;
        }

        FusionLobbyManager.Instance.StartGame();
    }

    private async void OnLeaveRoomClicked()
    {
        leaveRoomButton.interactable = false;
        readyButton.interactable = false;
        startGameButton.interactable = false;
        await FusionLobbyManager.Instance.LeaveRoomAsync();
    }
    #endregion
    
    #region Helpers

    private void ShowPanel(GameObject target)
    {
        mainMenuPanel.SetActive(target == mainMenuPanel);
        lobbyPanel.SetActive(target == lobbyPanel);
        roomPanel.SetActive(target == roomPanel);
    }

    private void SetMainMenuButtons(bool state)
    {
        connectButton.interactable = state;
    }

    private void SetLobbyButtons(bool state)
    {
        createRoomButton.interactable = state;
        joinByNameButton.interactable = state;
        disconnectButton.interactable = state;
    }

    /// <summary>
    /// Find the RoomPlayer that belongs to this local client by matching Fusion's local PlayerRef
    /// </summary>
    private RoomPlayer FindMyRoomPlayer()
    {
        var runner = FusionLobbyManager.Instance?.Runner;
        return !runner ? null : _activePlayers.FirstOrDefault(rp => rp.HasInputAuthority);
    }
    #endregion
}