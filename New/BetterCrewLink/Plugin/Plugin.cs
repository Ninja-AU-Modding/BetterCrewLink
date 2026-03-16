using BetterCrewLink.GameHooks;
using BetterCrewLink.Networking;
using BetterCrewLink.Utils;
using BetterCrewLink.Voice;
using Reactor.Utilities.Attributes;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BetterCrewLink;

public partial class BetterCrewLinkPlugin
{
    internal static BetterCrewLinkBehaviour? Behaviour { get; private set; }

    private void InitializeRuntime()
    {
        var go = new GameObject("BetterCrewLink");
        Object.DontDestroyOnLoad(go);

        Behaviour = go.AddComponent<BetterCrewLinkBehaviour>();
        Behaviour.Initialize();
    }
}

[RegisterInIl2Cpp]
public sealed class BetterCrewLinkBehaviour : MonoBehaviour
{
    private PlayerTracker    _tracker   = null!;
    private ProximityManager _proximity = null!;
    private VoiceClient      _voice     = null!;
    private GameSnapshot?    _lastSnapshot;

    public BetterCrewLinkBehaviour(IntPtr ptr) : base(ptr) { }

    public void Initialize()
    {
        _tracker   = new PlayerTracker();
        _proximity = new ProximityManager();
        _voice     = new VoiceClient();
        _voice.Initialize();
    }

    private void Update()
    {
        var settings = BclConfig.Current;
        LobbyBrowserClient.Instance.Tick(settings);

        var snapshot  = _tracker.Update();
        _lastSnapshot = snapshot;

        if (snapshot == null)
        {
            _voice.Tick(null, settings);
            MicOverlay.Ensure().SetVisible(false);
            return;
        }

        HandleKeybinds();

        var volumes = _proximity.ComputeVolumes(snapshot, settings);

        _voice.ApplyVolumes(volumes, snapshot, settings);
        _voice.Tick(snapshot, settings);

        UpdateMicOverlay(snapshot);
    }

    private void UpdateMicOverlay(GameSnapshot snapshot)
    {
        var overlay  = MicOverlay.Ensure();
        var settings = BclConfig.Current;
        var show     = settings.EnableOverlay && snapshot.Phase == GamePhase.Lobby;
        overlay.SetVisible(show);

        if (!show)
            return;

        overlay.SetPosition(settings.OverlayPosition);
        overlay.UpdateState(VoiceClient.LastLocalTalking, VoiceClient.LastLocalMicRms, settings.MicrophoneDevice);
    }

    private void HandleKeybinds()
    {
        if (BetterCrewLinkKeybinds.IsPressed(BetterCrewLinkKeybinds.ToggleMute))
            _voice.ToggleMute();

        if (BetterCrewLinkKeybinds.IsPressed(BetterCrewLinkKeybinds.ToggleDeafen))
            _voice.ToggleDeafen();

        if (BetterCrewLinkKeybinds.IsPressed(BetterCrewLinkKeybinds.ImpostorRadio))
            _voice.ToggleImpostorRadio();
    }
}
