using MiraAPI.LocalSettings;
using UnityEngine;

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
    OverlayPositionOption OverlayPosition
);

// Centralized access to MiraAPI local settings.
public static class BclConfig
{
    public const string DefaultServerUrl = "https://bettercrewl.ink";
    private const string DefaultDevice = "Default";

    public static RuntimeSettings Current
    {
        get
        {
            var settings = LocalSettingsTabSingleton<BetterCrewLinkLocalSettings>.Instance;

            var serverUrl = string.IsNullOrWhiteSpace(settings.ServerUrl.Value)
                ? DefaultServerUrl
                : settings.ServerUrl.Value;
            if (!serverUrl.EndsWith("/"))
                serverUrl += "/";

            var activation = settings.ActivationType.Value;
            var micDevice = settings.MicrophoneDevice.Value;
            if (string.IsNullOrWhiteSpace(micDevice) || micDevice == DefaultDevice)
                micDevice = DefaultDevice;

            return new RuntimeSettings(
                micDevice,
                DefaultDevice,
                activation == VoiceActivationType.VoiceActivity,
                activation == VoiceActivationType.PushToTalk,
                activation == VoiceActivationType.PushToMute,
                settings.MicrophoneVolume.Value,
                settings.MicSensitivity.Value,
                settings.MasterVolume.Value,
                settings.CrewVolumeAsGhost.Value,
                settings.GhostVolumeAsImpostor.Value,
                settings.MaxDistance.Value,
                settings.EnableSpatialAudio.Value,
                settings.TestRelay.Value,
                serverUrl,
                settings.EnableOverlay.Value,
                settings.OverlayPosition.Value
            );
        }
    }

}
