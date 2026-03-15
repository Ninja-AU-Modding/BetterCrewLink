using BepInEx.Configuration;
using BetterCrewLink.Networking;
using HarmonyLib;
using InnerNet;
using MiraAPI.LocalSettings;
using MiraAPI.Utilities;
using MiraAPI.Patches.LocalSettings;
using Reactor.Utilities.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace BetterCrewLink;

public sealed class BetterCrewLinkLobbyBrowserTab(ConfigFile config) : LocalSettingsTab(config)
{
    public override string TabName => "Lobby Browser";
    protected override bool ShouldCreateLabels => false;

    private readonly List<LobbyRow> _rows = [];
    private Scroller? _scroller;
    private Transform? _content;
    private TextMeshPro? _statusText;
    private bool _subscribed;
    private GameObject? _headerTemplate;
    private ToggleButtonBehaviour? _buttonTemplate;
    private BoxCollider2D? _clickMask;

    public override GameObject CreateTab(OptionsMenuBehaviour instance)
    {
        var tab = Object.Instantiate(instance.transform.FindChild("GeneralTab").gameObject, instance.transform);
        tab.name = $"{TabName}Tab";
        tab.transform.DestroyChildren();
        tab.gameObject.SetActive(false);

        _clickMask = GetMaskCollider(tab);
        _scroller = Helpers.CreateScroller(tab.transform, _clickMask);
        _content = _scroller.Inner;

        _headerTemplate = instance.transform.FindChild("GeneralTab")
            .FindChild("ControlGroup")
            .FindChild("ControlText_TMP")
            .gameObject;

        _buttonTemplate = instance.transform.FindChild("GeneralTab")
            .FindChild("ChatGroup")
            .FindChild("CensorChatButton")
            .GetComponent<ToggleButtonBehaviour>();

        BuildHeader();
        BuildStatus(instance);

        Subscribe();
        RefreshRows();

        return tab;
    }

    private void Subscribe()
    {
        if (_subscribed)
            return;

        _subscribed = true;
        LobbyBrowserClient.Instance.LobbiesChanged += RefreshRows;
        LobbyBrowserClient.Instance.StatusMessage += OnStatusMessage;
        LobbyBrowserClient.Instance.LobbyCodeUpdated += _ => RefreshRows();
    }

    private void OnStatusMessage(string message)
    {
        if (_statusText != null)
            _statusText.text = message;
    }

    private void BuildHeader()
    {
        if (_content == null)
            return;

        if (_headerTemplate == null)
            return;

        CreateText(_headerTemplate, _content, "Title", new Vector3(-2.7f, 1.9f, -7), TextAlignmentOptions.Left);
        CreateText(_headerTemplate, _content, "Host", new Vector3(-1.2f, 1.9f, -7), TextAlignmentOptions.Left);
        CreateText(_headerTemplate, _content, "Players", new Vector3(-0.1f, 1.9f, -7), TextAlignmentOptions.Left);
        CreateText(_headerTemplate, _content, "Code", new Vector3(0.8f, 1.9f, -7), TextAlignmentOptions.Left);
        CreateText(_headerTemplate, _content, "Status", new Vector3(1.6f, 1.9f, -7), TextAlignmentOptions.Left);
    }

    private void BuildStatus(OptionsMenuBehaviour instance)
    {
        if (_content == null)
            return;

        var headerTemplate = instance.transform.FindChild("GeneralTab")
            .FindChild("ControlGroup")
            .FindChild("ControlText_TMP")
            .gameObject;

        _statusText = CreateText(headerTemplate, _content, "Select a lobby to show its code.", new Vector3(-2.7f, -2.2f, -7), TextAlignmentOptions.Left);
    }

    private void RefreshRows()
    {
        if (_content == null)
            return;

        foreach (var row in _rows)
            row.Destroy();
        _rows.Clear();

        var lobbies = LobbyBrowserClient.Instance.SnapshotLobbies()
            .OrderByDescending(l => l.GameState == 0)
            .ThenByDescending(l => l.CurrentPlayers)
            .ToList();

        if (_headerTemplate == null || _buttonTemplate == null || _clickMask == null)
            return;

        var rowTemplate = GetRowTemplate();
        var buttonTemplate = _buttonTemplate;

        var rowHeight = 0.45f;
        var offset = 1.45f;
        foreach (var lobby in lobbies)
        {
            var rowY = offset;
            offset -= rowHeight;
            var row = LobbyRow.Create(_content, rowTemplate, _headerTemplate, buttonTemplate, _clickMask, lobby, rowY);
            _rows.Add(row);
        }

        ApplyScrollerBounds(lobbies.Count);
    }

    private void ApplyScrollerBounds(int count)
    {
        if (_scroller == null)
            return;

        var min = Mathf.Max(0f, (count * 0.45f) - 2.5f);
        _scroller.SetBounds(new FloatRange(0, min), new FloatRange(0, 0));
    }

    private GameObject GetRowTemplate()
    {
        return new GameObject("LobbyRow");
    }

    private static BoxCollider2D GetMaskCollider(GameObject tab)
    {
        var field = typeof(OptionsMenuPatches).GetField("MaskCollider", AccessTools.all);
        if (field?.GetValue(null) is BoxCollider2D collider && collider != null)
            return collider;

        var fallback = tab.AddComponent<BoxCollider2D>();
        fallback.size = new Vector2(6f, 4f);
        fallback.isTrigger = true;
        return fallback;
    }

    private static TextMeshPro CreateText(GameObject template, Transform parent, string text, Vector3 localPos, TextAlignmentOptions alignment)
    {
        var label = Object.Instantiate(template, parent);
        label.name = text;
        label.transform.localPosition = localPos;
        label.GetComponent<TextTranslatorTMP>().Destroy();

        var tmp = label.GetComponent<TextMeshPro>();
        tmp.text = text;
        tmp.alignment = alignment;

        var meshRend = label.GetComponent<MeshRenderer>();
        meshRend.sortingLayerName = "Default";
        meshRend.sortingOrder = 151;

        return tmp;
    }

    private sealed class LobbyRow
    {
        private readonly GameObject _root;
        private readonly TextMeshPro _title;
        private readonly TextMeshPro _host;
        private readonly TextMeshPro _players;
        private readonly TextMeshPro _code;
        private readonly TextMeshPro _status;
        private readonly PassiveButton _showButton;
        private readonly PassiveButton _connectButton;
        private readonly SpriteRenderer _buttonBackground;
        private readonly SpriteRenderer _connectBackground;

        private LobbyRow(GameObject root, TextMeshPro title, TextMeshPro host, TextMeshPro players, TextMeshPro code, TextMeshPro status, PassiveButton showButton, SpriteRenderer buttonBackground, PassiveButton connectButton, SpriteRenderer connectBackground)
        {
            _root = root;
            _title = title;
            _host = host;
            _players = players;
            _code = code;
            _status = status;
            _showButton = showButton;
            _buttonBackground = buttonBackground;
            _connectButton = connectButton;
            _connectBackground = connectBackground;
        }

        public static LobbyRow Create(Transform parent, GameObject rowTemplate, GameObject headerTemplate, ToggleButtonBehaviour buttonTemplate, BoxCollider2D clickMask, PublicLobby lobby, float y)
        {
            var root = Object.Instantiate(rowTemplate, parent);
            root.name = $"Lobby_{lobby.Id}";
            root.transform.localPosition = new Vector3(0, y, -7);

            var title = CreateRowText(headerTemplate, root.transform, lobby.Title, new Vector3(-2.7f, 0, 0));
            var host = CreateRowText(headerTemplate, root.transform, lobby.Host, new Vector3(-1.2f, 0, 0));
            var players = CreateRowText(headerTemplate, root.transform, $"{lobby.CurrentPlayers}/{lobby.MaxPlayers}", new Vector3(-0.1f, 0, 0));
            var codeText = LobbyBrowserClient.Instance.TryGetLobbyCode(lobby.Id, out var code) ? code : "-";
            var codeLabel = CreateRowText(headerTemplate, root.transform, codeText, new Vector3(0.8f, 0, 0));

            var statusText = lobby.GameState == 0 ? "Lobby" : "In Game";
            if (lobby.StateTime > 0)
            {
                var elapsed = TimeSpan.FromMilliseconds(Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - lobby.StateTime));
                statusText = $"{statusText} {elapsed.Minutes:00}:{elapsed.Seconds:00}";
            }
            var status = CreateRowText(headerTemplate, root.transform, statusText, new Vector3(1.6f, 0, 0));

            var button = Object.Instantiate(buttonTemplate, root.transform).GetComponent<PassiveButton>();
            button.gameObject.SetActive(true);
            button.transform.localPosition = new Vector3(2.55f, 0, 0);
            button.transform.localScale = new Vector3(0.5f, 0.5f, 1);

            var tmp = button.transform.FindChild("Text_TMP").GetComponent<TextMeshPro>();
            tmp.GetComponent<TextTranslatorTMP>().Destroy();
            tmp.text = "Show";

            var rollover = button.GetComponent<ButtonRolloverHandler>();
            var toggleComp = button.GetComponent<ToggleButtonBehaviour>();
            var background = toggleComp.Background;
            button.transform.FindChild("ButtonHighlight")?.gameObject.DestroyImmediate();
            toggleComp.Destroy();

            rollover.Target = background;
            rollover.OutColor = Palette.DisabledGrey;
            rollover.OverColor = Palette.AcceptedGreen;
            background.color = Palette.DisabledGrey;

            var canShow = lobby.GameState == 0 && lobby.CurrentPlayers < lobby.MaxPlayers;
            button.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            if (canShow)
            {
                button.OnClick.AddListener((UnityAction)(() => LobbyBrowserClient.Instance.RequestLobbyCode(lobby.Id)));
                background.color = new Color(0.3f, 0.3f, 0.3f);
                rollover.OutColor = new Color(0.3f, 0.3f, 0.3f);
            }

            button.ClickMask = clickMask;

            var connect = Object.Instantiate(buttonTemplate, root.transform).GetComponent<PassiveButton>();
            connect.gameObject.SetActive(true);
            connect.transform.localPosition = new Vector3(3.35f, 0, 0);
            connect.transform.localScale = new Vector3(0.5f, 0.5f, 1);

            var connectTmp = connect.transform.FindChild("Text_TMP").GetComponent<TextMeshPro>();
            connectTmp.GetComponent<TextTranslatorTMP>().Destroy();
            connectTmp.text = "Join";

            var connectRollover = connect.GetComponent<ButtonRolloverHandler>();
            var connectToggle = connect.GetComponent<ToggleButtonBehaviour>();
            var connectBg = connectToggle.Background;
            connect.transform.FindChild("ButtonHighlight")?.gameObject.DestroyImmediate();
            connectToggle.Destroy();

            connectRollover.Target = connectBg;
            connectRollover.OutColor = Palette.DisabledGrey;
            connectRollover.OverColor = Palette.AcceptedGreen;
            connectBg.color = Palette.DisabledGrey;

            connect.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            if (canShow)
            {
                connect.OnClick.AddListener((UnityAction)(() => TryJoinLobby(lobby.Id)));
                connectBg.color = new Color(0.3f, 0.3f, 0.3f);
                connectRollover.OutColor = new Color(0.3f, 0.3f, 0.3f);
            }

            connect.ClickMask = clickMask;

            return new LobbyRow(root, title, host, players, codeLabel, status, button, background, connect, connectBg);
        }

        public void Destroy()
        {
            if (_root != null)
                Object.Destroy(_root);
        }

        private static TextMeshPro CreateRowText(GameObject template, Transform parent, string text, Vector3 localPos)
        {
            var label = Object.Instantiate(template, parent);
            label.transform.localPosition = localPos;
            label.GetComponent<TextTranslatorTMP>().Destroy();

            var tmp = label.GetComponent<TextMeshPro>();
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.fontSize = 1.5f;

            var meshRend = label.GetComponent<MeshRenderer>();
            meshRend.sortingLayerName = "Default";
            meshRend.sortingOrder = 151;

            return tmp;
        }
    }

    private static void TryJoinLobby(int lobbyId)
    {
        if (!LobbyBrowserClient.Instance.TryGetLobbyCode(lobbyId, out var code))
        {
            LobbyBrowserClient.Instance.RequestLobbyCode(lobbyId);
            return;
        }

        try
        {
            var client = AmongUsClient.Instance;
            if (client == null)
                return;

            var type = client.GetType();
            var methods = type.GetMethods();
            foreach (var method in methods)
            {
                if (!string.Equals(method.Name, "JoinGame", StringComparison.OrdinalIgnoreCase))
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                {
                    method.Invoke(client, new object[] { code });
                    return;
                }

                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
                {
                    var gameId = GameCode.GameNameToInt(code);
                    method.Invoke(client, new object[] { gameId });
                    return;
                }
            }
        }
        catch
        {
            // Fall back to showing code in status message.
        }

        LobbyBrowserClient.Instance.RequestLobbyCode(lobbyId);
    }
}
