using HarmonyLib;
using Reactor.Utilities.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace BetterCrewLink;

[HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
public static class MainMenuLobbyBrowserPatch
{
    [HarmonyPostfix]
    public static void Postfix(MainMenuManager __instance)
    {
        if (__instance.newsButton == null)
            return;

        if (GameObject.Find("BCLLobbyBrowserButton") != null)
            return;

        var button = CloneMenuItem(__instance.newsButton, "BCLLobbyBrowserButton", new Vector2(0.815f, 0.52f), "Lobby Browser");
        var passive = button.GetComponent<PassiveButton>();
        passive.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        passive.OnClick.AddListener((UnityAction)(() =>
        {
            var menu = LobbyBrowserMenu.Ensure(__instance);
            menu.Toggle();
        }));

        var uiList = new Il2CppSystem.Collections.Generic.List<PassiveButton>();
        foreach (var ogButton in __instance.mainButtons)
            uiList.Add(ogButton);
        uiList.Add(button);
        __instance.mainButtons = uiList;
        __instance.SetUpControllerNav();
    }

    private static PassiveButton CloneMenuItem(PassiveButton source, string name, Vector2 pos, string label)
    {
        var obj = Object.Instantiate(source,
            GameObject.Find("Main Buttons").transform.Find("BottomButtonBounds").transform);
        obj.name = name;

        var positioner = obj.gameObject.AddComponent<AspectPosition>();
        positioner.Alignment = AspectPosition.EdgeAlignments.Center;
        positioner.anchorPoint = pos;
        positioner.updateAlways = true;

        var text = obj.transform.GetChild(0).GetChild(0).GetComponent<TextMeshPro>();
        text.GetComponent<TextTranslatorTMP>().Destroy();
        text.text = label;
        text.fontSize = 3f;
        text.fontSizeMin = 3f;
        text.fontSizeMax = 3f;

        obj.GetComponent<NewsCountButton>().DestroyImmediate();
        obj.transform.GetChild(3).gameObject.DestroyImmediate();
        return obj;
    }
}
