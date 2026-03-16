using BetterCrewLink;
using BetterCrewLink.Data;
using BetterCrewLink.GameHooks;
using BetterCrewLink.Utils;
using BetterCrewLink.Voice;
using Concentus.Enums;
using Concentus.Structs;
using Reactor.Utilities.Attributes;
using SIPSorcery.Net;
using SocketIOClient;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BetterCrewLink.Networking;

public sealed class VoiceClient
{
    public static float LastLocalMicRms { get; private set; }
    public static bool LastLocalTalking { get; private set; }

    private static int _activeCameraIndex = -1;
    public static int ActiveCameraIndex => _activeCameraIndex;
    public static void SetActiveCamera(int index) => _activeCameraIndex = index;
    public static void ClearActiveCamera() => _activeCameraIndex = -1;

    public static bool ImpostorRadioOnly { get; private set; }
    public void ToggleImpostorRadio() => ImpostorRadioOnly = !ImpostorRadioOnly;

    private const int SampleRate = 48000;
    private const int Channels = 1;
    private const int FrameSizeMs = 20;
    private const int FrameSamples = SampleRate * FrameSizeMs / 1000;

    // Socket (signaling only)
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();
    private SocketIO? _socket;
    private string _currentServer = string.Empty;
    private string _currentLobby = "MENU";
    private string _mySocketId = string.Empty;

    // ICE servers (populated by clientPeerConfig)
    private List<RTCIceServer> _iceServers = new()
    {
        new RTCIceServer { urls = "stun:stun.l.google.com:19302" }
    };

    // WebRTC peers
    private readonly ConcurrentDictionary<string, WebRtcPeer> _peers = new();
    private readonly Dictionary<int, string> _clientToSocket = new();
    private readonly Dictionary<string, int> _socketToClient = new();

    // Talking levels: clientId → RMS, timestamp
    private static readonly Dictionary<int, float> _peerTalkingLevels = new();
    private static readonly Dictionary<int, float> _peerTalkingTimestamps = new();
    private const float TalkingDecaySeconds = 0.15f;

    public static float GetClientTalkingLevel(int clientId)
    {
        if (!_peerTalkingLevels.TryGetValue(clientId, out var level)) return 0f;
        if (!_peerTalkingTimestamps.TryGetValue(clientId, out var t)) return 0f;
        return Time.time - t > TalkingDecaySeconds ? 0f : level;
    }

    // Mic / Opus
    private AudioClip? _micClip;
    private int _lastMicPos;
    private OpusEncoder? _encoder;
    private string _activeMicDevice = string.Empty;

    private bool _muted;
    private bool _deafened;
    private bool _lastVadTalking;
    private bool _lastVadSent;

    // Loopback
    private GameObject? _root;
    private PeerAudio? _loopback;

    // Public lifecycle

    public void Initialize()
    {
        if (_root != null) return;

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
        if (_deafened) _deafened = false;
    }

    public void ToggleDeafen() => _deafened = !_deafened;

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

        CaptureAndSendAudio(settings, ComputeTransmitState(settings));
    }

    public void ApplyVolumes(Dictionary<int, PeerVolumes> volumes, GameSnapshot snapshot, RuntimeSettings settings)
    {
        var me = snapshot.LocalPlayer;

        foreach (var other in snapshot.Players)
        {
            if (other.IsLocal) continue;
            if (!_clientToSocket.TryGetValue(other.ClientId, out var socketId)) continue;
            if (!_peers.TryGetValue(socketId, out var peer)) continue;

            var pv = volumes.TryGetValue(other.ClientId, out var v) ? v : default;
            float masterScale = settings.MasterVolume / 100f;
            float finalVol = Mathf.Clamp01(
                (pv.NormalVolume + pv.GhostVolume + pv.RadioVolume) * masterScale);

            if (_deafened) finalVol = 0f;

            peer.Audio.Source.volume = finalVol;
            peer.Audio.Source.pitch = pv.RadioEffect ? 1.05f : 1f;

            if (settings.EnableSpatialAudio && finalVol > 0f)
            {
                peer.Audio.Source.spatialBlend = 1f;
                peer.Audio.Source.maxDistance = settings.MaxDistance;
                peer.Audio.Source.transform.localPosition = new Vector3(
                    other.Position.x - me.Position.x,
                    other.Position.y - me.Position.y,
                    -0.5f);
            }
            else
            {
                peer.Audio.Source.spatialBlend = 0f;
                peer.Audio.Source.panStereo = pv.Pan;
                peer.Audio.Source.transform.localPosition = Vector3.zero;
            }
        }

        if (_loopback != null)
            _loopback.Source.volume = Mathf.Clamp01(settings.MasterVolume / 100f);
    }

    // Signaling connection

    private void Connect(string serverUrl)
    {
        DisconnectSocket();

        _currentServer = serverUrl;
        _socket = new SocketIO(new Uri(serverUrl), new SocketIOOptions
        {
            Reconnection = true,
            ReconnectionAttempts = int.MaxValue,
            EIO = SocketIOClient.Common.EngineIO.V3,
        });

        _socket.OnConnected += async (_, _) =>
        {
            // Server echoes our socket ID back on connect in some BCL server versions.
            // It's derived it below via setClient. For now just rejoin if were in a lobby.
            if (_currentLobby != "MENU")
            {
                await _socket.EmitAsync("id", new object[] { 0, 0 });
                await _socket.EmitAsync("join", new object[] { _currentLobby, 0, 0, false });
            }
        };

        _socket.OnDisconnected += (_, _) => { _mainThreadActions.Enqueue(ClearPeers); };

        // Server tells us our own socket ID + client mapping.
        _socket.On("setClient", async ctx =>
        {
            var socketId = ctx.GetValue<string>(0);
            var client = ctx.GetValue<Client>(1);
            _mainThreadActions.Enqueue(() =>
            {
                _clientToSocket[client.ClientId] = socketId;
                _socketToClient[socketId] = client.ClientId;
                // If this is our own entry, store our socket ID.
                // (BCL server sends setClient for all peers including self)
            });
            await Task.CompletedTask;
        });

        _socket.On("setClients", async ctx =>
        {
            var clients = ctx.GetValue<Dictionary<string, Client>>(0);
            _mainThreadActions.Enqueue(() =>
            {
                // Remove stale
                foreach (var sid in _clientToSocket.Values.ToList())
                    if (!clients.ContainsKey(sid))
                        RemovePeer(sid);

                foreach (var kv in clients)
                {
                    _clientToSocket[kv.Value.ClientId] = kv.Key;
                    _socketToClient[kv.Key] = kv.Value.ClientId;
                    // Non-initiator for existing peers. They will send us an offer.
                    EnsurePeerAudio(kv.Key);
                }
            });
            await Task.CompletedTask;
        });

        // A new peer joined, so initiate to them.
        _socket.On("join", async ctx =>
        {
            var socketId = ctx.GetValue<string>(0);
            var client = ctx.GetValue<Client>(1);
            _mainThreadActions.Enqueue(() =>
            {
                _clientToSocket[client.ClientId] = socketId;
                _socketToClient[socketId] = client.ClientId;
                EnsurePeerAudio(socketId);
                _ = StartOfferAsync(socketId);
            });
            await Task.CompletedTask;
        });

        _socket.On("leave", async ctx =>
        {
            var socketId = ctx.GetValue<string>(0);
            _mainThreadActions.Enqueue(() => RemovePeer(socketId));
            await Task.CompletedTask;
        });

        // WebRTC signaling relay
        _socket.On("signal", async ctx =>
        {
            var payload = ctx.GetValue<SignalPayload>(0);
            _ = HandleSignalAsync(payload.from, payload.data);
            await Task.CompletedTask;
        });

        // STUN/TURN config from server
        _socket.On("clientPeerConfig", async ctx =>
        {
            var config = ctx.GetValue<ClientPeerConfig>(0);
            if (config?.iceServers != null && config.iceServers.Length > 0)
            {
                _iceServers = config.iceServers
                    .Select(s => new RTCIceServer
                    {
                        urls = s.urls,
                        username = s.username,
                        credential = s.credential,
                    })
                    .ToList();
            }
            await Task.CompletedTask;
        });

        _socket.On("VAD", async ctx => { await Task.CompletedTask; });

        _socket.ConnectAsync();
    }

    private void DisconnectSocket()
    {
        _socket?.EmitAsync("leave");
        _socket?.DisconnectAsync();
        _socket = null;
        _currentLobby = "MENU";
        _mySocketId = string.Empty;
        ClearPeers();
        StopMicrophone();
    }

    public async Task JoinLobby(string lobbyCode, int playerId, int clientId, bool isHost)
    {
        if (_socket == null) return;

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

    // WebRTC peer management

    private void EnsurePeerAudio(string socketId)
    {
        if (_peers.ContainsKey(socketId)) return;

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

        var clip = AudioClip.Create($"BCL_PeerClip_{socketId}", SampleRate, Channels, SampleRate, false);
        clip.SetData(new float[SampleRate], 0);
        source.clip = clip;
        source.Play();

        var audio = new PeerAudio(source, clip);
        var filter = go.AddComponent<PeerAudioFilter>();
        filter.SampleQueue = audio.SampleQueue;

        var rtcConfig = new RTCConfiguration { iceServers = _iceServers };
        var pc = new RTCPeerConnection(rtcConfig);

        var peer = new WebRtcPeer(pc, audio);
        _peers[socketId] = peer;

        // Incoming data channel
        pc.ondatachannel += dc =>
        {
            peer.DataChannel = dc;
            dc.onmessage += (_, _, data) => OnAudioData(socketId, data);
        };

        // Trickle ICE; send candidates to peer via socket
        pc.onicecandidate += candidate =>
        {
            if (candidate == null) return;
            var signalData = JsonSerializer.Serialize(new
            {
                candidate = candidate.candidate,
                sdpMid = candidate.sdpMid,
                sdpMLineIndex = candidate.sdpMLineIndex,
            });
            _socket?.EmitAsync("signal", new object[]
            {
                new { to = socketId, data = signalData }
            });
        };
    }

    private async Task StartOfferAsync(string socketId)
    {
        if (!_peers.TryGetValue(socketId, out var peer)) return;
        var pc = peer.Connection;

        // Create unreliable unordered data channel for audio (low latency)
        var dc = await pc.createDataChannel("audio", new RTCDataChannelInit
        {
            ordered = false,
            maxRetransmits = 0,
        });
        peer.DataChannel = dc;
        dc.onmessage += (_, _, data) => OnAudioData(socketId, data);

        var offer = pc.createOffer(null);
        await pc.setLocalDescription(offer);

        var sdpJson = JsonSerializer.Serialize(new { type = "offer", sdp = offer.sdp });
        await _socket!.EmitAsync("signal", new object[] { new { to = socketId, data = sdpJson } });
    }

    private async Task HandleSignalAsync(string fromSocketId, string dataJson)
    {
        if (!_peers.TryGetValue(fromSocketId, out var peer)) return;
        var pc = peer.Connection;

        using var doc = JsonDocument.Parse(dataJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("type", out var typeProp))
        {
            var type = typeProp.GetString();
            var sdp = root.GetProperty("sdp").GetString() ?? string.Empty;

            if (type == "offer")
            {
                pc.setRemoteDescription(new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.offer,
                    sdp = sdp,
                });

                var answer = pc.createAnswer(null);
                await pc.setLocalDescription(answer);

                var answerJson = JsonSerializer.Serialize(new { type = "answer", sdp = answer.sdp });
                await _socket!.EmitAsync("signal", new object[] { new { to = fromSocketId, data = answerJson } });
            }
            else if (type == "answer")
            {
                pc.setRemoteDescription(new RTCSessionDescriptionInit
                {
                    type = RTCSdpType.answer,
                    sdp = sdp,
                });
            }
        }
        else if (root.TryGetProperty("candidate", out var candProp))
        {
            var candidate = new RTCIceCandidateInit
            {
                candidate = candProp.GetString(),
                sdpMid = root.TryGetProperty("sdpMid", out var mid) ? mid.GetString() : null,
                sdpMLineIndex = root.TryGetProperty("sdpMLineIndex", out var idx) ? (ushort)idx.GetInt32() : (ushort)0,
            };
            pc.addIceCandidate(candidate);
        }
    }

    private void OnAudioData(string socketId, byte[] opusData)
    {
        if (!_peers.TryGetValue(socketId, out var peer)) return;

        var pcm = new short[FrameSamples * 2];
        var decoded = peer.Audio.Decoder.Decode(opusData, 0, opusData.Length, pcm, 0, pcm.Length);
        if (decoded <= 0) return;

        // Track RMS for speaking indicators
        if (_socketToClient.TryGetValue(socketId, out var clientId))
        {
            var rmsSum = 0f;
            for (var i = 0; i < decoded; i++) { var s = pcm[i] / (float)short.MaxValue; rmsSum += s * s; }
            _peerTalkingLevels[clientId] = Mathf.Sqrt(rmsSum / decoded);
            _peerTalkingTimestamps[clientId] = Time.time;
        }

        for (var i = 0; i < decoded; i++)
            peer.Audio.SampleQueue.Enqueue(pcm[i] / (float)short.MaxValue);
    }

    private void RemovePeer(string socketId)
    {
        if (!_peers.TryRemove(socketId, out var peer)) return;

        if (_socketToClient.TryGetValue(socketId, out var clientId))
        {
            _peerTalkingLevels.Remove(clientId);
            _peerTalkingTimestamps.Remove(clientId);
            _clientToSocket.Remove(clientId);
            _socketToClient.Remove(socketId);
        }

        peer.Dispose();
    }

    private void ClearPeers()
    {
        foreach (var socketId in _peers.Keys.ToList())
            RemovePeer(socketId);
        _clientToSocket.Clear();
        _socketToClient.Clear();
        _peerTalkingLevels.Clear();
        _peerTalkingTimestamps.Clear();
    }

    // Microphone capture

    private bool ComputeTransmitState(RuntimeSettings settings)
    {
        var pttHeld = settings.PushToTalkEnabled && BetterCrewLinkKeybinds.IsHeld(BetterCrewLinkKeybinds.PushToTalk);
        var ptmHeld = settings.PushToMuteEnabled && BetterCrewLinkKeybinds.IsHeld(BetterCrewLinkKeybinds.PushToMute);

        var wants = settings.VoiceActivityEnabled ? _lastVadTalking : true;
        if (settings.PushToTalkEnabled) wants &= pttHeld;
        if (settings.PushToMuteEnabled) wants &= !ptmHeld;
        if (_muted) wants = false;
        return wants;
    }

    private void StartMicrophone(RuntimeSettings settings)
    {
        var desired = settings.MicrophoneDevice;
        if (string.IsNullOrWhiteSpace(desired) || desired == "Default") desired = string.Empty;

        if (_micClip != null && _activeMicDevice == desired) return;
        if (_micClip != null) StopMicrophone();

        if (Microphone.devices.Length == 0)
        {
            BCLLogger.Error("BetterCrewLink: no microphone devices found.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(desired) && !Microphone.devices.Contains(desired))
        {
            BCLLogger.Info($"BetterCrewLink: mic '{desired}' not found, using default.");
            desired = string.Empty;
        }

        _micClip = Microphone.Start(string.IsNullOrWhiteSpace(desired) ? null : desired, true, 1, SampleRate);
        _lastMicPos = 0;
        _activeMicDevice = desired;
        EnsureLoopback();
    }

    private void StopMicrophone()
    {
        if (_micClip == null) return;
        Microphone.End(null);
        Object.Destroy(_micClip);
        _micClip = null;
        _activeMicDevice = string.Empty;
    }

    private void CaptureAndSendAudio(RuntimeSettings settings, bool wantsTransmit)
    {
        StartMicrophone(settings);
        if (_micClip == null || _encoder == null) return;

        var currentPos = Microphone.GetPosition(null);
        if (currentPos < _lastMicPos) currentPos += _micClip.samples;
        if (currentPos - _lastMicPos < FrameSamples) return;

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
        LastLocalTalking = wantsTransmit && _lastVadTalking;

        EmitVad(LastLocalTalking);

        if (settings.TestRelay) EnqueueLoopback(samples);

        _lastMicPos = (_lastMicPos + FrameSamples) % _micClip.samples;

        if (!wantsTransmit) return;

        // Encode and send over every open DataChannel
        var pcm = new short[FrameSamples];
        for (var i = 0; i < FrameSamples; i++)
            pcm[i] = (short)(samples[i] * short.MaxValue);

        var opusPacket = new byte[1024];
        var encodedLength = _encoder.Encode(pcm, 0, FrameSamples, opusPacket, 0, opusPacket.Length);
        var trimmed = new byte[encodedLength];
        Array.Copy(opusPacket, trimmed, encodedLength);

        foreach (var peer in _peers.Values)
        {
            try
            {
                if (peer.DataChannel?.readyState == RTCDataChannelState.open)
                    peer.DataChannel.send(trimmed);
            }
            catch { /* Ignore if channel closed mid-send */ }
        }
    }

    private void EmitVad(bool talking)
    {
        if (_socket == null || talking == _lastVadSent) return;
        _lastVadSent = talking;
        _socket.EmitAsync("VAD", new object[] { talking });
    }

    private void EnsureLoopback()
    {
        if (_loopback != null) return;
        Initialize();

        var go = new GameObject("BCL_Loopback");
        go.transform.SetParent(_root!.transform);

        var source = go.AddComponent<AudioSource>();
        source.spatialBlend = 0f;
        source.volume = 1f;
        source.loop = true;

        var clip = AudioClip.Create("BCL_LoopbackClip", SampleRate, Channels, SampleRate, false);
        clip.SetData(new float[SampleRate], 0);
        source.clip = clip;
        source.Play();

        var audio = new PeerAudio(source, clip);
        var filter = go.AddComponent<PeerAudioFilter>();
        filter.SampleQueue = audio.SampleQueue;

        _loopback = audio;
    }

    private void EnqueueLoopback(float[] samples)
    {
        if (_loopback == null) return;
        foreach (var s in samples)
            _loopback.SampleQueue.Enqueue(s);
    }

    // Inner types

    private sealed class WebRtcPeer : IDisposable
    {
        public RTCPeerConnection Connection { get; }
        public RTCDataChannel? DataChannel { get; set; }
        public PeerAudio Audio { get; }
        public bool Disposed { get; private set; }

        public WebRtcPeer(RTCPeerConnection connection, PeerAudio audio)
        {
            Connection = connection;
            Audio = audio;
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            try { Connection.close(); } catch { }
            Audio.Decoder?.Dispose();
            if (Audio.Source != null)
            {
                Audio.Source.Stop();
                Object.Destroy(Audio.Clip);
                Object.Destroy(Audio.Source.gameObject);
            }
        }
    }

    private sealed class PeerAudio
    {
        public OpusDecoder Decoder { get; } = new(SampleRate, Channels);
        public AudioSource Source { get; }
        public AudioClip Clip { get; }
        public ConcurrentQueue<float> SampleQueue { get; } = new();

        public PeerAudio(AudioSource source, AudioClip clip)
        {
            Source = source;
            Clip = clip;
        }
    }

    // Signal / config DTOs

    private sealed class SignalPayload
    {
        public string from { get; set; } = string.Empty;
        public string data { get; set; } = string.Empty;
        public Client? client { get; set; }
    }

    private sealed class ClientPeerConfig
    {
        public IceServerDto[]? iceServers { get; set; }
    }

    private sealed class IceServerDto
    {
        public string urls { get; set; } = string.Empty;
        public string? username { get; set; }
        public string? credential { get; set; }
    }
}

[RegisterInIl2Cpp]
public sealed class PeerAudioFilter : MonoBehaviour
{
    public ConcurrentQueue<float>? SampleQueue;
    public PeerAudioFilter(IntPtr ptr) : base(ptr) { }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (SampleQueue == null) { Array.Clear(data, 0, data.Length); return; }

        var frames = data.Length / Mathf.Max(1, channels);
        var idx = 0;
        for (var i = 0; i < frames; i++)
        {
            SampleQueue.TryDequeue(out var sample);
            for (var c = 0; c < channels; c++) data[idx++] = sample;
        }
    }
}