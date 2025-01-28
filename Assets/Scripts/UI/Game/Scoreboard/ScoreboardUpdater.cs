using NSMB.Extensions;
using Quantum;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NSMB.UI.Game.Scoreboard {
    public class ScoreboardUpdater : MonoBehaviour {

        //---Properties
        public bool RequestSorting { get; set; }
        public EntityRef Target => playerElements.Entity;

        //---Serialized Variables
        [SerializeField] private PlayerElements playerElements;
        [SerializeField] private ScoreboardEntry entryTemplate;
        [SerializeField] private GameObject teamHeader;
        [SerializeField] private TMP_Text spectatorText, teamHeaderText;
        [SerializeField] private Animator animator;
        [SerializeField] private InputActionReference toggleScoreboardAction;

        //---Private Variables
        private readonly List<ScoreboardEntry> entries = new();
        private bool isToggled;

        public void OnValidate() {
            this.SetIfNull(ref playerElements, UnityExtensions.GetComponentType.Parent);
        }

        public void Initialize() {
            ShowWithoutAnimation();
        }

        public void OnEnable() {
            toggleScoreboardAction.action.performed += OnToggleScoreboard;
            toggleScoreboardAction.action.actionMap.Enable();
            Settings.OnColorblindModeChanged += OnColorblindModeChanged;
        }

        public void OnDisable() {
            toggleScoreboardAction.action.performed -= OnToggleScoreboard;
            Settings.OnColorblindModeChanged -= OnColorblindModeChanged;
        }

        public unsafe void Start() {
            // Populate the scoreboard if we're a late joiner
            QuantumGame game = QuantumRunner.DefaultGame;
            if (game != null) {
                PopulateScoreboard(game.Frames.Predicted);
            }

            QuantumCallback.Subscribe<CallbackGameResynced>(this, OnGameResynced);
            QuantumCallback.Subscribe<CallbackUpdateView>(this, OnUpdateView);
            QuantumEvent.Subscribe<EventMarioPlayerDied>(this, OnMarioPlayerDied);
            QuantumEvent.Subscribe<EventMarioPlayerCollectedStar>(this, OnMarioPlayerCollectedStar);
            QuantumEvent.Subscribe<EventMarioPlayerDroppedStar>(this, OnMarioPlayerDroppedStar);
            QuantumEvent.Subscribe<EventMarioPlayerRespawned>(this, OnMarioPlayerRespawned);
            QuantumEvent.Subscribe<EventPlayerAdded>(this, OnPlayerAdded);
            QuantumEvent.Subscribe<EventPlayerRemoved>(this, OnPlayerRemoved);
        }

        public void OnUpdateView(CallbackUpdateView e) {
            if (!RequestSorting) {
                return;
            }

            Frame f = e.Game.Frames.Predicted;
            SortScoreboard(f);
            RequestSorting = false;
        }

        public unsafe void PopulateScoreboard(Frame f) {
            UpdateTeamHeader(f);
            UpdateSpectatorCount(f);

            for (int i = 0; i < f.Global->RealPlayers; i++) {
                ref PlayerInformation info = ref f.Global->PlayerInfo[i];

                EntityRef entity = default;
                var filter = f.Filter<MarioPlayer>();
                while (filter.NextUnsafe(out EntityRef marioEntity, out MarioPlayer* mario)) {
                    if (mario->PlayerRef == info.PlayerRef) {
                        entity = marioEntity;
                        break;
                    }
                }

                ScoreboardEntry newEntry = Instantiate(entryTemplate, entryTemplate.transform.parent);
                newEntry.Initialize(f, i, entity, this);
                entries.Add(newEntry);
            }

            SortScoreboard(f);
        }

        public unsafe void SortScoreboard(Frame f) {
            entries.Sort((se1, se2) => {
                if (f.Exists(se1.Target) && !f.Exists(se2.Target)) {
                    return -1;
                } else if (f.Exists(se2.Target) && !f.Exists(se1.Target)) {
                    return 1;
                } else if (!f.Exists(se1.Target) && !f.Exists(se2.Target)) {
                    return 0;
                }

                var mario1 = f.Unsafe.GetPointer<MarioPlayer>(se1.Target);
                var mario2 = f.Unsafe.GetPointer<MarioPlayer>(se2.Target);

                if (f.Global->Rules.IsLivesEnabled && ((mario1->Lives == 0) ^ (mario2->Lives == 0))) {
                    return mario2->Lives - mario1->Lives;
                }

                int starDiff = mario2->Stars - mario1->Stars;
                if (starDiff != 0) {
                    return starDiff;
                }

                var playerDataOne = QuantumUtils.GetPlayerData(f, mario1->PlayerRef);
                var playerDataTwo = QuantumUtils.GetPlayerData(f, mario2->PlayerRef);
                if (playerDataOne == null || playerDataTwo == null) {
                    return 0;
                }
                return playerDataOne->JoinTick - playerDataTwo->JoinTick;
            });

            foreach (var entry in entries) {
                entry.transform.SetAsLastSibling();
            }
            spectatorText.transform.SetAsLastSibling();
        }

        public unsafe void UpdateTeamHeader(Frame f) {
            bool teamsEnabled = f.Global->Rules.TeamsEnabled;
            teamHeader.SetActive(teamsEnabled);

            if (!teamsEnabled) {
                return;
            }

            TeamAsset[] teamAssets = f.SimulationConfig.Teams;
            StringBuilder result = new();

            byte[] teamStars = new byte[10];
            QuantumUtils.GetTeamStars(f, teamStars);
            int aliveTeams = QuantumUtils.GetValidTeams(f);
            for (int i = 0; i < 10; i++) {
                if ((aliveTeams & (1 << i)) == 0) {
                    // Invalid team
                    continue;
                }

                byte stars = teamStars[i];
                result.Append(Settings.Instance.GraphicsColorblind ? teamAssets[i].textSpriteColorblind : teamAssets[i].textSpriteNormal);
                result.Append(Utils.Utils.GetSymbolString("x" + stars));
            }

            teamHeaderText.text = result.ToString();
        }

        public unsafe void UpdateSpectatorCount(Frame f) {
            int spectators = 0;
            var playerDataFilter = f.Filter<PlayerData>();
            while (playerDataFilter.NextUnsafe(out _, out PlayerData* playerData)) {
                if (playerData->IsSpectator) {
                    spectators++;
                }
            }

            if (spectators > 0) {
                spectatorText.text = "<sprite name=room_spectator>" + Utils.Utils.GetSymbolString("x" + spectators.ToString());
            } else {
                spectatorText.text = "";
            }
        }

        public void Toggle() {
            isToggled = !isToggled;
            PlayAnimation(isToggled);
        }

        public void Show() {
            isToggled = true;
            PlayAnimation(isToggled);
        }

        public void ShowWithoutAnimation() {
            isToggled = true;
            animator.SetFloat("speed", 1);
            animator.Play("toggle", 0, 0.999f);
        }

        public void Hide() {
            isToggled = false;
            PlayAnimation(isToggled);
        }

        public void PlayAnimation(bool enabled) {
            animator.SetFloat("speed", enabled ? 1 : -1);
            animator.Play("toggle", 0, Mathf.Clamp01(animator.GetCurrentAnimatorStateInfo(0).normalizedTime));
        }

        public EntityRef EntityAtPosition(int index) {
            if (index < 0 || index >= entries.Count) {
                return EntityRef.None;
            }

            return entries[index].Target;
        }

        private void OnToggleScoreboard(InputAction.CallbackContext context) {
            if (context.canceled) {
                return;
            }

            Toggle();
        }

        private void OnMarioPlayerDroppedStar(EventMarioPlayerDroppedStar e) {
            UpdateTeamHeader(e.Frame);
        }

        private void OnMarioPlayerDied(EventMarioPlayerDied e) {
            if (e.Entity != Target) {
                return;
            }

            Show();
        }

        private void OnMarioPlayerCollectedStar(EventMarioPlayerCollectedStar e) {
            UpdateTeamHeader(e.Frame);
        }

        private void OnMarioPlayerRespawned(EventMarioPlayerRespawned e) {
            if (e.Entity != Target) {
                return;
            }

            if (!Settings.Instance.generalScoreboardAlways) {
                Hide();
            }
        }

        private void OnPlayerAdded(EventPlayerAdded e) {
            UpdateSpectatorCount(e.Frame);
        }

        private void OnPlayerRemoved(EventPlayerRemoved e) {
            UpdateSpectatorCount(e.Frame);
        }

        private void OnColorblindModeChanged() {
            UpdateTeamHeader(NetworkHandler.Game.Frames.Predicted);
        }

        private void OnGameResynced(CallbackGameResynced e) {
            Frame f = e.Game.Frames.Predicted;
            UpdateTeamHeader(f);
            UpdateSpectatorCount(f);
        }
    }
}