using BetterCrewLink.GameHooks;
using BetterCrewLink.Utils;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCrewLink.Voice;

// Computes per-player volume based on distance and basic game state.
public sealed class ProximityManager
{
    public Dictionary<int, float> ComputeVolumes(GameSnapshot snapshot, RuntimeSettings settings)
    {
        var result = new Dictionary<int, float>();
        var me = snapshot.LocalPlayer;

        foreach (var other in snapshot.Players)
        {
            if (other.IsLocal)
                continue;

            var gain = ComputeGain(snapshot.Phase, me, other, settings);
            result[other.ClientId] = gain;
        }

        return result;
    }

    private static float ComputeGain(GamePhase phase, PlayerSnapshot me, PlayerSnapshot other, RuntimeSettings settings)
    {
        if (phase == GamePhase.Menu)
            return 0f;

        // Meetings: treat everyone as nearby; mute dead for living players.
        if (phase == GamePhase.Discussion)
        {
            if (!me.IsDead && other.IsDead)
                return 0f;
            return Mathf.Clamp01(settings.MasterVolume / 100f);
        }

        // Tasks/Lobby: apply proximity and ghost rules.
        if (!me.IsDead && other.IsDead)
        {
            // Living players do not hear ghosts by default.
            if (!me.IsImpostor)
                return 0f;

            // Impostors can hear ghosts if configured.
            return Mathf.Clamp01(settings.GhostVolumeAsImpostor / 100f) * (settings.MasterVolume / 100f);
        }

        if (me.IsDead && !other.IsDead)
        {
            // Ghosts hear living players with a separate volume scaler.
            return Mathf.Clamp01(settings.CrewVolumeAsGhost / 100f) * (settings.MasterVolume / 100f);
        }

        var delta = other.Position - me.Position;
        var distance = delta.magnitude;
        if (distance > settings.MaxDistance)
            return 0f;

        var distanceGain = 1f - (distance / settings.MaxDistance);
        return Mathf.Clamp01(distanceGain) * (settings.MasterVolume / 100f);
    }
}
