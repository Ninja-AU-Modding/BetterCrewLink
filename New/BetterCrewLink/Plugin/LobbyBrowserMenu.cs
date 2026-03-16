using BetterCrewLink.Networking;
using HarmonyLib;
using InnerNet;
using Reactor.Utilities.Attributes;
using Reactor.Utilities.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace BetterCrewLink;

[RegisterInIl2Cpp]
public sealed class LobbyBrowserMenu : MonoBehaviour
{
    private readonly List<LobbyRow> _rows = [];
    private MainMenuManager? _menu;
    private GameObject? _panel;
    private TextMeshPro? _statusText;
    private float _lastRefresh;

    public LobbyBrowserMenu(IntPtr ptr) : base(ptr) { }

    public static LobbyBrowserMenu Ensure(MainMenuManager menu)
    {
        var existing = Object.FindObjectOfType<LobbyBrowserMenu>();
        if (existing != null)
            return existing;

        var root = new GameObject("BCL_LobbyBrowserMenu");
        Object.DontDestroyOnLoad(root);
        var menuObj = root.AddComponent<LobbyBrowserMenu>();
        menuObj.Build(menu);
        return menuObj;
    }

    public void Toggle()
    {
        if (_panel == null)
            return;

        var show = !_panel.activeSelf;
        _panel.SetActive(show);
        if (show)
            RefreshRows(true);
    }

    private void Update()
    {
        if (_panel == null || !_panel.activeSelf)
            return;

        if (Time.time - _lastRefresh < 1f)
            return;

        RefreshRows(false);
    }

    private void Build(MainMenuManager menu)
    {
        _menu = menu;
        _panel = new GameObject("Panel");
        _panel.transform.SetParent(menu.transform, false);
        _panel.transform.localPosition = new Vector3(0f, 0.2f, -5f);
        _panel.SetActive(false);

        var bgObj = new GameObject("Background");
        bgObj.transform.SetParent(_panel.transform, false);
        bgObj.transform.localPosition = Vector3.zero;
        bgObj.transform.localScale = new Vector3(6.2f, 4.2f, 1f);
        var bg = bgObj.AddComponent<SpriteRenderer>();
        bg.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        bg.color = new Color(0f, 0f, 0f, 0.85f);
        bg.sortingOrder = 200;

        var template = menu.newsButton.transform.GetChild(0).GetChild(0).GetComponent<TextMeshPro>();
        CreateHeader(template, "Title", new Vector3(-2.7f, 1.65f, -6f));
        CreateHeader(template, "Host", new Vector3(-1.2f, 1.65f, -6f));
        CreateHeader(template, "Players", new Vector3(-0.1f, 1.65f, -6f));
        CreateHeader(template, "Code", new Vector3(0.8f, 1.65f, -6f));

        _statusText = CreateHeader(template, "Select a lobby to show its code.", new Vector3(-2.7f, -1.85f, -6f));

        var closeBtn = CreateSmallButton(menu, _panel.transform, "Close", new Vector3(2.6f, 1.7f, -6f));
        closeBtn.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        closeBtn.OnClick.AddListener((UnityAction)(() => _panel.SetActive(false)));
    }

    private void RefreshRows(bool forceRebuild)
    {
        _lastRefresh = Time.time;

        var lobbies = LobbyBrowserClient.Instance.SnapshotLobbies()
            .OrderByDescending(l => l.GameState == 0)
            .ThenByDescending(l => l.CurrentPlayers)
            .Take(8)
            .ToList();

        if (_menu == null)
            return;

        if (forceRebuild || _rows.Count != lobbies.Count)
        {
            foreach (var row in _rows)
                row.Destroy();
            _rows.Clear();

            for (var i = 0; i < lobbies.Count; i++)
            {
                var y = 1.2f - (i * 0.45f);
                var row = LobbyRow.Create(_panel!, _menu, y, lobbies[i], this);
                _rows.Add(row);
            }
        }
        else
        {
            for (var i = 0; i < _rows.Count; i++)
                _rows[i].Update(lobbies[i]);
        }

        if (_statusText != null && lobbies.Count == 0)
            _statusText.text = "No lobbies found yet.";
    }

    private TextMeshPro CreateHeader(TextMeshPro template, string text, Vector3 pos)
    {
        var labelObj = Object.Instantiate(template.gameObject, _panel!.transform);
        labelObj.transform.localPosition = pos;
        var tmp = labelObj.GetComponent<TextMeshPro>();
        tmp.GetComponent<TextTranslatorTMP>().Destroy();
        tmp.text = text;
        tmp.fontSize = 2.6f;
        tmp.alignment = TextAlignmentOptions.Left;
        return tmp;
    }

    private static PassiveButton CreateSmallButton(MainMenuManager menu, Transform parent, string label, Vector3 pos)
    {
        var btn = Object.Instantiate(menu.newsButton, parent);
        btn.name = $"BCL_{label}Button";
        btn.transform.localPosition = pos;
        btn.transform.localScale = new Vector3(0.6f, 0.6f, 1f);

        var aspect = btn.GetComponent<AspectPosition>();
        if (aspect != null)
            aspect.DestroyImmediate();

        var text = btn.transform.GetChild(0).GetChild(0).GetComponent<TextMeshPro>();
        text.GetComponent<TextTranslatorTMP>().Destroy();
        text.text = label;
        text.fontSize = 2.6f;

        btn.GetComponent<NewsCountButton>()?.DestroyImmediate();
        if (btn.transform.childCount > 3)
            btn.transform.GetChild(3).gameObject.DestroyImmediate();

        return btn;
    }

    private sealed class LobbyRow
    {
        private readonly GameObject _root;
        private readonly TextMeshPro _title;
        private readonly TextMeshPro _host;
        private readonly TextMeshPro _players;
        private readonly TextMeshPro _code;
        private readonly PassiveButton _show;
        private readonly PassiveButton _join;
        private int _lobbyId;

        private LobbyRow(GameObject root, TextMeshPro title, TextMeshPro host, TextMeshPro players, TextMeshPro code, PassiveButton show, PassiveButton join, int lobbyId)
        {
            _root = root;
            _title = title;
            _host = host;
            _players = players;
            _code = code;
            _show = show;
            _join = join;
            _lobbyId = lobbyId;
        }

        public static LobbyRow Create(GameObject panel, MainMenuManager menu, float y, PublicLobby lobby, LobbyBrowserMenu owner)
        {
            var root = new GameObject($"Lobby_{lobby.Id}");
            root.transform.SetParent(panel.transform, false);
            root.transform.localPosition = new Vector3(0f, y, -6f);

            var template = menu.newsButton.transform.GetChild(0).GetChild(0).GetComponent<TextMeshPro>();

            var title = CreateText(template, root.transform, lobby.Title, new Vector3(-2.7f, 0f, 0f));
            var host = CreateText(template, root.transform, lobby.Host, new Vector3(-1.2f, 0f, 0f));
            var players = CreateText(template, root.transform, $"{lobby.CurrentPlayers}/{lobby.MaxPlayers}", new Vector3(-0.1f, 0f, 0f));

            var codeText = LobbyBrowserClient.Instance.TryGetLobbyCode(lobby.Id, out var code) ? code : "-";
            var codeLabel = CreateText(template, root.transform, codeText, new Vector3(0.8f, 0f, 0f));

            var show = CreateRowButton(menu, root.transform, "Show", new Vector3(2.1f, 0f, 0f), () =>
            {
                LobbyBrowserClient.Instance.RequestLobbyCode(lobby.Id);
            });

            var join = CreateRowButton(menu, root.transform, "Join", new Vector3(2.9f, 0f, 0f), () =>
            {
                owner.TryJoinLobby(lobby.Id);
            });

            return new LobbyRow(root, title, host, players, codeLabel, show, join, lobby.Id);
        }

        public void Update(PublicLobby lobby)
        {
            _lobbyId = lobby.Id;
            _title.text = lobby.Title;
            _host.text = lobby.Host;
            _players.text = $"{lobby.CurrentPlayers}/{lobby.MaxPlayers}";
            _code.text = LobbyBrowserClient.Instance.TryGetLobbyCode(lobby.Id, out var code) ? code : "-";
        }

        public void Destroy()
        {
            if (_root != null)
                Object.Destroy(_root);
        }

        private static TextMeshPro CreateText(TextMeshPro template, Transform parent, string text, Vector3 pos)
        {
            var labelObj = Object.Instantiate(template.gameObject, parent);
            labelObj.transform.localPosition = pos;
            var tmp = labelObj.GetComponent<TextMeshPro>();
            tmp.GetComponent<TextTranslatorTMP>().Destroy();
            tmp.text = text;
            tmp.fontSize = 2.2f;
            tmp.alignment = TextAlignmentOptions.Left;
            return tmp;
        }

        private static PassiveButton CreateRowButton(MainMenuManager menu, Transform parent, string label, Vector3 pos, Action onClick)
        {
            var button = CreateSmallButton(menu, parent, label, pos);
            button.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            button.OnClick.AddListener((UnityAction)(() => onClick()));
            return button;
        }
    }

    private void TryJoinLobby(int lobbyId)
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
            // Ignore and leave the code available in the UI.
        }

        LobbyBrowserClient.Instance.RequestLobbyCode(lobbyId);
    }
}
