using UnityEngine;
using TMPro;

public class PlayerListItem : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI statusText;

    /// <summary>
    /// Fills in the row with data from a RoomPlayer snapshot
    /// Called from LobbyUIController.RefreshPlayerList()
    /// </summary>
    public void Populate(RoomPlayer player)
    {
        playerNameText.text = player.PlayerName.ToString();
        statusText.text = player.IsReady ? "Ready" : "Not Ready";
        statusText.color = player.IsReady ? Color.green : Color.red;
    }
}