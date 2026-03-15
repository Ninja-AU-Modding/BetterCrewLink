using AmongUs.GameOptions;
using BetterCrewLink.Data;
using Concentus.Structs;
using InnerNet;
using Reactor.Utilities.Attributes;
using SocketIOClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Concentus.Enums;
using static UnityEngine.AudioClip;
using Object = UnityEngine.Object;

namespace BetterCrewLink;

[RegisterInIl2Cpp]
public sealed class VoiceManager(IntPtr cppPtr) : MonoBehaviour(cppPtr)
{

    public static VoiceManager? Instance { get; private set; }

    // Configuration
    public string ServerUrl = "https://bettercrewl.ink";
    public LobbySettings ActiveLobbySettings = new LobbySettings(
        MaxDistance: 5.32f,
        VisionHearing: false,
        Haunting: false,
        HearImpostorsInVents: false,
        ImpostorsHearImpostorsInVent: false,
        ImpostorRadioEnabled: false,
        CommsSabotage: false,
        DeadOnly: false,
        MeetingGhostOnly: false,
        HearThroughCameras: false,
        WallsBlockAudio: false,
        PublicLobbyOn: false,
        PublicLobbyTitle: "",
        PublicLobbyLanguage: "en"
    );

    public float MasterVolume = 100f;
    public float CrewVolumeAsGhost = 100f;
    public float GhostVolumeAsImpostor = 100f;
    public bool EnableSpatialAudio = true;

    // Audio constants
    private const int SampleRate = 48000;
    private const int Channels = 1;
    private const int FrameSizeMs = 20;
    private const int FrameSamples = SampleRate * FrameSizeMs / 1000;

    // State
    private SocketIOClient.SocketIO? _socket;
    private string _currentLobby = "MENU";
    private int _impostorRadioClientId = -1;
    private int _previousRadioClientId = -1;
    private string _pendingLobby = "MENU";
    private int _pendingPlayerId, _pendingClientId;
    private bool _pendingIsHost;
    private bool _muted;
    private bool _deafened;

    private readonly ConcurrentQueue<Action> _mainThreadActions = new();

    // Camera
    private static int _activeCameraIndex = -1;
    public static void SetActiveCamera(int index) => _activeCameraIndex = index;
    public static void ClearActiveCamera() => _activeCameraIndex = -1;

    public Dictionary<int, bool> OtherTalking { get; } = new();
    public Dictionary<int, bool> OtherDead { get; } = new();
    public Dictionary<string, Client> SocketClients { get; } = new();
    private readonly Dictionary<int, string> _playerSocketIds = new();

    private class PeerAudio : IDisposable
    {
        public OpusDecoder Decoder { get; } = new OpusDecoder(SampleRate, Channels);
        public AudioSource Source { get; set; } = null!;
        public AudioClip Clip { get; set; } = null!;
        public ConcurrentQueue<float> SampleQueue { get; } = new();
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
            Decoder?.Dispose();
        }
    }

    private readonly ConcurrentDictionary<string, PeerAudio> _peerAudio = new();

    private AudioClip? _micClip;
    private int _lastMicPos;
    private OpusEncoder _encoder = null!;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        _encoder = new OpusEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        _encoder.Bitrate = 32000;
        StartMicrophone();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Disconnect();
        StopMicrophone();
        _encoder?.Dispose();
    }

    private void Update()
    {
        while (_mainThreadActions.TryDequeue(out var action))
            action();

        UpdatePeerAudio();
    }

    private void StartMicrophone()
    {
        if (Microphone.devices.Length == 0)
        {
            BCLLogger.Error("No microphone found!");
            return;
        }
        _micClip = Microphone.Start(null, true, 1, SampleRate);
        _lastMicPos = 0;
    }

    private void StopMicrophone()
    {
        if (_micClip != null)
        {
            Microphone.End(null);
            Destroy(_micClip);
            _micClip = null;
        }
    }

    private void CaptureAndSendAudio()
    {
        if (_muted || _socket == null || !_socket.Connected || _micClip == null)
            return;

        int currentPos = Microphone.GetPosition(null);
        if (currentPos < _lastMicPos)
            currentPos += _micClip.samples;

        int samplesAvailable = currentPos - _lastMicPos;
        if (samplesAvailable < FrameSamples)
            return;

        float[] samples = new float[FrameSamples];
        _micClip.GetData(samples, _lastMicPos % _micClip.samples);

        short[] pcm = new short[FrameSamples];
        for (int i = 0; i < FrameSamples; i++)
        {
            float clamped = Mathf.Clamp(samples[i], -1f, 1f);
            pcm[i] = (short)(clamped * short.MaxValue);
        }

        byte[] opusPacket = new byte[1024];
        int encodedLength = _encoder.Encode(pcm, 0, FrameSamples, opusPacket, 0, opusPacket.Length);
        byte[] trimmedPacket = new byte[encodedLength];
        Array.Copy(opusPacket, trimmedPacket, encodedLength);

        _socket.EmitAsync("audio", new object[] { trimmedPacket });

        _lastMicPos = (_lastMicPos + FrameSamples) % _micClip.samples;
    }

    private void UpdatePeerAudio()
    {
        var localPlayer = PlayerControl.LocalPlayer;
        if (localPlayer == null || !ShipStatus.Instance) return;

        var gameState = BuildCurrentState();
        var me = gameState.Players.FirstOrDefault(p => p.IsLocal);
        if (me == null) return;

        foreach (var player in gameState.Players.Where(p => !p.IsLocal))
        {
            if (!_playerSocketIds.TryGetValue(player.ClientId, out var peerId)) continue;
            if (!_peerAudio.TryGetValue(peerId, out var peer)) continue;

            float gain = CalculateVoiceAudio(gameState, me, player);
            if (_deafened) gain = 0f;
            if (gain > 0f)
            {
                if (me.IsDead && !player.IsDead)
                    gain *= CrewVolumeAsGhost / 100f;
                gain *= MasterVolume / 100f;
            }

            peer.Source.volume = gain;
            peer.Source.maxDistance = ActiveLobbySettings.MaxDistance;

            if (EnableSpatialAudio && gain > 0f)
            {
                peer.Source.transform.localPosition = new Vector3(
                    player.X - me.X,
                    player.Y - me.Y,
                    -0.5f);
            }
            else
            {
                peer.Source.transform.localPosition = Vector3.zero;
            }
        }

        CaptureAndSendAudio();
    }

    public void Connect()
    {
        if (_socket != null) return;

        _socket = new SocketIO(new Uri(ServerUrl), new SocketIOOptions
        {
            Reconnection = true,
            ReconnectionAttempts = int.MaxValue
        });

        _socket.OnConnected += async (sender, e) =>
        {
            BCLLogger.Info("BetterCrewLink: connected to server");
            if (_pendingLobby != "MENU")
            {
                await _socket.EmitAsync("id", new object[] { _pendingPlayerId, _pendingClientId });
                await _socket.EmitAsync("join", new object[] { _pendingLobby, _pendingPlayerId, _pendingClientId, _pendingIsHost });
            }
        };

        _socket.OnDisconnected += (sender, e) =>
        {
            BCLLogger.Info("BetterCrewLink: disconnected from server");
            _mainThreadActions.Enqueue(() =>
            {
                foreach (var peer in _peerAudio.Keys.ToList())
                    RemovePeer(peer);
            });
        };

        _socket.On("setHost", async ctx => await Task.CompletedTask);

        _socket.On("setClient", async ctx =>
        {
            string socketId = ctx.GetValue<string>(0);
            Client client = ctx.GetValue<Client>(1);
            _mainThreadActions.Enqueue(() =>
            {
                SocketClients[socketId] = client;
                _playerSocketIds[client.ClientId] = socketId;
                EnsurePeerAudio(socketId);
            });
            await Task.CompletedTask;
        });

        _socket.On("setClients", async ctx =>
        {
            var clients = ctx.GetValue<Dictionary<string, Client>>(0);
            _mainThreadActions.Enqueue(() =>
            {
                foreach (var existing in SocketClients.Keys.ToList())
                {
                    if (!clients.ContainsKey(existing))
                        RemovePeer(existing);
                }
                foreach (var kv in clients)
                {
                    SocketClients[kv.Key] = kv.Value;
                    _playerSocketIds[kv.Value.ClientId] = kv.Key;
                    EnsurePeerAudio(kv.Key);
                }
            });
            await Task.CompletedTask;
        });

        _socket.On("VAD", async ctx =>
        {
            VadPayload data = ctx.GetValue<VadPayload>(0);
            _mainThreadActions.Enqueue(() =>
            {
                OtherTalking[data.Client.ClientId] = data.Activity;
            });
            await Task.CompletedTask;
        });

        _socket.On("join", async ctx =>
        {
            string peerId = ctx.GetValue<string>(0);
            Client client = ctx.GetValue<Client>(1);
            _mainThreadActions.Enqueue(() =>
            {
                SocketClients[peerId] = client;
                _playerSocketIds[client.ClientId] = peerId;
                EnsurePeerAudio(peerId);
            });
            await Task.CompletedTask;
        });

        _socket.On("leave", async ctx =>
        {
            string peerId = ctx.GetValue<string>(0);
            _mainThreadActions.Enqueue(() => RemovePeer(peerId));
            await Task.CompletedTask;
        });

        _socket.On("lobbySettings", async ctx =>
        {
            LobbySettings settings = ctx.GetValue<LobbySettings>(0);
            _mainThreadActions.Enqueue(() =>
            {
                ActiveLobbySettings = settings;
            });
            await Task.CompletedTask;
        });

        _socket.On("audio", async ctx =>
        {
            byte[] opusData = ctx.GetValue<byte[]>(0);
            string senderSocketId = ctx.GetValue<string>(1);

            _mainThreadActions.Enqueue(() =>
            {
                if (!_peerAudio.TryGetValue(senderSocketId, out var peer))
                    return;

                short[] pcm = new short[FrameSamples * 2];
                int decodedSamples = peer.Decoder.Decode(opusData, 0, opusData.Length, pcm, 0, pcm.Length);
                if (decodedSamples <= 0) return;

                for (int i = 0; i < decodedSamples; i++)
                {
                    float sample = pcm[i] / (float)short.MaxValue;
                    peer.SampleQueue.Enqueue(sample);
                }
            });
            await Task.CompletedTask;
        });

        _socket.ConnectAsync();
    }

    public void Disconnect()
    {
        _socket?.EmitAsync("leave");
        _socket?.DisconnectAsync();
        _socket = null;
        _currentLobby = "MENU";
        _pendingLobby = "MENU";
        _mainThreadActions.Enqueue(() =>
        {
            foreach (var peer in _peerAudio.Keys.ToList())
                RemovePeer(peer);
            SocketClients.Clear();
            _playerSocketIds.Clear();
        });
    }

    public async Task JoinLobby(string lobbyCode, int playerId, int clientId, bool isHost)
    {
        if (_socket == null) return;
        _pendingLobby = lobbyCode;
        _pendingPlayerId = playerId;
        _pendingClientId = clientId;
        _pendingIsHost = isHost;

        if (lobbyCode == "MENU")
        {
            await _socket.EmitAsync("leave");
            _mainThreadActions.Enqueue(() =>
            {
                foreach (var peer in _peerAudio.Keys.ToList())
                    RemovePeer(peer);
                SocketClients.Clear();
            });
            _currentLobby = lobbyCode;
            return;
        }

        if (_currentLobby == lobbyCode) return;

        await _socket.EmitAsync("leave");
        await _socket.EmitAsync("id", new object[] { playerId, clientId });
        await _socket.EmitAsync("join", new object[] { lobbyCode, playerId, clientId, isHost });
        _currentLobby = lobbyCode;
    }

    private void EnsurePeerAudio(string socketId)
    {
        if (_peerAudio.ContainsKey(socketId)) return;

        var go = new GameObject($"BCL_Peer_{socketId}");
        go.transform.SetParent(transform);
        var source = go.AddComponent<AudioSource>();
        source.spatialBlend = EnableSpatialAudio ? 1f : 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 0.1f;
        source.maxDistance = ActiveLobbySettings.MaxDistance;
        source.volume = 0f;
        source.loop = true;

        var clip = AudioClip.Create(
            $"PeerAudio_{socketId}",
            SampleRate * 3,
            Channels,
            SampleRate,
            true,
            (PCMReaderCallback)(pcmData => OnAudioRead(pcmData, socketId))
        );
        source.clip = clip;
        source.Play();

        _peerAudio[socketId] = new PeerAudio { Source = source, Clip = clip };
    }

    private void OnAudioRead(float[] data, string socketId)
    {
        if (!_peerAudio.TryGetValue(socketId, out var peer) || peer.Disposed)
        {
            Array.Clear(data, 0, data.Length);
            return;
        }

        int samplesNeeded = data.Length;
        int samplesCopied = 0;

        while (samplesCopied < samplesNeeded && peer.SampleQueue.TryDequeue(out float sample))
        {
            data[samplesCopied++] = sample;
        }

        if (samplesCopied < samplesNeeded)
            Array.Clear(data, samplesCopied, samplesNeeded - samplesCopied);
    }

    private void RemovePeer(string socketId)
    {
        if (!_peerAudio.TryRemove(socketId, out var peer)) return;
        peer.Source.Stop();
        Destroy(peer.Clip);
        Destroy(peer.Source.gameObject);
        peer.Dispose();

        var clientId = SocketClients.FirstOrDefault(kv => kv.Key == socketId).Value?.ClientId ?? -1;
        if (clientId != -1) _playerSocketIds.Remove(clientId);
        SocketClients.Remove(socketId);
    }

    public void ToggleMute()
    {
        _muted = !_muted;
        if (_deafened)
        {
            _deafened = false;
            _muted = false;
        }
    }

    public void ToggleDeafen()
    {
        _deafened = !_deafened;
    }

    public bool IsMuted => _muted;
    public bool IsDeafened => _deafened;

    public async Task EmitTalking(bool talking)
    {
        if (_socket != null)
            await _socket.EmitAsync("VAD", new object[] { talking });
    }

    public void SetImpostorRadioClientId(int clientId)
    {
        _previousRadioClientId = _impostorRadioClientId;
        _impostorRadioClientId = clientId;

        if (_previousRadioClientId != -1 && _previousRadioClientId != clientId)
        {
            if (_playerSocketIds.TryGetValue(_previousRadioClientId, out var prevPeerId) &&
                _peerAudio.TryGetValue(prevPeerId, out var prevPeer))
            {
                prevPeer.Source.pitch = 1f;
            }
        }
    }

    public float CalculateVoiceAudio(AmongUsState state, Player me, Player other)
    {
        if (other.Disconnected || other.IsDummy) return 0f;

        var panPos = new Vector2(other.X - me.X, other.Y - me.Y);
        float endGain;
        var collided = false;
        var skipDistanceCheck = false;
        var maxDistance = ActiveLobbySettings.MaxDistance;

        switch (state.GameState)
        {
            case GameState.Menu:
                return 0f;

            case GameState.Lobby:
                endGain = 1f;
                break;

            case GameState.Tasks:
                endGain = 1f;

                if (ActiveLobbySettings.MeetingGhostOnly)
                    endGain = 0f;

                if (!me.IsDead && ActiveLobbySettings.CommsSabotage && state.ComsSabotaged && !me.IsImpostor)
                    endGain = 0f;

                if (other.InVent &&
                    !(ActiveLobbySettings.HearImpostorsInVents ||
                      (ActiveLobbySettings.ImpostorsHearImpostorsInVent && me.InVent)))
                    endGain = 0f;

                if (ActiveLobbySettings.WallsBlockAudio && !me.IsDead)
                    collided = Physics2D.Linecast(
                        new Vector2(me.X, me.Y),
                        new Vector2(other.X, other.Y),
                        LayerMask.GetMask("Ship"));

                if (me.IsImpostor && other.IsImpostor &&
                    ActiveLobbySettings.ImpostorRadioEnabled &&
                    other.ClientId == _impostorRadioClientId)
                {
                    skipDistanceCheck = true;
                    ApplyRadioEffect(state, other);
                }

                if (!me.IsDead && other.IsDead && me.IsImpostor && ActiveLobbySettings.Haunting)
                {
                    collided = false;
                    endGain = GhostVolumeAsImpostor / 100f;
                }
                else if (other.IsDead && !me.IsDead)
                {
                    endGain = 0f;
                }
                break;

            case GameState.Discussion:
                panPos = Vector2.zero;
                endGain = 1f;
                if (!me.IsDead && other.IsDead)
                    endGain = 0f;
                break;

            default:
                return 0f;
        }

        if (ActiveLobbySettings.DeadOnly)
        {
            panPos = Vector2.zero;
            if (!me.IsDead || !other.IsDead)
                endGain = 0f;
        }

        if (!skipDistanceCheck)
        {
            var dist = panPos.magnitude;
            if (dist > maxDistance)
            {
                if (ActiveLobbySettings.HearThroughCameras && state.GameState == GameState.Tasks)
                {
                    panPos = GetCameraAdjustedPan(state, other, me, maxDistance, out var stillTooFar);
                    if (stillTooFar) return 0f;
                }
                else
                {
                    return 0f;
                }
            }
            else if (collided)
            {
                return 0f;
            }
        }

        var isOnCamera = state.CurrentCamera != CameraLocation.None;
        if ((me.InVent || other.InVent || isOnCamera) && state.GameState == GameState.Tasks)
        {
            if (endGain >= 1f)
                endGain = isOnCamera ? 0.8f : 0.5f;
        }

        return Mathf.Clamp01(endGain);
    }

    private Vector2 GetCameraAdjustedPan(
        AmongUsState state,
        Player other,
        Player me,
        float maxDistance,
        out bool stillTooFar)
    {
        stillTooFar = false;

        if (!AmongUsMaps.Maps.TryGetValue(state.Map, out var mapData) || mapData == null)
        {
            stillTooFar = true;
            return Vector2.zero;
        }

        if (state.CurrentCamera != CameraLocation.Skeld)
        {
            if (!mapData.Cameras.TryGetValue((int)state.CurrentCamera, out var cam))
            {
                stillTooFar = true;
                return Vector2.zero;
            }
            var pan = new Vector2(other.X - cam.X, other.Y - cam.Y);
            stillTooFar = pan.magnitude > maxDistance;
            return pan;
        }
        else
        {
            var bestDist = float.MaxValue;
            var bestPan = Vector2.zero;
            foreach (var cam in mapData.Cameras.Values)
            {
                var pan = new Vector2(other.X - cam.X, other.Y - cam.Y);
                if (pan.magnitude < bestDist)
                {
                    bestDist = pan.magnitude;
                    bestPan = pan;
                }
            }
            stillTooFar = bestDist > maxDistance;
            return bestPan;
        }
    }

    private void ApplyRadioEffect(AmongUsState state, Player other)
    {
        if (_playerSocketIds.TryGetValue(other.ClientId, out var peerId) &&
            _peerAudio.TryGetValue(peerId, out var peer))
        {
            peer.Source.pitch = 1.05f;
        }
    }

    private static AmongUsState BuildCurrentState()
    {
        var gameOptions = GameOptionsManager.Instance.CurrentGameOptions;
        var map = gameOptions != null ? (MapType)gameOptions.MapId : MapType.TheSkeld;
        var maxPlayers = gameOptions?.MaxPlayers ?? 10;

        var client = AmongUsClient.Instance;
        var localPlayer = PlayerControl.LocalPlayer;

        var gameState = client.GameState switch
        {
            InnerNetClient.GameStates.NotJoined => GameState.Menu,
            InnerNetClient.GameStates.Joined => GameState.Lobby,
            InnerNetClient.GameStates.Started => MeetingHud.Instance
                ? GameState.Discussion
                : GameState.Tasks,
            InnerNetClient.GameStates.Ended => GameState.Lobby,
            _ => GameState.Menu,
        };

        bool comsSabotaged = Utilities.IsCommsSabotaged();
        CameraLocation currentCamera = _activeCameraIndex >= 0 ? (CameraLocation)_activeCameraIndex : CameraLocation.None;

        float lightRadius = 1f;

        var players = PlayerControl.AllPlayerControls
            .ToArray()
            .Select(p => BuildPlayer(p, client.ClientId))
            .ToList();

        return new AmongUsState(
            GameState: gameState,
            OldGameState: gameState,
            LobbyCodeInt: client.GameId,
            LobbyCode: GameCode.IntToGameName(client.GameId),
            Players: players,
            IsHost: client.AmHost,
            ClientId: client.ClientId,
            HostId: client.HostId,
            ComsSabotaged: comsSabotaged,
            CurrentCamera: currentCamera,
            Map: map,
            LightRadius: lightRadius,
            LightRadiusChanged: false,
            ClosedDoors: new List<int>(),
            CurrentServer: client.networkAddress,
            MaxPlayers: maxPlayers,
            Mod: ModsType.None,
            OldMeetingHud: false
        );
    }

    private static Player BuildPlayer(PlayerControl p, int localClientId)
    {
        var data = p.Data;
        var pos = p.GetTruePosition();
        return new Player(
            Ptr: p.GetInstanceID(),
            Id: data.PlayerId,
            ClientId: data.ClientId,
            Name: data.PlayerName,
            NameHash: data.PlayerName.GetHashCode(),
            ColorId: data.DefaultOutfit.ColorId,
            HatId: data.DefaultOutfit.HatId,
            PetId: 0,
            SkinId: data.DefaultOutfit.SkinId,
            VisorId: data.DefaultOutfit.VisorId,
            Disconnected: data.Disconnected,
            IsImpostor: p.IsImpostor(),
            IsDead: data.IsDead,
            TaskPtr: 0,
            ObjectPtr: p.GetInstanceID(),
            IsLocal: data.ClientId == localClientId,
            ShiftedColor: -1,
            Bugged: false,
            X: pos.x,
            Y: pos.y,
            InVent: p.inVent,
            IsDummy: false
        );
    }

    private record VadPayload(bool Activity, Client Client);
}