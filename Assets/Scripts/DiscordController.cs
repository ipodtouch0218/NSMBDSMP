using UnityEngine;
using UnityEngine.SceneManagement;
using Photon.Pun;
using Photon.Realtime;
using Discord;
using System;

public class DiscordController : MonoBehaviour {

    public Discord.Discord discord;
    public ActivityManager activityManager;

    public void Awake() {
#if UNITY_WEBGL || UNITY_ANDROID
        return;
#endif

        discord = new Discord.Discord(962073502469459999, (ulong) CreateFlags.NoRequireDiscord);
        activityManager = discord.GetActivityManager();
        activityManager.OnActivityJoinRequest += AskToJoin;
        activityManager.OnActivityJoin += TryJoinGame;

//#if UNITY_STANDALONE_WIN
        try {
            activityManager.RegisterCommand(AppDomain.CurrentDomain.BaseDirectory + AppDomain.CurrentDomain.FriendlyName);
        } catch { }
//#endif
    }

    public void TryJoinGame(string secret) {
        if (SceneManager.GetActiveScene().buildIndex != 0)
            return;

        Debug.Log($"[DISCORD] Attempting to join game with secret \"{secret}\"");
        string[] split = secret.Split("-");
        string region = split[0];
        string room = split[1];

        MainMenuManager.lastRegion = region;
        MainMenuManager.Instance.connectThroughSecret = room;
        PhotonNetwork.Disconnect();
    }

    //TODO this doesn't work???
    public void AskToJoin(ref User user) {
        //activityManager.SendRequestReply(user.Id, ActivityJoinRequestReply.Yes, (res) => {
        //    Debug.Log($"[DISCORD] Ask to Join response: {res}");
        //});
    }

    public void Update() {
#if UNITY_WEBGL || UNITY_ANDROID
        return;
#endif
        try {
            discord.RunCallbacks();
        } catch { }
    }

    public void OnDisable() {
        discord.Dispose();
    }

    public void UpdateActivity() {
#if UNITY_WEBGL || UNITY_ANDROID
        return;
#endif
        if (discord == null || activityManager == null)
            return;

        Activity activity = new();

        if (GameManager.Instance) {
            //in a level
            GameManager gm = GameManager.Instance;
            Room room = PhotonNetwork.CurrentRoom;

            activity.Details = PhotonNetwork.OfflineMode ? "Playing Offline" : "Playing Online";
            activity.Party = new() { Size = new() { CurrentSize = room.PlayerCount, MaxSize = room.MaxPlayers }, Id = PhotonNetwork.CurrentRoom.Name };
            activity.State = room.IsVisible ? "In a Public Lobby" : "In a Private Lobby";
            activity.Secrets = new() { Join = PhotonNetwork.CloudRegion + "-" + room.Name };

            ActivityAssets assets = new();
            if (gm.richPresenceId != "")
                assets.LargeImage = $"level-{gm.richPresenceId}";
            else
                assets.LargeImage = "mainmenu";
            assets.LargeText = gm.levelName;

            activity.Assets = assets;

            if (gm.timedGameDuration == -1) {
                activity.Timestamps = new() { Start = gm.startRealTime / 1000 };
            } else {
                activity.Timestamps = new() { End = gm.endRealTime / 1000 };
            }

        } else if (PhotonNetwork.InRoom) {
            //in a room
            Room room = PhotonNetwork.CurrentRoom;

            activity.Details = PhotonNetwork.OfflineMode ? "Playing Offline" : "Playing Online";
            activity.Party = new() { Size = new() { CurrentSize = room.PlayerCount, MaxSize = room.MaxPlayers }, Id = PhotonNetwork.CurrentRoom.Name };
            activity.State = room.IsVisible ? "In a Public Lobby" : "In a Private Lobby";
            activity.Secrets = new() { Join = PhotonNetwork.CloudRegion + "-" + room.Name };

            activity.Assets = new() { LargeImage = "mainmenu" };

        } else {
            //in the main menu, not in a room

            activity.Details = "Browsing the Main Menu...";
            activity.Assets = new() { LargeImage = "mainmenu" };

        }


        activityManager.UpdateActivity(activity, (res) => {
            //head empty.
            Debug.Log($"[DISCORD] Rich Presence Update: {res}");
        });
    }
}