using BetterCrewLink.Networking;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BetterCrewLink.Patches;

[HarmonyPatch(typeof(PingTracker), nameof(PingTracker.Update))]
public static class PingTrackerSpeakingPatch
{
    private const float SpeakingThreshold = 0.01f;

    private static readonly AspectPosition.EdgeAlignments Anchor   = AspectPosition.EdgeAlignments.LeftTop;
    private static readonly Vector3                       EdgeDist  = new(0.5f, 0.11f, 0f);

    private static PingTracker?    _tracker;
    private static AspectPosition? _aspect;

    [HarmonyPostfix]
    public static void Postfix(PingTracker __instance)
    {
        if (__instance?.text == null)
            return;

        if (_tracker == null || _tracker.gameObject == null)
            CreateTracker(__instance);

        if (_tracker?.text == null)
            return;

        var room = VoiceChatRoom.TryGet();  // returns null when no game running

        if (room == null)
        {
            _tracker.gameObject.SetActive(false);
            return;
        }

        // Who is speaking
        var speakers     = new List<string>();
        var vcPlayerIds  = new HashSet<byte>();

        foreach (var pc in PlayerControl.AllPlayerControls.ToArray())
        {
            if (pc == null || pc.Data == null) continue;

            bool hasVc = IsConnected(pc);
            if (hasVc) vcPlayerIds.Add(pc.PlayerId);

            if (VoiceClient.GetClientTalkingLevel(pc.Data.ClientId) > SpeakingThreshold)
                speakers.Add(pc.Data.PlayerName);
        }

        // Local player
        if (PlayerControl.LocalPlayer && VoiceClient.LastLocalMicRms > SpeakingThreshold)
            speakers.Add(PlayerControl.LocalPlayer.Data?.PlayerName ?? PlayerControl.LocalPlayer.name);

        // Who is missing VC
        var localId     = PlayerControl.LocalPlayer ? PlayerControl.LocalPlayer.PlayerId : byte.MaxValue;
        var noVcPlayers = new List<string>();
        foreach (var pc in PlayerControl.AllPlayerControls.ToArray())
        {
            if (pc == null || pc.Data == null) continue;
            if (pc.PlayerId == localId) continue;
            if (!vcPlayerIds.Contains(pc.PlayerId))
                noVcPlayers.Add(pc.Data.PlayerName);
        }

        var speakingText = speakers.Count > 0
            ? $"<color=#00FF00FF>Speaking: {string.Join(", ", speakers.Distinct())}</color>"
            : string.Empty;

        var noVcNames  = noVcPlayers.Count > 0 ? string.Join(", ", noVcPlayers.Distinct()) : "-";
        var missingText = $"<color=#FFD35AFF>No BCL: {noVcNames}</color>";

        _tracker.gameObject.SetActive(true);
        _tracker.text.text = string.IsNullOrEmpty(speakingText)
            ? missingText
            : speakingText + "\n" + missingText;

        if (_aspect != null)
        {
            _aspect.Alignment        = Anchor;
            _aspect.DistanceFromEdge = EdgeDist;
            _aspect.AdjustPosition();
        }
    }

    private static void CreateTracker(PingTracker template)
    {
        var go = Object.Instantiate(template.gameObject, template.transform.parent);
        go.name      = "BCL_SpeakingTracker";
        go.SetActive(true);

        _tracker = go.GetComponent<PingTracker>();
        if (_tracker?.text != null)
        {
            _tracker.text.gameObject.SetActive(true);
            _tracker.text.alignment        = TextAlignmentOptions.TopLeft;
            _tracker.text.enableWordWrapping = false;
        }

        _aspect = go.GetComponent<AspectPosition>() ?? go.AddComponent<AspectPosition>();
        _aspect.Alignment        = Anchor;
        _aspect.DistanceFromEdge = EdgeDist;
        _aspect.AdjustPosition();
    }

    // A player "has BCL" if we have a socket entry for their clientId.
    // For a more accurate check in a future update,
    // use a public set from VoiceClient, but i can't be bothered.
    private static bool IsConnected(PlayerControl pc)
    {
        if (pc.Data == null) return false;
        // If BCL has ever received a packet from this client, their level entry exists (even if 0).
        // Any non-local player whose clientId appears in the relay's known clients.
        return VoiceClient.GetClientTalkingLevel(pc.Data.ClientId) >= 0f
               && IsKnownToRelay(pc.Data.ClientId);
    }

    // Expose from VoiceClient: public static bool IsClientKnown(int clientId)
    // For now, treat everyone as "connected" and just show speakers.
    // Replace with VoiceClient.IsClientKnown once that method is added.
    private static bool IsKnownToRelay(int clientId) => true;
}

// Thin shim so PingTrackerSpeakingPatch compiles without a VoiceChatRoom dependency.
// Remove this if we add real room tracking; the null return disables the HUD tracker
file static class VoiceChatRoom
{
    public static object? TryGet()
        => AmongUsClient.Instance?.GameState == InnerNet.InnerNetClient.GameStates.Started ||
           AmongUsClient.Instance?.GameState == InnerNet.InnerNetClient.GameStates.Joined
            ? new object()
            : null;
}
