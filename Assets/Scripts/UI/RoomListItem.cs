using UnityEngine;
using UnityEngine.UI;
using Fusion;
using System;
using TMPro;

public class RoomListItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI roomTextName;
    [SerializeField] private TextMeshProUGUI playerCountText;
    [SerializeField] private Button joinButton;

    private string _sessionName;
    private Action<string> _onJoinClicked;

    private void Awake()
    {
        joinButton.onClick.AddListener(() => _onJoinClicked?.Invoke(_sessionName));
    }

    /// <summary>
    /// Called by LobbyUIController when building the session list
    /// </summary>
    public void Populate(SessionInfo session, Action<string> onJoinClicked)
    {
        _sessionName = session.Name;
        _onJoinClicked = onJoinClicked;
        
        roomTextName.text = session.Name;
        playerCountText.text = $"{session.PlayerCount}/{session.MaxPlayers}";
        
        // Disable join if room is full or not open
        var canJoin = session.IsOpen && session.PlayerCount < session.MaxPlayers;
        joinButton.interactable = canJoin;
    }
}