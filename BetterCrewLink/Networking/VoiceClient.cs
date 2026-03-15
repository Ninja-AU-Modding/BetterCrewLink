using BetterCrewLink;
using BetterCrewLink.Data;
using BetterCrewLink.GameHooks;
using BetterCrewLink.Utils;
using Concentus.Enums;
using Concentus.Structs;
using Reactor.Utilities.Attributes;
using SocketIOClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BetterCrewLink.Networking;

// Handles voice relay networking, mic capture, and remote playback. UPDATED
public sealed class VoiceClient
{
    public static float LastLocalMicRms { get; private set; }
    public static bool LastLocalTalking { get; private set; }
    private const int SampleRate = 48000;
    private const int Channels = 1;
    private const int FrameSizeMs = 20;
    private const int FrameSamples = SampleRate * FrameSizeMs / 1000;

    private readonly ConcurrentQueue<Action> _mainThreadActions = new();
    private readonly ConcurrentDictionary<string, PeerAudio> _peers = new();
    private readonly Dictionary<int, string> _playerSocketIds = new();

    private SocketIO? _socket;
    private string _currentServer = string.Empty;
    private string _currentLobby = "MENU";

    private AudioClip? _micClip;
    private int _lastMicPos;
    private OpusEncoder? _encoder;
    private string _activeMicDevice = string.Empty;

    private bool _muted;
    private bool _deafened;
    private bool _lastVadTalking;
    private bool _lastVadSent;

    private GameObject? _root;
    private PeerAudio? _loopback;

    public void Initialize()
    {
        if (_root != null)
            return;

        _root = new GameObject("BCL_Voice");
        Object.DontDestroyOnLoad(_root);

        _encoder = new OpusEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP)
        {
            Bitrate = 32000
        };
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

    public void Tick(GameSnapshot? snapshot, RuntimeSettings settings)
    {
        while (_mainThreadActions.TryDequeue(out var action))
            action();

        Initialize();

        // Connect or re-connect when the server changes.
        if (_socket == null || _currentServer != settings.ServerUrl)
        {
            Connect(settings.ServerUrl);
        }

        if (snapshot == null || snapshot.Phase == GamePhase.Menu)
        {
            if (_currentLobby != "MENU")
                _ = JoinLobby("MENU", 0, 0, false);

            CaptureAndSendAudio(settings, false);
            return;
        }

        var lobby = string.IsNullOrWhiteSpace(snapshot.LobbyCode) ? "MENU" : snapshot.LobbyCode;
        if (_currentLobby != lobby)
        {
            _ = JoinLobby(lobby, snapshot.PlayerId, snapshot.ClientId, snapshot.IsHost);
        }

        var wantsTransmit = ComputeTransmitState(settings);
        CaptureAndSendAudio(settings, wantsTransmit);
    }

    public void ApplyVolumes(Dictionary<int, float> volumes, GameSnapshot snapshot, RuntimeSettings settings)
    {
        var me = snapshot.LocalPlayer;

        foreach (var other in snapshot.Players)
        {
            if (other.IsLocal)
                continue;

            if (!_playerSocketIds.TryGetValue(other.ClientId, out var socketId))
                continue;

            if (!_peers.TryGetValue(socketId, out var peer))
                continue;

            var gain = volumes.TryGetValue(other.ClientId, out var value) ? value : 0f;
            if (_deafened)
                gain = 0f;

            peer.Source.volume = gain;
            peer.Source.maxDistance = settings.MaxDistance;
            peer.Source.spatialBlend = settings.EnableSpatialAudio ? 1f : 0f;

            if (settings.EnableSpatialAudio && gain > 0f)
            {
                peer.Source.transform.localPosition = new Vector3(
                    other.Position.x - me.Position.x,
                    other.Position.y - me.Position.y,
                    -0.5f
                );
            }
            else
            {
                peer.Source.transform.localPosition = Vector3.zero;
            }
        }

        if (_loopback != null)
        {
            _loopback.Source.volume = Mathf.Clamp01(settings.MasterVolume / 100f);
        }
    }

    private bool ComputeTransmitState(RuntimeSettings settings)
    {
        var pushToTalkHeld = settings.PushToTalkEnabled && BetterCrewLinkKeybinds.IsHeld(BetterCrewLinkKeybinds.PushToTalk);
        var pushToMuteHeld = settings.PushToMuteEnabled && BetterCrewLinkKeybinds.IsHeld(BetterCrewLinkKeybinds.PushToMute);

        var wantsTransmit = settings.VoiceActivityEnabled ? _lastVadTalking : true;

        if (settings.PushToTalkEnabled)
            wantsTransmit &= pushToTalkHeld;

        if (settings.PushToMuteEnabled)
            wantsTransmit &= !pushToMuteHeld;

        if (_muted)
            wantsTransmit = false;

        return wantsTransmit;
    }

    private void StartMicrophone(RuntimeSettings settings)
    {
        var device = settings.MicrophoneDevice;
        var desired = string.IsNullOrWhiteSpace(device) || device == "Default" ? string.Empty : device;

        if (_micClip != null && _activeMicDevice == desired)
            return;

        if (_micClip != null)
            StopMicrophone();

        if (Microphone.devices.Length == 0)
        {
            BCLLogger.Error("BetterCrewLink: no microphone devices found.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(desired) && !Microphone.devices.Contains(desired))
        {
            BCLLogger.Info($"BetterCrewLink: microphone '{desired}' not found, using default.");
            desired = string.Empty;
        }

        _micClip = Microphone.Start(string.IsNullOrWhiteSpace(desired) ? null : desired, true, 1, SampleRate);
        _lastMicPos = 0;
        _activeMicDevice = desired;

        EnsureLoopback();
    }

    private void StopMicrophone()
    {
        if (_micClip == null)
            return;

        Microphone.End(null);
        Object.Destroy(_micClip);
        _micClip = null;
        _activeMicDevice = string.Empty;
    }

    private void CaptureAndSendAudio(RuntimeSettings settings, bool wantsTransmit)
    {
        StartMicrophone(settings);
        if (_micClip == null || _encoder == null)
            return;

        var currentPos = Microphone.GetPosition(null);
        if (currentPos < _lastMicPos)
            currentPos += _micClip.samples;

        var samplesAvailable = currentPos - _lastMicPos;
        if (samplesAvailable < FrameSamples)
            return;

        var samples = new float[FrameSamples];
        _micClip.GetData(samples, _lastMicPos % _micClip.samples);

        var rms = 0f;
        for (var i = 0; i < FrameSamples; i++)
        {
            var scaled = samples[i] * (settings.MicrophoneVolume / 100f);
            samples[i] = Mathf.Clamp(scaled, -1f, 1f);
            rms += samples[i] * samples[i];
        }

        rms = Mathf.Sqrt(rms / FrameSamples);
        LastLocalMicRms = rms;
        _lastVadTalking = rms >= settings.MicSensitivity;

        var talking = wantsTransmit && _lastVadTalking;
        LastLocalTalking = talking;
        EmitVad(talking);

        if (settings.TestRelay)
            EnqueueLoopback(samples);

        if (!wantsTransmit || _socket == null || !_socket.Connected)
        {
            _lastMicPos = (_lastMicPos + FrameSamples) % _micClip.samples;
            return;
        }

        var pcm = new short[FrameSamples];
        for (var i = 0; i < FrameSamples; i++)
            pcm[i] = (short)(samples[i] * short.MaxValue);

        var opusPacket = new byte[1024];
        var encodedLength = _encoder.Encode(pcm, 0, FrameSamples, opusPacket, 0, opusPacket.Length);
        var trimmed = new byte[encodedLength];
        Array.Copy(opusPacket, trimmed, encodedLength);

        _socket.EmitAsync("audio", new object[] { trimmed });
        _lastMicPos = (_lastMicPos + FrameSamples) % _micClip.samples;
    }

    private void EmitVad(bool talking)
    {
        if (_socket == null)
            return;

        if (talking == _lastVadSent)
            return;

        _lastVadSent = talking;
        _socket.EmitAsync("VAD", new object[] { talking });
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
            if (_currentLobby != "MENU")
            {
                await _socket.EmitAsync("id", new object[] { 0, 0 });
                await _socket.EmitAsync("join", new object[] { _currentLobby, 0, 0, false });
            }
        };

        _socket.OnDisconnected += (_, _) =>
        {
            _mainThreadActions.Enqueue(ClearPeers);
        };

        _socket.On("setClient", async ctx =>
        {
            var socketId = ctx.GetValue<string>(0);
            var client = ctx.GetValue<Client>(1);
            _mainThreadActions.Enqueue(() =>
            {
                _playerSocketIds[client.ClientId] = socketId;
                EnsurePeer(socketId);
            });
            await Task.CompletedTask;
        });

        _socket.On("setClients", async ctx =>
        {
            var clients = ctx.GetValue<Dictionary<string, Client>>(0);
            _mainThreadActions.Enqueue(() =>
            {
                foreach (var existing in _playerSocketIds.Keys.ToList())
                {
                    if (!clients.Values.Any(c => c.ClientId == existing))
                        _playerSocketIds.Remove(existing);
                }

                foreach (var kv in clients)
                {
                    _playerSocketIds[kv.Value.ClientId] = kv.Key;
                    EnsurePeer(kv.Key);
                }
            });
            await Task.CompletedTask;
        });

        _socket.On("join", async ctx =>
        {
            var socketId = ctx.GetValue<string>(0);
            var client = ctx.GetValue<Client>(1);
            _mainThreadActions.Enqueue(() =>
            {
                _playerSocketIds[client.ClientId] = socketId;
                EnsurePeer(socketId);
            });
            await Task.CompletedTask;
        });

        _socket.On("leave", async ctx =>
        {
            var socketId = ctx.GetValue<string>(0);
            _mainThreadActions.Enqueue(() => RemovePeer(socketId));
            await Task.CompletedTask;
        });

        _socket.On("audio", async ctx =>
        {
            var opusData = ctx.GetValue<byte[]>(0);
            var senderSocketId = ctx.GetValue<string>(1);

            _mainThreadActions.Enqueue(() =>
            {
                if (!_peers.TryGetValue(senderSocketId, out var peer))
                    return;

                var pcm = new short[FrameSamples * 2];
                var decoded = peer.Decoder.Decode(opusData, 0, opusData.Length, pcm, 0, pcm.Length);
                if (decoded <= 0)
                    return;

                for (var i = 0; i < decoded; i++)
                    peer.SampleQueue.Enqueue(pcm[i] / (float)short.MaxValue);
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

    public async Task JoinLobby(string lobbyCode, int playerId, int clientId, bool isHost)
    {
        if (_socket == null)
            return;

        _currentLobby = lobbyCode;

        if (lobbyCode == "MENU")
        {
            await _socket.EmitAsync("leave");
            _mainThreadActions.Enqueue(ClearPeers);
            return;
        }

        await _socket.EmitAsync("leave");
        await _socket.EmitAsync("id", new object[] { playerId, clientId });
        await _socket.EmitAsync("join", new object[] { lobbyCode, playerId, clientId, isHost });
    }

    private void EnsurePeer(string socketId)
    {
        if (_peers.ContainsKey(socketId))
            return;

        Initialize();

        var go = new GameObject($"BCL_Peer_{socketId}");
        go.transform.SetParent(_root!.transform);

        var source = go.AddComponent<AudioSource>();
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 0.1f;
        source.maxDistance = 5f;
        source.volume = 0f;
        source.loop = true;

        var clip = AudioClip.Create(
            $"BCL_PeerClip_{socketId}",
            SampleRate,
            Channels,
            SampleRate,
            false
        );
        clip.SetData(new float[SampleRate], 0);

        source.clip = clip;
        source.Play();

        var peer = new PeerAudio(source, clip);
        var filter = go.AddComponent<PeerAudioFilter>();
        filter.SampleQueue = peer.SampleQueue;

        _peers[socketId] = peer;
    }

    private void EnsureLoopback()
    {
        if (_loopback != null)
            return;

        Initialize();

        var go = new GameObject("BCL_Loopback");
        go.transform.SetParent(_root!.transform);

        var source = go.AddComponent<AudioSource>();
        source.spatialBlend = 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 0.1f;
        source.maxDistance = 5f;
        source.volume = 1f;
        source.loop = true;

        var clip = AudioClip.Create(
            "BCL_LoopbackClip",
            SampleRate,
            Channels,
            SampleRate,
            false
        );
        clip.SetData(new float[SampleRate], 0);

        source.clip = clip;
        source.Play();

        var peer = new PeerAudio(source, clip);
        var filter = go.AddComponent<PeerAudioFilter>();
        filter.SampleQueue = peer.SampleQueue;

        _loopback = peer;
    }

    private void EnqueueLoopback(float[] samples)
    {
        if (_loopback == null)
            return;

        for (var i = 0; i < samples.Length; i++)
            _loopback.SampleQueue.Enqueue(samples[i]);
    }

    private void RemovePeer(string socketId)
    {
        if (!_peers.TryRemove(socketId, out var peer))
            return;

        peer.Source.Stop();
        Object.Destroy(peer.Clip);
        Object.Destroy(peer.Source.gameObject);
        peer.Dispose();
    }

    private void ClearPeers()
    {
        foreach (var socketId in _peers.Keys.ToList())
            RemovePeer(socketId);

        _playerSocketIds.Clear();
    }

    private void Disconnect()
    {
        _socket?.EmitAsync("leave");
        _socket?.DisconnectAsync();
        _socket = null;
        _currentLobby = "MENU";
        ClearPeers();
        StopMicrophone();
        if (_loopback != null)
        {
            _loopback.Source.Stop();
            Object.Destroy(_loopback.Clip);
            Object.Destroy(_loopback.Source.gameObject);
            _loopback.Dispose();
            _loopback = null;
        }
    }

    private sealed class PeerAudio : IDisposable
    {
        public OpusDecoder Decoder { get; } = new(SampleRate, Channels);
        public AudioSource Source { get; }
        public AudioClip Clip { get; }
        public ConcurrentQueue<float> SampleQueue { get; } = new();
        public bool Disposed { get; private set; }

        public PeerAudio(AudioSource source, AudioClip clip)
        {
            Source = source;
            Clip = clip;
        }

        public void Dispose()
        {
            Disposed = true;
            Decoder?.Dispose();
        }
    }
}

[RegisterInIl2Cpp]
public sealed class PeerAudioFilter : MonoBehaviour
{
    public ConcurrentQueue<float>? SampleQueue;

    public PeerAudioFilter(IntPtr ptr) : base(ptr) { }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (SampleQueue == null)
        {
            Array.Clear(data, 0, data.Length);
            return;
        }

        var frames = data.Length / Mathf.Max(1, channels);
        var idx = 0;

        for (var i = 0; i < frames; i++)
        {
            var sample = 0f;
            if (SampleQueue.TryDequeue(out var queued))
                sample = queued;

            for (var c = 0; c < channels; c++)
                data[idx++] = sample;
        }
    }
}
