using BetterCrewLink.Utils;
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
    IReadOnlyList<PlayerSnapshot> Players
);

// Reads in-game player state directly from Among Us objects.
public sealed class PlayerTracker
{
    public GameSnapshot? Update()
    {
        var client = AmongUsClient.Instance;
        var local = PlayerControl.LocalPlayer;

        if (client == null || local == null)
            return null;

        var phase = client.GameState switch
        {
            InnerNet.InnerNetClient.GameStates.NotJoined => GamePhase.Menu,
            InnerNet.InnerNetClient.GameStates.Joined => GamePhase.Lobby,
            InnerNet.InnerNetClient.GameStates.Started => MeetingHud.Instance ? GamePhase.Discussion : GamePhase.Tasks,
            InnerNet.InnerNetClient.GameStates.Ended => GamePhase.Lobby,
            _ => GamePhase.Menu,
        };

        var players = PlayerControl.AllPlayerControls
            .ToArray()
            .Select(p => BuildSnapshot(p, client.ClientId))
            .ToList();

        var localSnapshot = players.FirstOrDefault(p => p.IsLocal);
        if (localSnapshot == null)
            return null;

        return new GameSnapshot(
            phase,
            GameCode.IntToGameName(client.GameId),
            client.GameId,
            client.ClientId,
            localSnapshot.PlayerId,
            client.AmHost,
            localSnapshot,
            players
        );
    }

    private static PlayerSnapshot BuildSnapshot(PlayerControl player, int localClientId)
    {
        var data = player.Data;
        var pos = player.GetTruePosition();

        return new PlayerSnapshot(
            data.PlayerId,
            data.ClientId,
            data.PlayerName,
            data.ClientId == localClientId,
            data.IsDead,
            player.IsImpostor(),
            player.inVent,
            new Vector2(pos.x, pos.y)
        );
    }
}
