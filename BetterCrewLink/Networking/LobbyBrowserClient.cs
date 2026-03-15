using BetterCrewLink.Utils;
using SocketIOClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BetterCrewLink.Networking;

public sealed class LobbyBrowserClient
{
    public static LobbyBrowserClient Instance { get; } = new();

    private readonly ConcurrentQueue<Action> _mainThreadActions = new();
    private readonly Dictionary<int, PublicLobby> _lobbies = new();
    private readonly Dictionary<int, string> _lobbyCodes = new();
    private SocketIO? _socket;
    private string _currentServer = string.Empty;

    public event Action? LobbiesChanged;
    public event Action<string>? StatusMessage;
    public event Action<int>? LobbyCodeUpdated;

    public IReadOnlyList<PublicLobby> SnapshotLobbies()
    {
        lock (_lobbies)
            return _lobbies.Values.ToList();
    }

    public bool TryGetLobbyCode(int lobbyId, out string code)
    {
        lock (_lobbies)
            return _lobbyCodes.TryGetValue(lobbyId, out code);
    }

    public void Tick(RuntimeSettings settings)
    {
        while (_mainThreadActions.TryDequeue(out var action))
            action();

        if (_socket == null || _currentServer != settings.ServerUrl)
            Connect(settings.ServerUrl);
    }

    public void RequestLobbyCode(int lobbyId)
    {
        if (_socket == null || !_socket.Connected)
        {
            EnqueueStatus("Lobby browser not connected.");
            return;
        }

        _socket.EmitAsync("join_lobby", new object[] { lobbyId }, async response =>
        {
            var state = response.GetValue<int>(0);
            var codeOrError = response.GetValue<string>(1);
            var server = response.GetValue<string>(2);

            if (state == 0)
            {
                _mainThreadActions.Enqueue(() =>
                {
                    lock (_lobbies)
                        _lobbyCodes[lobbyId] = codeOrError;
                    LobbyCodeUpdated?.Invoke(lobbyId);
                });
                EnqueueStatus($"Code: {codeOrError} | Region: {server}");
            }
            else
                EnqueueStatus($"Error: {codeOrError}");
            await Task.CompletedTask;
        });
    }

    private void Connect(string serverUrl)
    {
        Disconnect();

        _currentServer = serverUrl;
        var options = new SocketIOOptions
        {
            Reconnection = true,
            ReconnectionAttempts = int.MaxValue
        };
        ForceEngineIoV3(options);
        _socket = new SocketIO(new Uri(serverUrl), options);

        _socket.OnConnected += async (_, _) =>
        {
            await _socket.EmitAsync("lobbybrowser", new object[] { true });
            EnqueueStatus("Lobby browser connected.");
        };

        _socket.OnReconnectAttempt += (_, attempt) =>
        {
            EnqueueStatus($"Lobby browser reconnecting ({attempt})...");
        };

        _socket.OnError += (_, error) =>
        {
            EnqueueStatus($"Lobby browser error: {error}");
        };

        _socket.OnDisconnected += (_, _) =>
        {
            _mainThreadActions.Enqueue(() =>
            {
                lock (_lobbies)
                {
                    _lobbies.Clear();
                    _lobbyCodes.Clear();
                }
                LobbiesChanged?.Invoke();
            });
        };

        _socket.On("update_lobby", async ctx =>
        {
            var lobby = ctx.GetValue<PublicLobby>(0);
            _mainThreadActions.Enqueue(() =>
            {
                lock (_lobbies)
                    _lobbies[lobby.Id] = lobby;
                LobbiesChanged?.Invoke();
            });
            await Task.CompletedTask;
        });

        _socket.On("new_lobbies", async ctx =>
        {
            var lobbies = ctx.GetValue<PublicLobby[]>(0);
            _mainThreadActions.Enqueue(() =>
            {
                lock (_lobbies)
                {
                    foreach (var lobby in lobbies)
                        _lobbies[lobby.Id] = lobby;
                }
                LobbiesChanged?.Invoke();
            });
            await Task.CompletedTask;
        });

        _socket.On("remove_lobby", async ctx =>
        {
            var lobbyId = ctx.GetValue<int>(0);
            _mainThreadActions.Enqueue(() =>
            {
                lock (_lobbies)
                    _lobbies.Remove(lobbyId);
                LobbiesChanged?.Invoke();
            });
            await Task.CompletedTask;
        });

        _socket.ConnectAsync();
    }

    private static void ForceEngineIoV3(SocketIOOptions options)
    {
        var prop = options.GetType().GetProperty("EIO");
        if (prop == null || !prop.CanWrite)
            return;

        var value = 3;
        if (prop.PropertyType.IsEnum)
            prop.SetValue(options, Enum.ToObject(prop.PropertyType, value));
        else
            prop.SetValue(options, Convert.ChangeType(value, prop.PropertyType));
    }

    private void Disconnect()
    {
        _socket?.EmitAsync("lobbybrowser", new object[] { false });
        _socket?.DisconnectAsync();
        _socket = null;
        _currentServer = string.Empty;
        lock (_lobbies)
        {
            _lobbies.Clear();
            _lobbyCodes.Clear();
        }
    }

    private void EnqueueStatus(string message)
    {
        _mainThreadActions.Enqueue(() => StatusMessage?.Invoke(message));
    }
}

public sealed class PublicLobby
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; set; } = string.Empty;

    [JsonPropertyName("current_players")]
    public int CurrentPlayers { get; set; }

    [JsonPropertyName("max_players")]
    public int MaxPlayers { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("mods")]
    public string Mods { get; set; } = string.Empty;

    [JsonPropertyName("isPublic")]
    public bool IsPublic { get; set; }

    [JsonPropertyName("server")]
    public string Server { get; set; } = string.Empty;

    [JsonPropertyName("gameState")]
    public int GameState { get; set; }

    [JsonPropertyName("stateTime")]
    public long StateTime { get; set; }
}
