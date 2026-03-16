using BetterCrewLink.Networking;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BetterCrewLink.Patches;

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Update))]
public static class MeetingSpeakingIndicatorPatch
{
    private const float SpeakingThreshold = 0.01f;
    private static readonly Dictionary<byte, TextMeshPro> Indicators = new();

    [HarmonyPostfix]
    public static void Postfix(MeetingHud __instance)
    {
        if (__instance.playerStates == null) { HideAll(); return; }

        var speaking = new HashSet<byte>();

        // Remote players
        foreach (var pc in PlayerControl.AllPlayerControls.ToArray())
        {
            if (pc == null || pc.Data == null) continue;
            if (VoiceClient.GetClientTalkingLevel(pc.Data.ClientId) > SpeakingThreshold)
                speaking.Add(pc.PlayerId);
        }

        // Local player
        if (PlayerControl.LocalPlayer &&
            VoiceClient.LastLocalMicRms > SpeakingThreshold &&
            PlayerControl.LocalPlayer.PlayerId != byte.MaxValue)
        {
            speaking.Add(PlayerControl.LocalPlayer.PlayerId);
        }

        var alive = new HashSet<byte>();
        foreach (var state in __instance.playerStates)
        {
            if (state == null) continue;
            alive.Add(state.TargetPlayerId);
            var ind = GetOrCreate(state);
            ind.gameObject.SetActive(speaking.Contains(state.TargetPlayerId));
        }

        CleanStale(alive);
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.OnDestroy))]
    private static class DestroyPatch
    {
        private static void Postfix()
        {
            foreach (var v in Indicators.Values)
                if (v != null) Object.Destroy(v.gameObject);
            Indicators.Clear();
        }
    }

    private static TextMeshPro GetOrCreate(PlayerVoteArea state)
    {
        if (Indicators.TryGetValue(state.TargetPlayerId, out var ex) && ex != null)
            return ex;

        var template = state.NameText;
        TextMeshPro tmp;

        if (template == null)
        {
            var go = new GameObject("BCL_SpeakingIndicator");
            go.transform.SetParent(state.transform, false);
            tmp           = go.AddComponent<TextMeshPro>();
            tmp.fontSize  = 2f;
            tmp.color     = Color.green;
            tmp.alignment = TextAlignmentOptions.Center;
            go.transform.localPosition = new Vector3(-0.52f, 0.21f, -1f);
        }
        else
        {
            var go = Object.Instantiate(template.gameObject, state.transform);
            tmp = go.GetComponent<TextMeshPro>();
            tmp.name               = "BCL_SpeakingIndicator";
            tmp.color              = Color.green;
            tmp.alignment          = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.fontSize           = template.fontSize * 0.9f;
            tmp.transform.localPosition = new Vector3(-0.52f, 0.21f, -1f);
            tmp.transform.localScale    = template.transform.localScale * 0.8f;
        }

        tmp.text = "Speaking";
        tmp.gameObject.SetActive(false);
        Indicators[state.TargetPlayerId] = tmp;
        return tmp;
    }

    private static void HideAll()
    {
        foreach (var v in Indicators.Values)
            if (v != null) v.gameObject.SetActive(false);
    }

    private static void CleanStale(HashSet<byte> alive)
    {
        var remove = new List<byte>();
        foreach (var kv in Indicators)
        {
            if (alive.Contains(kv.Key)) continue;
            if (kv.Value != null) Object.Destroy(kv.Value.gameObject);
            remove.Add(kv.Key);
        }
        foreach (var k in remove) Indicators.Remove(k);
    }
}
