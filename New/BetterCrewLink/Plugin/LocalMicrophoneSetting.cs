using System;
using System.Linq;
using BepInEx.Configuration;
using MiraAPI.LocalSettings.SettingTypes;
using MiraAPI.Utilities;
using Reactor.Utilities.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace BetterCrewLink;

public sealed class LocalMicrophoneSetting : LocalSettingBase<string>
{
    public LocalMicrophoneSetting(Type tab, ConfigEntryBase configEntry, string? name = null, string? description = null)
        : base(tab, configEntry, name, description)
    {
    }

    public override GameObject CreateOption(ToggleButtonBehaviour toggle, SlideBar slider, Transform parent, ref float offset, ref int order, bool last)
    {
        var button = Object.Instantiate(toggle, parent).GetComponent<PassiveButton>();
        var tmp = button.transform.FindChild("Text_TMP").GetComponent<TextMeshPro>();
        var rollover = button.GetComponent<ButtonRolloverHandler>();
        tmp.GetComponent<TextTranslatorTMP>().Destroy();
        button.gameObject.SetActive(true);

        var toggleComp = button.GetComponent<ToggleButtonBehaviour>();
        var background = toggleComp.Background;
        var highlight = button.transform.FindChild("ButtonHighlight")?.GetComponent<SpriteRenderer>();
        if (highlight != null)
        {
            highlight.color = Tab!.TabAppearance.EnumHoverColor;
            highlight.gameObject.SetActive(false);
        }
        toggleComp.Destroy();

        if (last && order == 1)
        {
            button.transform.localPosition = new Vector3(0, 1.85f - offset, -7);
        }
        else
        {
            button.transform.localPosition = new Vector3(order == 1 ? -1.185f : 1.185f, 1.85f - offset, -7);
        }

        tmp.text = GetValueText();
        button.name = Name;
        button.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        rollover.OutColor = Tab!.TabAppearance.EnumColor;
        rollover.OverColor = Tab!.TabAppearance.EnumHoverColor;
        background.color = Tab!.TabAppearance.EnumColor;

        button.OnClick.AddListener((UnityAction)(() =>
        {
            var devices = GetDevices();
            if (devices.Length == 0)
                return;

            var current = GetValue();
            var idx = Array.IndexOf(devices, current);
            idx = (idx + 1) % devices.Length;
            SetValue(devices[idx]);
            tmp.text = GetValueText();
        }));
        button.OnMouseOver.AddListener((UnityAction)(() =>
        {
            if (!Description.IsNullOrWhiteSpace())
                tmp.text = Description;
            highlight?.gameObject.SetActive(true);
        }));
        button.OnMouseOut.AddListener((UnityAction)(() =>
        {
            tmp.text = GetValueText();
            highlight?.gameObject.SetActive(false);
        }));

        Helpers.DivideSize(button.gameObject, 1.1f);

        order++;
        if (order > 2 && !last)
        {
            offset += 0.5f;
            order = 1;
        }
        if (last)
            offset += 0.6f;

        return button.gameObject;
    }

    protected override string GetValueText()
    {
        var devices = GetDevices();
        var value = GetValue();
        if (devices.Length == 0)
            value = "Default";
        else if (!devices.Contains(value))
            value = devices[0];

        return $"<font=\"LiberationSans SDF\" material=\"LiberationSans SDF - Chat Message Masked\">{Name}: <b>{value}</font></b>";
    }

    private static string[] GetDevices()
    {
        var list = Microphone.devices ?? Array.Empty<string>();
        if (list.Length == 0)
            return ["Default"];

        return ["Default", .. list];
    }
}
