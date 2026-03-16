using BetterCrewLink.Data;
using BetterCrewLink.GameHooks;
using BetterCrewLink.Networking;
using BetterCrewLink.Utils;
using System.Collections.Generic;
using UnityEngine;

namespace BetterCrewLink.Voice;

public readonly record struct PeerVolumes(
    float NormalVolume,
    float GhostVolume,
    float RadioVolume,
    float Pan,
    bool RadioEffect
);

public sealed class ProximityManager
{
    private readonly Dictionary<int, float> _wallCoeffs = new();

    public Dictionary<int, PeerVolumes> ComputeVolumes(GameSnapshot snapshot, RuntimeSettings settings)
    {
        var result = new Dictionary<int, PeerVolumes>();
        var me = snapshot.LocalPlayer;

        foreach (var other in snapshot.Players)
        {
            if (other.IsLocal)
                continue;
            result[other.ClientId] = ComputePeer(snapshot, me, other, settings);
        }

        var active = new HashSet<int>();
        foreach (var p in snapshot.Players)
            if (!p.IsLocal) active.Add(p.ClientId);
        foreach (var k in new List<int>(_wallCoeffs.Keys))
            if (!active.Contains(k)) _wallCoeffs.Remove(k);

        return result;
    }

    private PeerVolumes ComputePeer(GameSnapshot snapshot, PlayerSnapshot me, PlayerSnapshot other, RuntimeSettings s)
    {
        return snapshot.Phase switch
        {
            GamePhase.Menu       => default,
            GamePhase.Lobby      => ComputeLobby(me, other, s),
            GamePhase.Discussion => ComputeMeeting(me, other, s),
            GamePhase.Tasks      => ComputeTasks(snapshot, me, other, s),
            _                    => default,
        };
    }

    private static PeerVolumes ComputeLobby(PlayerSnapshot me, PlayerSnapshot other, RuntimeSettings s)
    {
        if (!me.IsDead && other.IsDead)
            return default;
        if (me.IsDead && !other.IsDead)
            return new PeerVolumes(0f, s.CrewVolumeAsGhost / 100f, 0f, 0f, false);
        return new PeerVolumes(1f, 0f, 0f, 0f, false);
    }

    private static PeerVolumes ComputeMeeting(PlayerSnapshot me, PlayerSnapshot other, RuntimeSettings s)
    {
        bool localDead  = me.IsDead;
        bool targetDead = other.IsDead;
        bool localImp   = me.IsImpostor;
        bool targetImp  = other.IsImpostor;

        if (s.OnlyGhostsCanTalk && !localDead)
            return default;

        if (localDead)
        {
            float ghostVol = targetDead ? 1f : s.CrewVolumeAsGhost / 100f;
            return new PeerVolumes(0f, ghostVol, 0f, 0f, false);
        }

        if (s.ImpostorPrivateRadio && localImp && targetImp && !targetDead)
            return new PeerVolumes(0f, 0f, 1f, 0f, true);

        if (s.ImpostorRadioOnly && localImp)
        {
            bool hear = targetImp && !targetDead;
            return new PeerVolumes(0f, 0f, hear ? 1f : 0f, 0f, hear);
        }

        return new PeerVolumes(targetDead ? 0f : 1f, 0f, 0f, 0f, false);
    }

    private PeerVolumes ComputeTasks(GameSnapshot snapshot, PlayerSnapshot me, PlayerSnapshot other, RuntimeSettings s)
    {
        bool localDead    = me.IsDead;
        bool targetDead   = other.IsDead;
        bool localImp     = me.IsImpostor;
        bool targetImp    = other.IsImpostor;
        bool targetInVent = other.InVent;
        bool localInVent  = me.InVent;

        if (s.OnlyMeetingOrLobby)
            return default;
        if (s.OnlyGhostsCanTalk && !localDead)
            return default;
        if (snapshot.CommsSabActive && s.CommsSabDisables && !localImp && !localDead)
            return default;

        var mePos     = me.Position;
        var targetPos = other.Position;
        float pan     = GetPan(mePos.x, targetPos.x);

        if (localDead)
        {
            if (!targetDead)
            {
                float d   = Vector2.Distance(mePos, targetPos);
                float vol = GetVolume(d, s.MaxDistance) * (s.CrewVolumeAsGhost / 100f);
                return new PeerVolumes(0f, vol, 0f, pan, false);
            }
            return new PeerVolumes(0f, 1f, 0f, 0f, false);
        }

        if (s.ImpostorPrivateRadio && localImp && targetImp && !targetDead)
            return new PeerVolumes(0f, 0f, 1f, 0f, true);

        if (s.ImpostorRadioOnly && localImp)
        {
            bool hear = targetImp && !targetDead;
            return new PeerVolumes(0f, 0f, hear ? 1f : 0f, 0f, hear);
        }

        if (localImp && targetDead && s.GhostVolumeAsImpostor > 0f)
        {
            float d   = Vector2.Distance(mePos, targetPos);
            float vol = GetVolume(d, s.MaxDistance) * (s.GhostVolumeAsImpostor / 100f);
            return new PeerVolumes(0f, vol, 0f, pan, false);
        }

        if (targetDead)
            return default;

        if (targetInVent)
        {
            if (!s.HearInVent) return default;
            if (s.VentPrivateChat && !localInVent) return default;
        }
        else if (s.VentPrivateChat && localInVent)
        {
            return default;
        }

        float dist   = Vector2.Distance(mePos, targetPos);
        float volume = GetVolume(dist, s.MaxDistance);

        if (volume <= 0f)
        {
            if (s.CameraCanHear && snapshot.ActiveCameraIndex >= 0)
            {
                volume = GetCameraVolume(snapshot, other, s.MaxDistance);
                if (volume <= 0f) return default;
                pan = GetCameraPan(snapshot, other);
            }
            else return default;
        }
        else
        {
            if (s.OnlyHearInSight)
            {
                bool inSight = !Physics2D.Linecast(mePos, targetPos, LayerMask.GetMask("Shadow"));
                if (!inSight) return default;
            }

            if (s.WallsBlockSound)
            {
                _wallCoeffs.TryGetValue(other.ClientId, out var prev);
                bool hasWall = Physics2D.Linecast(mePos, targetPos, LayerMask.GetMask("Shadow"));
                float coeff = prev + ((hasWall ? 0f : 1f) - prev) * Mathf.Clamp(Time.deltaTime * 4f, 0f, 1f);
                _wallCoeffs[other.ClientId] = coeff;
                volume *= coeff;
            }
            else
            {
                _wallCoeffs[other.ClientId] = 1f;
            }
        }

        return new PeerVolumes(Mathf.Clamp01(volume), 0f, 0f, pan, false);
    }

    private static float GetCameraVolume(GameSnapshot snapshot, PlayerSnapshot other, float maxDistance)
    {
        if (!AmongUsMaps.Maps.TryGetValue(snapshot.Map, out var mapData) || mapData == null)
            return 0f;

        var camLoc = (CameraLocation)snapshot.ActiveCameraIndex;

        if (camLoc != CameraLocation.Skeld)
        {
            if (!mapData.Cameras.TryGetValue(snapshot.ActiveCameraIndex, out var cam))
                return 0f;
            return GetVolume(Vector2.Distance(other.Position, cam.ToVector2()), maxDistance);
        }

        float best = 0f;
        foreach (var cam in mapData.Cameras.Values)
        {
            float v = GetVolume(Vector2.Distance(other.Position, cam.ToVector2()), maxDistance);
            if (v > best) best = v;
        }
        return best;
    }

    private static float GetCameraPan(GameSnapshot snapshot, PlayerSnapshot other)
    {
        if (!AmongUsMaps.Maps.TryGetValue(snapshot.Map, out var mapData) || mapData == null)
            return 0f;

        var camLoc = (CameraLocation)snapshot.ActiveCameraIndex;

        if (camLoc != CameraLocation.Skeld)
        {
            if (!mapData.Cameras.TryGetValue(snapshot.ActiveCameraIndex, out var cam))
                return 0f;
            return GetPan(cam.X, other.Position.x);
        }

        float bestDist = float.MaxValue;
        var bestCam = new CameraEntry(0f, 0f);
        foreach (var cam in mapData.Cameras.Values)
        {
            float d = Vector2.Distance(other.Position, cam.ToVector2());
            if (d < bestDist) { bestDist = d; bestCam = cam; }
        }
        return GetPan(bestCam.X, other.Position.x);
    }

    private static float GetVolume(float dist, float maxDist)
        => Mathf.Clamp01(1f - dist / maxDist);

    private static float GetPan(float micX, float spkX)
        => Mathf.Clamp((spkX - micX) / 3f, -1f, 1f);
}
