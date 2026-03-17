using BetterCrewLink.Networking;
using MiraAPI.LocalSettings;

namespace BetterCrewLink.Utils;

public readonly record struct RuntimeSettings(
    string MicrophoneDevice,
    string SpeakerDevice,
    bool VoiceActivityEnabled,
    bool PushToTalkEnabled,
    bool PushToMuteEnabled,
    float MicrophoneVolume,
    float MicSensitivity,
    float MasterVolume,
    float CrewVolumeAsGhost,
    float GhostVolumeAsImpostor,
    float MaxDistance,
    bool EnableSpatialAudio,
    bool TestRelay,
    string ServerUrl,
    bool EnableOverlay,
    OverlayPositionOption OverlayPosition,
    // Room rules
    bool WallsBlockSound,
    bool OnlyHearInSight,
    bool HearInVent,
    bool VentPrivateChat,
    bool CommsSabDisables,
    bool CameraCanHear,
    bool ImpostorPrivateRadio,
    bool OnlyGhostsCanTalk,
    bool OnlyMeetingOrLobby,
    // Runtime keybind state
    bool ImpostorRadioOnly
);

public static class BclConfig
{
    public const string DefaultServerUrl = "https://bettercrewl.ink";
    private const string DefaultDevice = "Default";

    public static RuntimeSettings Current
    {
        get
        {
            var s = LocalSettingsTabSingleton<BetterCrewLinkLocalSettings>.Instance;

            var serverUrl = string.IsNullOrWhiteSpace(s.ServerUrl.Value)
                ? DefaultServerUrl
                : s.ServerUrl.Value;
            if (!serverUrl.EndsWith('/'))
                serverUrl += "/";

            var activation = s.ActivationType.Value;
            var micDevice = s.MicrophoneDevice.Value;
            if (string.IsNullOrWhiteSpace(micDevice) || micDevice == DefaultDevice)
                micDevice = DefaultDevice;

            return new RuntimeSettings(
                MicrophoneDevice:    micDevice,
                SpeakerDevice:       DefaultDevice,
                VoiceActivityEnabled: activation == VoiceActivationType.VoiceActivity,
                PushToTalkEnabled:   activation == VoiceActivationType.PushToTalk,
                PushToMuteEnabled:   activation == VoiceActivationType.PushToMute,
                MicrophoneVolume:    s.MicrophoneVolume.Value,
                MicSensitivity:      s.MicSensitivity.Value,
                MasterVolume:        s.MasterVolume.Value,
                CrewVolumeAsGhost:   s.CrewVolumeAsGhost.Value,
                GhostVolumeAsImpostor: s.GhostVolumeAsImpostor.Value,
                MaxDistance:         s.MaxDistance.Value,
                EnableSpatialAudio:  s.EnableSpatialAudio.Value,
                TestRelay:           s.TestRelay.Value,
                ServerUrl:           serverUrl,
                EnableOverlay:       s.EnableOverlay.Value,
                OverlayPosition:     s.OverlayPosition.Value,
                WallsBlockSound:     s.WallsBlockSound.Value,
                OnlyHearInSight:     s.OnlyHearInSight.Value,
                HearInVent:          s.HearInVent.Value,
                VentPrivateChat:     s.VentPrivateChat.Value,
                CommsSabDisables:    s.CommsSabDisables.Value,
                CameraCanHear:       s.CameraCanHear.Value,
                ImpostorPrivateRadio: s.ImpostorPrivateRadio.Value,
                OnlyGhostsCanTalk:   s.OnlyGhostsCanTalk.Value,
                OnlyMeetingOrLobby:  s.OnlyMeetingOrLobby.Value,
                ImpostorRadioOnly:   VoiceClient.ImpostorRadioOnly
            );
        }
    }
}
