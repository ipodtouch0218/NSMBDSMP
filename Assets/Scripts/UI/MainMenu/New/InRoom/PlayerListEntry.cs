using JimmysUnityUtilities;
using NSMB.Utils;
using Quantum;
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Button = UnityEngine.UI.Button;
using Navigation = UnityEngine.UI.Navigation;

namespace NSMB.UI.MainMenu {
    public class PlayerListEntry : MonoBehaviour, ISelectHandler {

        //---Static Variables
        public static event Action<PlayerListEntry> PlayerMuteStateChanged;
        public static event Action<PlayerListEntry> PlayerEntrySelected;

        //---Properties
        public bool IsDropdownOpen => dropdownOptions.activeInHierarchy;

        //---Public Variables
        public PlayerRef player;
        public float typingCounter;
        public Button button;
        [NonSerialized] public int joinTick = int.MaxValue;

        //---Serialized Variables
        [SerializeField] private MainMenuCanvas canvas;
        [SerializeField] private PlayerListHandler handler;
        [SerializeField] private TMP_Text nameText, winsText, muteButtonText;
        [SerializeField] private Image colorStrip, pingImage;
        [SerializeField] private RectTransform background, dropdownBackgroundImage;
        [SerializeField] private GameObject blockerTemplate, dropdownOptions, firstButton, chattingIcon, settingsIcon, readyIcon;
        [SerializeField] private LayoutElement layout;
        [SerializeField] private Button[] allOptions, adminOnlyOptions, othersOnlyOptions;
        [SerializeField] private GameObject playerExistsGameObject;

        //---Private Variables
        private GameObject blockerInstance;
        private EntityRef playerDataEntity;
        private string userId;
        private string cachedNickname;
        private string nicknameColor;
        private bool constantNicknameColor;
        private int orderIndex;
        private int cachedWins;

        public void OnEnable() {
            Settings.OnColorblindModeChanged += OnColorblindModeChanged;
            ChatManager.OnChatMessage += OnChatMessage;

            QuantumGame game = NetworkHandler.Game;
            if (game != null) {
                UpdateText(NetworkHandler.Game.Frames.Predicted);
            }
            dropdownOptions.SetActive(false);
        }

        public void OnDisable() {
            Settings.OnColorblindModeChanged -= OnColorblindModeChanged;
            ChatManager.OnChatMessage -= OnChatMessage;
        }

        public void OnDestroy() {
            if (blockerInstance) {
                Destroy(blockerInstance);
            }
        }

        public void Start() {
            QuantumEvent.Subscribe<EventPlayerDataChanged>(this, OnPlayerDataChanged, onlyIfActiveAndEnabled: true);
            QuantumEvent.Subscribe<EventGameStateChanged>(this, OnGameStateChanged);
            QuantumEvent.Subscribe<EventPlayerStartedTyping>(this, OnPlayerStartedTyping);
            QuantumEvent.Subscribe<EventPlayerRemoved>(this, OnPlayerRemoved);
            QuantumCallback.Subscribe<CallbackUpdateView>(this, OnUpdateView, onlyIfActiveAndEnabled: true);

            if (!player.IsValid) {
                playerExistsGameObject.SetActive(false);
            }
        }

        public void OnUpdateView(CallbackUpdateView e) {
            if (!constantNicknameColor) {
                nameText.color = Utils.Utils.SampleNicknameColor(nicknameColor, out _);
            }

            if (typingCounter > 0 && !ChatManager.Instance.mutedPlayers.Contains(userId)) {
                chattingIcon.SetActive(true);
                typingCounter -= Time.deltaTime;
            } else {
                chattingIcon.SetActive(false);
                typingCounter = 0;
            };
        }

        public unsafe void SetPlayer(Frame f, PlayerRef player) {
            this.player = player;
            RuntimePlayer runtimePlayer = NetworkHandler.Game.Frames.Predicted.GetPlayerData(player);
            nicknameColor = runtimePlayer?.NicknameColor ?? "#FFFFFF";
            cachedNickname = runtimePlayer.PlayerNickname.ToValidUsername(f, player);
            userId = runtimePlayer?.UserId;
            nameText.color = Utils.Utils.SampleNicknameColor(nicknameColor, out constantNicknameColor);

            playerExistsGameObject.SetActive(true);
            joinTick = QuantumUtils.GetPlayerData(f, player)->JoinTick;
            name = $"{(runtimePlayer?.PlayerNickname ?? "noname")} ({userId})";
            dropdownOptions.SetActive(false);
        }

        public void RemovePlayer() {
            player = PlayerRef.None;
            nicknameColor = default;
            userId = default;
            joinTick = int.MaxValue;
            playerExistsGameObject.SetActive(false);
            dropdownOptions.SetActive(false);
        }

        private readonly StringBuilder builder = new();
        public unsafe void UpdateText(Frame f) {
            colorStrip.color = Utils.Utils.GetPlayerColor(f, player);
            var playerData = QuantumUtils.GetPlayerData(f, player);
            playerExistsGameObject.SetActive(playerData != null);

            if (playerData == null) {
                return;
            }

            // Wins text
            if (cachedWins != playerData->Wins) {
                if (playerData->Wins == 0) {
                    winsText.text = "";
                } else {
                    builder.Clear();
                    winsText.text = builder.Append("<sprite name=room_wins>").Append(playerData->Wins).ToString();
                }
                cachedWins = playerData->Wins;
            }

            // Ping text
            pingImage.sprite = Utils.Utils.GetPingSprite(playerData->Ping);

            // Name text
            RuntimePlayer runtimePlayer = f.GetPlayerData(player);
            builder.Clear();

            if (ChatManager.Instance.mutedPlayers.Contains(runtimePlayer.UserId)) {
                builder.Append("<sprite name=player_muted>");
            }

            if (playerData->IsRoomHost) {
                builder.Append("<sprite name=room_host>");
            }

            int characterIndex = playerData->Character;
            characterIndex %= GlobalController.Instance.config.CharacterDatas.Length;
            builder.Append(GlobalController.Instance.config.CharacterDatas[characterIndex].UiString);

            if (f.Global->Rules.TeamsEnabled && Settings.Instance.GraphicsColorblind && !playerData->ManualSpectator) {
                TeamAsset team = f.SimulationConfig.Teams[playerData->RequestedTeam];
                builder.Append(team.textSpriteColorblindBig);
            }

            builder.Append(cachedNickname);
            nameText.text = builder.ToString();

            Transform parent = transform.parent;
            orderIndex = 0;
            for (int i = 0; i < parent.childCount; i++) {
                if (parent.GetChild(i) != transform) {
                    continue;
                }

                orderIndex = i;
                break;
            }

            layout.layoutPriority = transform.parent.childCount - orderIndex;
        }

        public unsafe void ShowDropdown() {
            if (blockerInstance) {
                Destroy(blockerInstance);
            }

            foreach (var optionButton in allOptions) {
                optionButton.gameObject.SetActive(true);
                optionButton.navigation = new Navigation {
                    mode = Navigation.Mode.Explicit,
                };
            }

            QuantumGame game = NetworkHandler.Game;
            bool adminOptions = false;
            foreach (PlayerRef localPlayer in game.GetLocalPlayers()) {
                if (QuantumUtils.GetPlayerData(game.Frames.Predicted, localPlayer)->IsRoomHost) {
                    adminOptions = true;
                    break;
                }
            }

            if (!adminOptions) {
                foreach (var optionButton in adminOnlyOptions) {
                    optionButton.gameObject.SetActive(false);
                }
            }

            bool othersOptions = !game.PlayerIsLocal(player);
            if (!othersOptions) {
                foreach (var optionButton  in othersOnlyOptions) {
                    optionButton.gameObject.SetActive(false);
                }
            }

            Button first = null;
            Button previous = null;
            foreach (var current in allOptions) {
                if (!current.gameObject.activeSelf) {
                    continue;
                }
                if (!first) {
                    first = current;
                }

                // Update navigation.
                if (previous) {
                    Navigation previousNavigation = previous.navigation;
                    previousNavigation.selectOnDown = current;
                    previous.navigation = previousNavigation;

                    Navigation currentNaviation = current.navigation;
                    currentNaviation.selectOnUp = previous;
                    current.navigation = currentNaviation;
                }
                previous = current;
            }

            Canvas.ForceUpdateCanvases();

            blockerInstance = Instantiate(blockerTemplate, canvas.transform);
            RectTransform blockerTransform = blockerInstance.GetComponent<RectTransform>();
            blockerTransform.offsetMax = blockerTransform.offsetMin = Vector2.zero;
            blockerInstance.SetActive(true);
            dropdownOptions.SetActive(true);

            // FLip options if needed
            bool flip = orderIndex >= 5;
            RectTransform dropdownRect = (RectTransform) dropdownOptions.transform;
            dropdownRect.SetPivotY(flip ? 0 : 1);
            dropdownRect.SetAnchoredPositionY(0);
            dropdownBackgroundImage.transform.localScale = flip ? new Vector3(1, -1, 1) : Vector3.one;

            canvas.EventSystem.SetSelectedGameObject(first.gameObject);
            canvas.PlayCursorSound();
        }

        public void HideDropdown(bool didAction) {
            if (blockerInstance) {
                Destroy(blockerInstance);
            }
            dropdownOptions.SetActive(false);
            canvas.PlaySound(didAction ? SoundEffect.UI_Decide : SoundEffect.UI_Back);
            canvas.EventSystem.SetSelectedGameObject(button.gameObject);
        }

        public void BanPlayer() {
            // TODO MainMenuManager.Instance.Ban(player);
            HideDropdown(true);
        }

        public unsafe void KickPlayer() {
            QuantumGame game = NetworkHandler.Game;
            PlayerRef host = QuantumUtils.GetHostPlayer(game.Frames.Predicted, out _);
            if (game.PlayerIsLocal(host)) {
                int slot = game.GetLocalPlayerSlots()[game.GetLocalPlayers().IndexOf(host)];
                game.SendCommand(slot, new CommandKickPlayer {
                    Target = player
                });
            }
            HideDropdown(true);
        }

        public void MutePlayer() {
            Frame f = NetworkHandler.Game.Frames.Predicted;
            RuntimePlayer runtimePlayer = f.GetPlayerData(player);
            if (runtimePlayer != null) {
                HashSet<string> mutedPlayers = ChatManager.Instance.mutedPlayers;
                if (mutedPlayers.Contains(userId)) {
                    mutedPlayers.Remove(userId);
                    ChatManager.Instance.AddSystemMessage("ui.inroom.chat.player.unmuted", ChatManager.Blue, "playername", runtimePlayer.PlayerNickname.ToValidUsername(f, player));
                    muteButtonText.text = GlobalController.Instance.translationManager.GetTranslation("ui.inroom.player.mute");
                } else {
                    mutedPlayers.Add(userId);
                    ChatManager.Instance.AddSystemMessage("ui.inroom.chat.player.muted", ChatManager.Blue, "playername", runtimePlayer.PlayerNickname.ToValidUsername(f, player));
                    muteButtonText.text = GlobalController.Instance.translationManager.GetTranslation("ui.inroom.player.unmute");
                }
            }

            PlayerMuteStateChanged?.Invoke(this);
            UpdateText(f);
            HideDropdown(true);
        }

        public void PromotePlayer() {
            QuantumGame game = NetworkHandler.Game;
            game.SendCommand(new CommandChangeHost {
                NewHost = player,
            });
            Frame f = game.Frames.Predicted;
            RuntimePlayer runtimePlayer = f.GetPlayerData(player);
            if (runtimePlayer != null) {
                ChatManager.Instance.AddSystemMessage("ui.inroom.chat.player.promoted", ChatManager.Blue, "playername", runtimePlayer.PlayerNickname.ToValidUsername(f, player));
            }
            HideDropdown(true);
        }

        public void CopyPlayerId() {
            QuantumGame game = NetworkHandler.Game;
            Frame f = game.Frames.Predicted;
            RuntimePlayer runtimePlayer = f.GetPlayerData(player);

            TextEditor te = new() {
                text = runtimePlayer.UserId.ToString(),
            };
            te.SelectAll();
            te.Copy();
            HideDropdown(true);
        }

        //---Callbacks
        private void OnColorblindModeChanged() {
            if (NetworkHandler.Game != null) {
                UpdateText(NetworkHandler.Game.Frames.Predicted);
            }
        }

        private void OnGameStateChanged(EventGameStateChanged e) {
            if (e.NewState == GameState.PreGameRoom) {
                UpdateText(e.Frame);
            }
        }

        private unsafe void OnPlayerDataChanged(EventPlayerDataChanged e) {
            if (e.Player != player) {
                return;
            }

            var playerData = QuantumUtils.GetPlayerData(e.Frame, e.Player);
            readyIcon.SetActive(playerData->IsReady);
            settingsIcon.SetActive(playerData->IsInSettings);
            handler.GetPlayerEntry(e.Player).UpdateText(e.Frame);
        }

        public void OnSelect(BaseEventData eventData) {
            PlayerEntrySelected?.Invoke(this);
        }

        private void OnPlayerStartedTyping(EventPlayerStartedTyping e) {
            if (player == e.Player) {
                typingCounter = 4;
            }
        }

        private void OnChatMessage(ChatManager.ChatMessageData data) {
            if (player == data.player) {
                typingCounter = 0;
            }
        }

        private void OnPlayerRemoved(EventPlayerRemoved e) {
            RuntimePlayer runtimePlayer;
            if (!player.IsValid || (runtimePlayer = e.Frame.GetPlayerData(player)) == null) {
                return;
            }

            cachedNickname = runtimePlayer.PlayerNickname.ToValidUsername(e.Frame, player);
        }
    }
}