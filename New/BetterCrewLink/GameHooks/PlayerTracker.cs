using BetterCrewLink.Data;
using BetterCrewLink.Networking;
using InnerNet;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BetterCrewLink.GameHooks;

public enum GamePhase
{
    Menu,
    Lobby,
    Tasks,
    Discussion
}

public sealed record PlayerSnapshot(
    int PlayerId,
    int ClientId,
    string Name,
    bool IsLocal,
    bool IsDead,
    bool IsImpostor,
    bool InVent,
    Vector2 Position
);

public sealed record GameSnapshot(
    GamePhase Phase,
    string LobbyCode,
    int LobbyCodeInt,
    int ClientId,
    int PlayerId,
    bool IsHost,
    PlayerSnapshot LocalPlayer,
    IReadOnlyList<PlayerSnapshot> Players,
    bool CommsSabActive,
    MapType Map,
    int ActiveCameraIndex
);

public sealed class PlayerTracker
{
    public GameSnapshot? Update()
    {
        var client = AmongUsClient.Instance;
        var local  = PlayerControl.LocalPlayer;

        if (client == null || local == null)
            return null;

        var phase = client.GameState switch
        {
            InnerNetClient.GameStates.NotJoined => GamePhase.Menu,
            InnerNetClient.GameStates.Joined    => GamePhase.Lobby,
            InnerNetClient.GameStates.Started   => MeetingHud.Instance ? GamePhase.Discussion : GamePhase.Tasks,
            InnerNetClient.GameStates.Ended     => GamePhase.Lobby,
            _                                   => GamePhase.Menu,
        };

        var players = PlayerControl.AllPlayerControls
            .ToArray()
            .Where(p => p != null && p.Data != null)
            .Select(p => BuildSnapshot(p, client.ClientId))
            .ToList();

        var localSnapshot = players.FirstOrDefault(p => p.IsLocal);
        if (localSnapshot == null)
            return null;

        var gameOptions = GameOptionsManager.Instance?.CurrentGameOptions;
        var map = gameOptions != null ? (MapType)gameOptions.MapId : MapType.TheSkeld;

        return new GameSnapshot(
            Phase:            phase,
            LobbyCode:        GameCode.IntToGameName(client.GameId),
            LobbyCodeInt:     client.GameId,
            ClientId:         client.ClientId,
            PlayerId:         localSnapshot.PlayerId,
            IsHost:           client.AmHost,
            LocalPlayer:      localSnapshot,
            Players:          players,
            CommsSabActive:   Utilities.IsCommsSabotaged(),
            Map:              map,
            ActiveCameraIndex: VoiceClient.ActiveCameraIndex
        );
    }

    private static PlayerSnapshot BuildSnapshot(PlayerControl player, int localClientId)
    {
        var data = player.Data;
        var pos  = player.GetTruePosition();

        return new PlayerSnapshot(
            PlayerId:  data.PlayerId,
            ClientId:  data.ClientId,
            Name:      data.PlayerName,
            IsLocal:   data.ClientId == localClientId,
            IsDead:    data.IsDead,
            IsImpostor: player.IsImpostor(),
            InVent:    player.inVent,
            Position:  new Vector2(pos.x, pos.y)
        );
    }
}
