using HarmonyLib;
using UnityEngine;
using BetterCrewLink;

namespace BetterCrewLink.Patches;

[HarmonyPatch]
public static class VoiceManagerPatches
{
    // Camera detection for Skeld / MIRA HQ / Airship
    [HarmonyPatch(typeof(SurveillanceMinigame), nameof(SurveillanceMinigame.Update))]
    [HarmonyPostfix]
    public static void SurveillanceMinigame_Update(SurveillanceMinigame __instance)
    {
        if (__instance == null || !__instance.isActiveAndEnabled)
        {
            VoiceManager.ClearActiveCamera();
            return;
        }

        TrySetCamera(__instance);
    }

    // Camera detection for Polus
    [HarmonyPatch(typeof(PlanetSurveillanceMinigame), nameof(PlanetSurveillanceMinigame.Update))]
    [HarmonyPostfix]
    public static void PlanetSurveillanceMinigame_Update(PlanetSurveillanceMinigame __instance)
    {
        if (__instance == null || !__instance.isActiveAndEnabled)
        {
            VoiceManager.ClearActiveCamera();
            return;
        }

        TrySetCamera(__instance);
    }

    // Clear camera when any surveillance minigame closes
    [HarmonyPatch(typeof(Minigame), nameof(Minigame.Close))]
    [HarmonyPostfix]
    public static void Minigame_Close(Minigame __instance)
    {
        // Fix: removed non-existent SurvCameraMinigame check
        if (__instance is SurveillanceMinigame || __instance is PlanetSurveillanceMinigame)
        {
            VoiceManager.ClearActiveCamera();
        }
    }

    private static void TrySetCamera(object instance)
    {
        var type = instance.GetType();
        var field = AccessTools.Field(type, "currentCamera")
            ?? AccessTools.Field(type, "currentCam")
            ?? AccessTools.Field(type, "camNumber");

        if (field == null)
        {
            VoiceManager.ClearActiveCamera();
            return;
        }

        var value = field.GetValue(instance);
        if (value is int camInt)
        {
            VoiceManager.SetActiveCamera(camInt);
        }
        else if (value is byte camByte)
        {
            VoiceManager.SetActiveCamera(camByte);
        }
        else
        {
            VoiceManager.ClearActiveCamera();
        }
    }
}
