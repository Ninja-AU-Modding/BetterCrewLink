namespace BetterCrewLink.Data;

public record Settings(
    bool AlwaysOnTop,
    string Language,
    string Microphone,
    string Speaker,
    int PushToTalkMode,
    string ServerUrl,
    string PushToTalkShortcut,
    string DeafenShortcut,
    string MuteShortcut,
    string ImpostorRadioShortcut,
    bool HideCode,
    bool NatFix,
    bool CompactOverlay,
    string OverlayPosition,
    bool EnableOverlay,
    bool MeetingOverlay,
    LobbySettings LocalLobbySettings,
    float GhostVolumeAsImpostor,
    float CrewVolumeAsGhost,
    float MasterVolume,
    float MicrophoneGain,
    bool MicrophoneGainEnabled,
    float MicSensitivity,
    bool MicSensitivityEnabled,
    bool MobileHost,
    bool VadEnabled,
    bool HardwareAcceleration,
    bool EchoCancellation,
    bool NoiseSuppression,
    bool OldSampleDebug,
    bool EnableSpatialAudio,
    Dictionary<int, SocketConfig> PlayerConfigMap,
    bool ObsOverlay,
    string? ObsSecret,
    string LaunchPlatform,
    Dictionary<string, string> CustomPlatforms
);

public record LobbySettings(
    float MaxDistance,
    bool VisionHearing,
    bool Haunting,
    bool HearImpostorsInVents,
    bool ImpostorsHearImpostorsInVent,
    bool ImpostorRadioEnabled,
    bool CommsSabotage,
    bool DeadOnly,
    bool MeetingGhostOnly,
    bool HearThroughCameras,
    bool WallsBlockAudio,
    bool PublicLobbyOn,
    string PublicLobbyTitle,
    string PublicLobbyLanguage
);

public record SocketConfig(float Volume, bool IsMuted);