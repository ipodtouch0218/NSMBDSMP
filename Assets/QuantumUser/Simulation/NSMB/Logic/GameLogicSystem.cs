using Photon.Deterministic;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Quantum {
    public unsafe class GameLogicSystem : SystemMainThread, ISignalOnPlayerAdded, ISignalOnPlayerRemoved, ISignalOnMarioPlayerDied,
        ISignalOnLoadingComplete, ISignalOnMarioPlayerCollectedStar, ISignalOnReturnToRoom, ISignalOnComponentRemoved<MarioPlayer> {

        public override void OnInit(Frame f) {
            var config = f.RuntimeConfig;
            f.Global->Rules = f.SimulationConfig.DefaultRules;

            // Support booting in the editor.
            if (!config.IsRealGame) {
                f.Global->GameState = GameState.WaitingForPlayers;
                f.Global->PlayerLoadFrames = (ushort) (20 * f.UpdateRate);
            } else {
                f.Events.GameStateChanged(f, f.Global->GameState);
            }
        }

        public override void Update(Frame f) {
            // Parse lobby commands
            var playerDataDictionary = f.ResolveDictionary(f.Global->PlayerDatas);
            for (int i = 0; i < f.PlayerCount; i++) {
                if (f.GetPlayerCommand(i) is ILobbyCommand lobbyCommand) {
                    var playerData = QuantumUtils.GetPlayerData(f, i, playerDataDictionary);
                    if (playerData == null) {
                        continue;
                    }

                    lobbyCommand.Execute(f, i, playerData);
                }
            }

            // Gaem state logic
            switch (f.Global->GameState) {
            case GameState.PreGameRoom:
                if (f.Global->GameStartFrames > 0) {
                    if (QuantumUtils.Decrement(ref f.Global->GameStartFrames)) {
                        // Start the game!
                        if (f.IsVerified) {
                            f.MapAssetRef = f.Global->Rules.Stage;
                        }
                        f.Global->PlayerLoadFrames = (ushort) (20 * f.UpdateRate);
                        f.Global->GameState = GameState.WaitingForPlayers;

                        f.Events.GameStateChanged(f, GameState.WaitingForPlayers);
                    } else if (f.Global->GameStartFrames % 60 == 0) {
                        f.Events.CountdownTick(f, f.Global->GameStartFrames / 60);
                    }
                }
                break;
            case GameState.WaitingForPlayers:
                var playerDataFilter = f.Filter<PlayerData>();
                int validPlayers = 0;
                int loadedPlayers = 0;
                while (playerDataFilter.NextUnsafe(out _, out PlayerData* data)) {
                    if (!f.RuntimeConfig.IsRealGame) {
                        data->IsLoaded = true;
                        data->IsSpectator = false;
                    }

                    if (!data->IsSpectator) {
                        validPlayers++;
                    }
                    if (data->IsLoaded) {
                        loadedPlayers++;
                    }
                }
                f.Global->RealPlayers = (byte) validPlayers;

                if (validPlayers <= 0) {
                    break;
                }

                if (QuantumUtils.Decrement(ref f.Global->PlayerLoadFrames) || !f.RuntimeConfig.IsRealGame || (validPlayers == loadedPlayers)) {
                    // Progress to next stage.
                    f.Global->RealPlayers = (byte) loadedPlayers;
                    f.Global->GameState = GameState.Starting;
                    f.Global->GameStartFrames = 6 * 60;
                    f.Global->Timer = f.Global->Rules.TimerSeconds;

                    f.Signals.OnLoadingComplete();
                    f.Events.GameStateChanged(f, GameState.Starting);
                }
                break;
            case GameState.Starting:
                if (QuantumUtils.Decrement(ref f.Global->GameStartFrames)) {
                    // Now playing
                    f.Global->GameState = GameState.Playing;
                    f.Events.GameStateChanged(f, GameState.Playing);
                    f.Global->StartFrame = f.Number;

                } else if (f.Global->GameStartFrames == 79) {
                    f.Events.RecordingStarted(f);

                } if (f.Global->GameStartFrames == 78) {
                    // Respawn all players and enable systems
                    f.SystemEnable<StartDisabledSystemGroup>();
                    f.Signals.OnGameStarting();
                    f.Events.GameStarted(f);
                }
                break;

            case GameState.Playing:
                if (f.Global->Rules.TimerSeconds > 0 && f.Global->Timer > 0) {
                    if ((f.Global->Timer -= f.DeltaTime) <= 0) {
                        f.Global->Timer = 0;
                        CheckForGameEnd(f);
                        f.Events.TimerExpired(f);
                    }
                }

                PlayerRef host = QuantumUtils.GetHostPlayer(f, out _);
                if (f.GetPlayerCommand(host) is CommandHostEndGame) {
                    EndGame(f, null);
                }
                break;

            case GameState.Ended:
                QuantumUtils.Decrement(ref f.Global->GameStartFrames);
                if (f.Global->GameStartFrames == 30) {
                    f.Events.StartGameEndFade(f);
                }

                if (f.Global->GameStartFrames == 0) {
                    // Move back to lobby.
                    f.Global->TotalGamesPlayed++;
                    if (f.IsVerified) {
                        //f.MapAssetRef = f.SimulationConfig.LobbyMap;
                        f.Map = null;
                    }
                    f.SystemEnable<StartDisabledSystemGroup>();
                    f.Signals.OnReturnToRoom();
                    f.Global->GameState = GameState.PreGameRoom;
                    f.Events.GameStateChanged(f, GameState.PreGameRoom);
                    f.SystemDisable<StartDisabledSystemGroup>();
                }
                break;
            }
        }

        public static void StopCountdown(Frame f) {
            f.Global->GameStartFrames = 0;
            f.Events.StartingCountdownChanged(f, false);
        }

        public static void CheckForGameEnd(Frame f) {
            // End Condition: only one team alive
            var marioFilter = f.Filter<MarioPlayer>();
            marioFilter.UseCulling = false;

            bool livesGame = f.Global->Rules.IsLivesEnabled;
            bool oneOrNoTeamAlive = true;
            int aliveTeam = -1;
            while (marioFilter.NextUnsafe(out _, out MarioPlayer* mario)) {
                if ((livesGame && mario->Lives <= 0) || mario->Disconnected) {
                    continue;
                }

                if (aliveTeam == -1) {
                    aliveTeam = mario->GetTeam(f);
                } else {
                    oneOrNoTeamAlive = false;
                    break;
                }
            }

            if (oneOrNoTeamAlive) {
                if (aliveTeam == -1) {
                    // It's a draw
                    EndGame(f, null);
                    return;
                } else if (f.Global->RealPlayers > 1) {
                    // <team> wins, assuming more than 1 player
                    // so the player doesn't insta-win in a solo game.
                    EndGame(f, aliveTeam);
                    return;
                }
            }

            int? winningTeam = QuantumUtils.GetWinningTeam(f, out int stars);

            // End Condition: team gets to enough stars
            if (winningTeam != null && stars >= f.Global->Rules.StarsToWin) {
                // <team> wins
                EndGame(f, winningTeam.Value);
                return;
            }

            // End Condition: timer expires
            if (f.Global->Rules.IsTimerEnabled && f.Global->Timer <= 0) {
                if (f.Global->Rules.DrawOnTimeUp) {
                    // It's a draw
                    EndGame(f, null);
                    return;
                }

                // Check if one team is winning
                if (winningTeam != null) {
                    // <team> wins
                    EndGame(f, winningTeam.Value);
                    return;
                }
            }
        }

        public static void EndGame(Frame f, int? winningTeam) {
            if (f.Global->GameState != GameState.Playing) {
                return;
            }

            f.Global->WinningTeam = winningTeam.GetValueOrDefault();
            f.Global->HasWinner = winningTeam.HasValue;

            f.Signals.OnGameEnding(winningTeam.GetValueOrDefault(), winningTeam.HasValue);
            f.Events.GameEnded(f, winningTeam.GetValueOrDefault(), winningTeam.HasValue);

            var playerDatas = f.Filter<PlayerData>();
            playerDatas.UseCulling = false;
            while (playerDatas.NextUnsafe(out _, out PlayerData* data)) {
                if (winningTeam == data->RealTeam && !data->IsSpectator) {
                    data->Wins++;
                }
                data->IsSpectator = data->ManualSpectator;
            }

            f.Global->GameState = GameState.Ended;
            f.Events.GameStateChanged(f, GameState.Ended);
            f.Global->GameStartFrames = (ushort) (21 * f.UpdateRate);
            f.SystemDisable<StartDisabledSystemGroup>();
        }

        public void OnMarioPlayerDied(Frame f, EntityRef entity) {
            CheckForGameEnd(f);
        }

        public void OnPlayerAdded(Frame f, PlayerRef player, bool firstTime) {
            RuntimePlayer runtimePlayer = f.GetPlayerData(player);
            EntityRef newEntity = f.Create();
            f.Add(newEntity, out PlayerData* newData);
            newData->PlayerRef = player;
            newData->JoinTick = f.Number;
            newData->IsSpectator = f.Global->GameState != GameState.PreGameRoom;
            newData->RealTeam = 255;

            // Get team counts
            int teams = f.SimulationConfig.Teams.Length;
            Span<byte> teamCounts = stackalloc byte[teams];
            var playerDatas = f.ResolveDictionary(f.Global->PlayerDatas);
            foreach ((PlayerRef otherPlayer, EntityRef otherEntity) in playerDatas) {
                var data = f.Unsafe.GetPointer<PlayerData>(otherEntity);
                if (data->RequestedTeam < teams) {
                    teamCounts[data->RequestedTeam]++;
                }
            }

            // Assign the player to the team with the least players
            int lowestTeamCount = teamCounts[0];
            int lowestTeamIndex = 0;
            for (int i = 1; i < teams; i++) {
                if (teamCounts[i] < lowestTeamCount) {
                    lowestTeamCount = teamCounts[i];
                    lowestTeamIndex = i;
                }
            }
            newData->RequestedTeam = (byte) lowestTeamIndex;

            // Other bookkeeping
            newData->Character = runtimePlayer.Character;
            newData->Palette = runtimePlayer.Palette;

            if (playerDatas.Count == 0) {
                // First player is host
                newData->IsRoomHost = true;
                f.Events.HostChanged(f, player);
            }

            foreach ((_, EntityRef otherEntity) in playerDatas) {
                var data = f.Unsafe.GetPointer<PlayerData>(otherEntity);
                if (data->RequestedTeam < teams) {
                    teamCounts[data->RequestedTeam]++;
                }
            }

            playerDatas[player] = newEntity;
            f.Events.PlayerAdded(f, player);
            f.Events.PlayerDataChanged(f, player);
        }

        public void OnPlayerRemoved(Frame f, PlayerRef player) {
            var playerDatas = f.ResolveDictionary(f.Global->PlayerDatas);
            bool hostChanged = false;

            for (int i = 0; i < f.Global->RealPlayers; i++) {
                ref PlayerInformation info = ref f.Global->PlayerInfo[i];
                if (info.PlayerRef == player) {
                    info.PlayerRef = PlayerRef.None;
                }
            }

            if (playerDatas.TryGetValue(player, out EntityRef entity)
                && f.Unsafe.TryGetPointer(entity, out PlayerData* deletedPlayerData)) {

                if (deletedPlayerData->IsRoomHost) {
                    // Give the host to the youngest player.
                    PlayerData* youngestPlayer = null;
                    foreach ((_, EntityRef otherEntity) in playerDatas) {
                        PlayerData* otherPlayerData = f.Unsafe.GetPointer<PlayerData>(otherEntity);
                        if (deletedPlayerData == otherPlayerData) {
                            continue;
                        }

                        if (youngestPlayer == null || otherPlayerData->JoinTick < youngestPlayer->JoinTick) {
                            youngestPlayer = otherPlayerData;
                        }
                    }

                    if (youngestPlayer != null) {
                        youngestPlayer->IsRoomHost = true;
                        f.Events.HostChanged(f, youngestPlayer->PlayerRef);
                    }

                    hostChanged = true;
                }

                f.Destroy(entity);
                playerDatas.Remove(player);
            }

            f.Events.PlayerRemoved(f, player);

            switch (f.Global->GameState) {
            case GameState.PreGameRoom:
                if (f.Global->GameStartFrames > 0 && (hostChanged || !QuantumUtils.IsGameStartable(f))) {
                    StopCountdown(f);
                }
                break;
            case GameState.Starting:
            case GameState.Playing:
                for (int i = 0; i < f.Global->RealPlayers; i++) {
                    ref PlayerInformation info = ref f.Global->PlayerInfo[i];
                    if (info.PlayerRef != player) {
                        continue;
                    }

                    info.Disconnected = true;
                    info.Disqualified = true;
                    break;
                }
                break;
            }
        }

        public void OnLoadingComplete(Frame f) {
            // Spawn players
            var config = f.SimulationConfig;
            var stage = f.FindAsset<VersusStageData>(f.Map.UserAsset);
            int teamCount = 0;

            var playerDatas = f.Filter<PlayerData>();
            int playerCount = 0;
            while (playerDatas.NextUnsafe(out _, out PlayerData* data)) {
                if (!data->IsLoaded) {
                    // Force spectator, didn't load in time
                    data->IsSpectator = true;
                    continue;
                }

                if (data->IsSpectator) {
                    continue;
                }

                int characterIndex = FPMath.Clamp(data->Character, 0, config.CharacterDatas.Length - 1);
                CharacterAsset character = config.CharacterDatas[characterIndex];

                EntityRef newPlayer = f.Create(character.Prototype);
                var mario = f.Unsafe.GetPointer<MarioPlayer>(newPlayer);
                mario->PlayerRef = data->PlayerRef;
                data->RealTeam = (byte) (f.Global->Rules.TeamsEnabled ? data->RequestedTeam : teamCount++);

                var newTransform = f.Unsafe.GetPointer<Transform2D>(newPlayer);
                newTransform->Position = stage.Spawnpoint;

                // Save runtimeplayer info for late joiners, in case this player DCs
                RuntimePlayer runtimePlayer = f.GetPlayerData(data->PlayerRef);
                f.Global->PlayerInfo[playerCount++] = new PlayerInformation {
                    PlayerRef = data->PlayerRef,
                    Nickname = runtimePlayer.PlayerNickname,
                    NicknameColor = runtimePlayer.NicknameColor,
                    Character = (byte) characterIndex,
                    Team = data->RealTeam,
                };
            }

            // Assign random spawnpoints
            f.Global->TotalMarios = (byte) f.ComponentCount<MarioPlayer>();
            List<int> spawnpoints = Enumerable.Range(0, f.ComponentCount<MarioPlayer>()).ToList();
            var allMarios = f.Filter<MarioPlayer>();
            while (allMarios.NextUnsafe(out EntityRef entity, out MarioPlayer* mario)) {
                int randomIndex = FPMath.FloorToInt(f.RNG->Next() * spawnpoints.Count);
                mario->SpawnpointIndex = (byte) spawnpoints[randomIndex];
                spawnpoints.RemoveAt(randomIndex);

                var camera = f.Unsafe.GetPointer<CameraController>(entity);
                camera->Recenter(stage, stage.GetWorldSpawnpointForPlayer(mario->SpawnpointIndex, f.Global->TotalMarios));
            }
        }

        public void OnMarioPlayerCollectedStar(Frame f, EntityRef entity) {
            CheckForGameEnd(f);
        }

        public void OnReturnToRoom(Frame f) {
            // Destroy all entities except PlayerDatas
            List<EntityRef> entities = new();
            f.GetAllEntityRefs(entities);

            foreach (var entity in entities) {
                if (f.Has<PlayerData>(entity)) {
                    continue;
                }

                f.Destroy(entity);
            }

            // Reset variables
            f.Global->Timer = 0;
            for (int i = 0; i < f.Global->PlayerInfo.Length; i++) {
                f.Global->PlayerInfo[i] = default;
            }

            var playerDatas = f.Filter<PlayerData>();
            while (playerDatas.NextUnsafe(out _, out PlayerData* data)) {
                data->IsLoaded = false;
                data->IsReady = false;
                data->IsSpectator = false;
                data->VotedToContinue = false;
                data->RealTeam = 255;
            }
        }

        public void OnRemoved(Frame f, EntityRef entity, MarioPlayer* component) {
            for (int i = 0; i < f.Global->RealPlayers; i++) {
                ref PlayerInformation info = ref f.Global->PlayerInfo[i];
                if (info.PlayerRef != component->PlayerRef) {
                    continue;
                }

                info.Disqualified = true;
                break;
            }
        }
    }
}