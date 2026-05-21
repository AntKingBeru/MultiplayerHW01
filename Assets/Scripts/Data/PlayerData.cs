using System;

[Serializable]
public class PlayerData
{
    public string playerName;
    public bool isReady;

    public PlayerData(string name)
    {
        playerName = name;
        isReady = false;
    }
}