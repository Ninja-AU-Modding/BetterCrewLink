using MiraAPI.Keybinds;
using Rewired;

namespace BetterCrewLink;

[RegisterCustomKeybinds]
public static class BetterCrewLinkKeybinds
{
    public static MiraKeybind PushToTalk { get; } =
        new("Push To Talk", KeyboardKeyCode.V, exclusive: false);

    public static MiraKeybind PushToMute { get; } =
        new("Push To Mute", KeyboardKeyCode.B, exclusive: false);

    public static MiraKeybind ToggleMute { get; } =
        new("Toggle Mute", KeyboardKeyCode.M);

    public static MiraKeybind ToggleDeafen { get; } =
        new("Toggle Deafen", KeyboardKeyCode.N);

    public static MiraKeybind ImpostorRadio { get; } =
        new("Impostor Radio", KeyboardKeyCode.R, exclusive: false);

    public static bool IsHeld(MiraKeybind keybind)
    {
        if (!ReInput.isReady)
            return false;

        var action = keybind.RewiredInputAction;
        if (action == null)
            return false;

        var player = ReInput.players.GetPlayer(0);
        return player != null && player.GetButton(action.id);
    }

    public static bool IsPressed(MiraKeybind keybind)
    {
        if (!ReInput.isReady)
            return false;

        var action = keybind.RewiredInputAction;
        if (action == null)
            return false;

        var player = ReInput.players.GetPlayer(0);
        return player != null && player.GetButtonDown(action.id);
    }

}
