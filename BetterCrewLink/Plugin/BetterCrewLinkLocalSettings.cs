using BepInEx.Configuration;
using MiraAPI.LocalSettings;
using MiraAPI.LocalSettings.Attributes;
using MiraAPI.Utilities;
using UnityEngine;

namespace BetterCrewLink;

public enum OverlayPositionOption
{
    Left,
    Right,
    Top,
    Bottom,
}

public enum VoiceActivationType
{
    VoiceActivity,
    PushToTalk,
    PushToMute,
}

public sealed class BetterCrewLinkLocalSettings : LocalSettingsTab
{
    public BetterCrewLinkLocalSettings(ConfigFile config) : base(config)
    {
        MicrophoneDevice = config.Bind("Audio", "Microphone Device", "Default");

        ActivationType = config.Bind("Audio", "Activation Type", VoiceActivationType.VoiceActivity);
        MicrophoneVolume = config.Bind("Audio", "Microphone Volume", 100f);
        MicSensitivity = config.Bind("Audio", "Microphone Sensitivity", 0.02f);
        MasterVolume = config.Bind("Audio", "Master Volume", 100f);
        CrewVolumeAsGhost = config.Bind("Audio", "Crew Volume As Ghost", 100f);
        GhostVolumeAsImpostor = config.Bind("Audio", "Ghost Volume As Impostor", 100f);
        MaxDistance = config.Bind("Audio", "Max Distance", 5f);
        EnableSpatialAudio = config.Bind("Audio", "Enable Spatial Audio", true);
        TestRelay = config.Bind("Audio", "Test Relay (Loopback)", false);

        ServerUrl = config.Bind("Networking", "Server URL", "https://bettercrewl.ink");
        EnableOverlay = config.Bind("Overlay", "Enable Overlay", true);
        OverlayPosition = config.Bind("Overlay", "Overlay Position", OverlayPositionOption.Right);
    }

    public override string TabName => "BetterCrewLink";
    protected override bool ShouldCreateLabels => false;
    private bool _micSettingInitialized;

    public override LocalSettingTabAppearance TabAppearance => new()
    {
        TabColor = new Color(0.72f, 0.2f, 0.9f),
        TabButtonColor = new Color(0.2f, 0.2f, 0.2f),
        TabButtonHoverColor = new Color(0.4f, 0.4f, 0.4f),
        TabButtonActiveColor = new Color(0.72f, 0.2f, 0.9f),
        ToggleActiveColor = new Color(0.72f, 0.2f, 0.9f),
        ToggleInactiveColor = Color.red,
    };

    [LocalEnumSetting(name: "Audio - Activation Type")]
    public ConfigEntry<VoiceActivationType> ActivationType { get; private set; }

    [LocalSliderSetting(name: "Audio - Microphone Volume", min: 0f, max: 200f, suffixType: MiraNumberSuffixes.Percent, formatString: "0", displayValue: true, roundValue: true)]
    public ConfigEntry<float> MicrophoneVolume { get; private set; }

    [LocalSliderSetting(name: "Audio - Microphone Sensitivity", min: 0f, max: 0.1f, formatString: "0.00", displayValue: true)]
    public ConfigEntry<float> MicSensitivity { get; private set; }

    public ConfigEntry<string> MicrophoneDevice { get; private set; }

    [LocalSliderSetting(name: "Audio - Master Volume", min: 0f, max: 200f, suffixType: MiraNumberSuffixes.Percent, formatString: "0", displayValue: true, roundValue: true)]
    public ConfigEntry<float> MasterVolume { get; private set; }

    [LocalSliderSetting(name: "Audio - Crew Volume As Ghost", min: 0f, max: 200f, suffixType: MiraNumberSuffixes.Percent, formatString: "0", displayValue: true, roundValue: true)]
    public ConfigEntry<float> CrewVolumeAsGhost { get; private set; }

    [LocalSliderSetting(name: "Audio - Ghost Volume As Impostor", min: 0f, max: 200f, suffixType: MiraNumberSuffixes.Percent, formatString: "0", displayValue: true, roundValue: true)]
    public ConfigEntry<float> GhostVolumeAsImpostor { get; private set; }

    [LocalSliderSetting(name: "Audio - Max Distance", min: 1f, max: 12f, formatString: "0.0", displayValue: true)]
    public ConfigEntry<float> MaxDistance { get; private set; }

    [LocalToggleSetting(name: "Audio - Enable Spatial Audio")]
    public ConfigEntry<bool> EnableSpatialAudio { get; private set; }

    [LocalToggleSetting(name: "Audio - Test Relay (Loopback)")]
    public ConfigEntry<bool> TestRelay { get; private set; }

    [LocalSettingsButton]
    public LocalSettingsButton TestAudioButton { get; private set; } =
        new("Test Speakers", AudioTestHelper.PlayTestTone);

    public ConfigEntry<string> ServerUrl { get; private set; }

    [LocalToggleSetting(name: "Overlay - Enable Overlay")]
    public ConfigEntry<bool> EnableOverlay { get; private set; }

    [LocalEnumSetting(name: "Overlay - Overlay Position")]
    public ConfigEntry<OverlayPositionOption> OverlayPosition { get; private set; }

    public override GameObject CreateTab(OptionsMenuBehaviour instance)
    {
        if (!_micSettingInitialized)
        {
            _micSettingInitialized = true;
            _ = new LocalMicrophoneSetting(GetType(), MicrophoneDevice, "Audio - Microphone");
        }

        return base.CreateTab(instance);
    }
}
