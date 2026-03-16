using BetterCrewLink;
using BetterCrewLink.Data;
using BetterCrewLink.GameHooks;
using BetterCrewLink.Utils;
using BetterCrewLink.Voice;
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

public sealed class VoiceClient
{
    public static float LastLocalMicRms  { get; private set; }
    public static bool  LastLocalTalking { get; private set; }

    // Camera state, PlayerTracker and VoiceManagerPatches
    private static int _activeCameraIndex = -1;
    public static int  ActiveCameraIndex => _activeCameraIndex;
    public static void SetActiveCamera(int index)  => _activeCameraIndex = index;
    public static void ClearActiveCamera()         => _activeCameraIndex = -1;

    // Impostor radio keybind toggle
    public static bool ImpostorRadioOnly { get; private set; }
    public void ToggleImpostorRadio() => ImpostorRadioOnly = !ImpostorRadioOnly;

    private const int SampleRate   = 48000;
    private const int Channels     = 1;
    private const int FrameSizeMs  = 20;
    private const int FrameSamples = SampleRate * FrameSizeMs / 1000;

    private readonly ConcurrentQueue<Action>           _mainThreadActions = new();
    private readonly ConcurrentDictionary<string, PeerAudio> _peers       = new();
    private readonly Dictionary<int, string>           _playerSocketIds   = new();
    private readonly Dictionary<string, int>           _socketToClientId  = new();

    // Peer talking level clientId -> RMS updated per decoded packet, with timestamp
    private static readonly Dictionary<int, float> _peerTalkingLevels    = new();
    private static readonly Dictionary<int, float> _peerTalkingTimestamps = new();
    private const float TalkingDecaySeconds = 0.15f;

    public static float GetClientTalkingLevel(int clientId)
    {
        if (!_peerTalkingLevels.TryGetValue(clientId, out var level)) return 0f;
        if (!_peerTalkingTimestamps.TryGetValue(clientId, out var t)) return 0f;
        return Time.time - t > TalkingDecaySeconds ? 0f : level;
    }

    private SocketIO?    _socket;
    private string       _currentServer = string.Empty;
    private string       _currentLobby  = "MENU";

    private AudioClip?   _micClip;
    private int          _lastMicPos;
    private OpusEncoder? _encoder;
    private string       _activeMicDevice = string.Empty;

    private bool _muted;
    private bool _deafened;
    private bool _lastVadTalking;
    private bool _lastVadSent;

    private GameObject? _root;
    private PeerAudio?  _loopback;

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
            _deafened = false;
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

        if (_socket == null || _currentServer != settings.ServerUrl)
            Connect(settings.ServerUrl);

        if (snapshot == null || snapshot.Phase == GamePhase.Menu)
        {
            if (_currentLobby != "MENU")
                _ = JoinLobby("MENU", 0, 0, false);

            CaptureAndSendAudio(settings, false);
            return;
        }

        var lobby = string.IsNullOrWhiteSpace(snapshot.LobbyCode) ? "MENU" : snapshot.LobbyCode;
        if (_currentLobby != lobby)
            _ = JoinLobby(lobby, snapshot.PlayerId, snapshot.ClientId, snapshot.IsHost);

        var wantsTransmit = ComputeTransmitState(settings);
        CaptureAndSendAudio(settings, wantsTransmit);
    }

    public void ApplyVolumes(Dictionary<int, PeerVolumes> volumes, GameSnapshot snapshot, RuntimeSettings settings)
    {
        var me = snapshot.LocalPlayer;

        foreach (var other in snapshot.Players)
        {
            if (other.IsLocal) continue;

            if (!_playerSocketIds.TryGetValue(other.ClientId, out var socketId)) continue;
            if (!_peers.TryGetValue(socketId, out var peer)) continue;

            var pv = volumes.TryGetValue(other.ClientId, out var v) ? v : default;

            float masterScale = settings.MasterVolume / 100f;
            float finalVol =
                pv.NormalVolume * masterScale +
                pv.GhostVolume  * masterScale +
                pv.RadioVolume  * masterScale;

            if (_deafened)
                finalVol = 0f;

            peer.Source.volume = Mathf.Clamp01(finalVol);
            peer.Source.pitch  = pv.RadioEffect ? 1.05f : 1f;

            if (settings.EnableSpatialAudio && finalVol > 0f)
            {
                peer.Source.spatialBlend = 1f;
                peer.Source.maxDistance  = settings.MaxDistance;
                peer.Source.transform.localPosition = new Vector3(
                    other.Position.x - me.Position.x,
                    other.Position.y - me.Position.y,
                    -0.5f
                );
            }
            else
            {
                peer.Source.spatialBlend = 0f;
                peer.Source.panStereo    = pv.Pan;
                peer.Source.transform.localPosition = Vector3.zero;
            }
        }

        if (_loopback != null)
            _loopback.Source.volume = Mathf.Clamp01(settings.MasterVolume / 100f);
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
        var device  = settings.MicrophoneDevice;
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

        _micClip         = Microphone.Start(string.IsNullOrWhiteSpace(desired) ? null : desired, true, 1, SampleRate);
        _lastMicPos      = 0;
        _activeMicDevice = desired;

        EnsureLoopback();
    }

    private void StopMicrophone()
    {
        if (_micClip == null)
            return;

        Microphone.End(null);
        Object.Destroy(_micClip);
        _micClip         = null;
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

        if (currentPos - _lastMicPos < FrameSamples)
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

        rms              = Mathf.Sqrt(rms / FrameSamples);
        LastLocalMicRms  = rms;
        _lastVadTalking  = rms >= settings.MicSensitivity;

        var talking      = wantsTransmit && _lastVadTalking;
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

        var opusPacket    = new byte[1024];
        var encodedLength = _encoder.Encode(pcm, 0, FrameSamples, opusPacket, 0, opusPacket.Length);
        var trimmed       = new byte[encodedLength];
        Array.Copy(opusPacket, trimmed, encodedLength);

        _socket.EmitAsync("audio", new object[] { trimmed });
        _lastMicPos = (_lastMicPos + FrameSamples) % _micClip.samples;
    }

    private void EmitVad(bool talking)
    {
        if (_socket == null || talking == _lastVadSent)
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
            Reconnection        = true,
            ReconnectionAttempts = int.MaxValue
        };
        options.EIO = SocketIOClient.Common.EngineIO.V3;
        _socket = new SocketIO(new Uri(serverUrl), options);

        _socket.OnConnected += async (_, _) =>
        {
            if (_currentLobby != "MENU")
            {
                await _socket.EmitAsync("id",   new object[] { 0, 0 });
                await _socket.EmitAsync("join", new object[] { _currentLobby, 0, 0, false });
            }
        };

        _socket.OnDisconnected += (_, _) => { _mainThreadActions.Enqueue(ClearPeers); };

        _socket.On("setClient", async ctx =>
        {
            var socketId = ctx.GetValue<string>(0);
            var client   = ctx.GetValue<Client>(1);
            _mainThreadActions.Enqueue(() =>
            {
                _playerSocketIds[client.ClientId] = socketId;
                _socketToClientId[socketId]       = client.ClientId;
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
                    if (!clients.Values.Any(c => c.ClientId == existing))
                        _playerSocketIds.Remove(existing);

                foreach (var kv in clients)
                {
                    _playerSocketIds[kv.Value.ClientId] = kv.Key;
                    _socketToClientId[kv.Key]           = kv.Value.ClientId;
                    EnsurePeer(kv.Key);
                }
            });
            await Task.CompletedTask;
        });

        _socket.On("join", async ctx =>
        {
            var socketId = ctx.GetValue<string>(0);
            var client   = ctx.GetValue<Client>(1);
            _mainThreadActions.Enqueue(() =>
            {
                _playerSocketIds[client.ClientId] = socketId;
                _socketToClientId[socketId]       = client.ClientId;
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
            var opusData       = ctx.GetValue<byte[]>(0);
            var senderSocketId = ctx.GetValue<string>(1);

            _mainThreadActions.Enqueue(() =>
            {
                if (!_peers.TryGetValue(senderSocketId, out var peer))
                    return;

                var pcm     = new short[FrameSamples * 2];
                var decoded = peer.Decoder.Decode(opusData, 0, opusData.Length, pcm, 0, pcm.Length);
                if (decoded <= 0)
                    return;

                if (_socketToClientId.TryGetValue(senderSocketId, out var clientId))
                {
                    var rmsSum = 0f;
                    for (var i = 0; i < decoded; i++)
                    {
                        var s = pcm[i] / (float)short.MaxValue;
                        rmsSum += s * s;
                    }
                    _peerTalkingLevels[clientId]     = Mathf.Sqrt(rmsSum / decoded);
                    _peerTalkingTimestamps[clientId] = Time.time;
                }

                for (var i = 0; i < decoded; i++)
                    peer.SampleQueue.Enqueue(pcm[i] / (float)short.MaxValue);
            });
            await Task.CompletedTask;
        });

        _socket.ConnectAsync();
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
        await _socket.EmitAsync("id",   new object[] { playerId, clientId });
        await _socket.EmitAsync("join", new object[] { lobbyCode, playerId, clientId, isHost });
    }

    private void EnsurePeer(string socketId)
    {
        if (_peers.ContainsKey(socketId))
            return;

        Initialize();

        var go = new GameObject($"BCL_Peer_{socketId}");
        go.transform.SetParent(_root!.transform);

        var source             = go.AddComponent<AudioSource>();
        source.spatialBlend    = 1f;
        source.rolloffMode     = AudioRolloffMode.Linear;
        source.minDistance     = 0.1f;
        source.maxDistance     = 5f;
        source.volume          = 0f;
        source.loop            = true;

        var clip = AudioClip.Create($"BCL_PeerClip_{socketId}", SampleRate, Channels, SampleRate, false);
        clip.SetData(new float[SampleRate], 0);

        source.clip = clip;
        source.Play();

        var peer   = new PeerAudio(source, clip);
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

        var source          = go.AddComponent<AudioSource>();
        source.spatialBlend = 0f;
        source.rolloffMode  = AudioRolloffMode.Linear;
        source.minDistance  = 0.1f;
        source.maxDistance  = 5f;
        source.volume       = 1f;
        source.loop         = true;

        var clip = AudioClip.Create("BCL_LoopbackClip", SampleRate, Channels, SampleRate, false);
        clip.SetData(new float[SampleRate], 0);

        source.clip = clip;
        source.Play();

        var peer   = new PeerAudio(source, clip);
        var filter = go.AddComponent<PeerAudioFilter>();
        filter.SampleQueue = peer.SampleQueue;

        _loopback = peer;
    }

    private void EnqueueLoopback(float[] samples)
    {
        if (_loopback == null)
            return;

        foreach (var s in samples)
            _loopback.SampleQueue.Enqueue(s);
    }

    private void RemovePeer(string socketId)
    {
        if (!_peers.TryRemove(socketId, out var peer))
            return;

        if (_socketToClientId.TryGetValue(socketId, out var clientId))
        {
            _peerTalkingLevels.Remove(clientId);
            _peerTalkingTimestamps.Remove(clientId);
            _socketToClientId.Remove(socketId);
        }

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
        _socketToClientId.Clear();
        _peerTalkingLevels.Clear();
        _peerTalkingTimestamps.Clear();
    }

    private void Disconnect()
    {
        _socket?.EmitAsync("leave");
        _socket?.DisconnectAsync();
        _socket        = null;
        _currentLobby  = "MENU";

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
        public OpusDecoder              Decoder     { get; } = new(SampleRate, Channels);
        public AudioSource              Source      { get; }
        public AudioClip                Clip        { get; }
        public ConcurrentQueue<float>   SampleQueue { get; } = new();
        public bool                     Disposed    { get; private set; }

        public PeerAudio(AudioSource source, AudioClip clip)
        {
            Source = source;
            Clip   = clip;
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
        var idx    = 0;

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
