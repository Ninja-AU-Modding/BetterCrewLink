using HarmonyLib;
using UnityEngine;
using BetterCrewLink.Networking;

namespace BetterCrewLink.Patches;

[HarmonyPatch]
public static class VoiceManagerPatches
{
    [HarmonyPatch(typeof(SurveillanceMinigame), nameof(SurveillanceMinigame.Update))]
    [HarmonyPostfix]
    public static void SurveillanceMinigame_Update(SurveillanceMinigame __instance)
    {
        if (__instance == null || !__instance.isActiveAndEnabled)
        {
            VoiceClient.ClearActiveCamera();
            return;
        }

        TrySetCamera(__instance);
    }

    [HarmonyPatch(typeof(PlanetSurveillanceMinigame), nameof(PlanetSurveillanceMinigame.Update))]
    [HarmonyPostfix]
    public static void PlanetSurveillanceMinigame_Update(PlanetSurveillanceMinigame __instance)
    {
        if (__instance == null || !__instance.isActiveAndEnabled)
        {
            VoiceClient.ClearActiveCamera();
            return;
        }

        TrySetCamera(__instance);
    }

    private static void TrySetCamera(object instance)
    {
        var type  = instance.GetType();
        var field = AccessTools.Field(type, "currentCamera")
                 ?? AccessTools.Field(type, "currentCam")
                 ?? AccessTools.Field(type, "camNumber");

        if (field == null)
        {
            VoiceClient.ClearActiveCamera();
            return;
        }

        var value = field.GetValue(instance);
        if (value is int camInt)
            VoiceClient.SetActiveCamera(camInt);
        else if (value is byte camByte)
            VoiceClient.SetActiveCamera(camByte);
        else
            VoiceClient.ClearActiveCamera();
    }
}
