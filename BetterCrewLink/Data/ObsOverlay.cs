namespace BetterCrewLink.Data;

public record OverlayState(
    GameState GameState,
    List<OverlayPlayer> Players
);

public record OverlayPlayer(
    int Id,
    int ClientId,
    bool InVent,
    bool IsDead,
    string Name,
    int ColorId,
    string HatId,
    string SkinId,
    string VisorId,
    int PetId,
    bool Disconnected,
    bool IsLocal,
    bool Bugged,
    bool Connected,
    List<string> RealColor,
    int ShiftedColor
);

public record ObsVoiceState(
    OverlayState OverlayState,
    Dictionary<int, bool> OtherTalking,
    Dictionary<int, bool> OtherDead,
    bool LocalTalking,
    bool LocalIsAlive,
    string Mod,
    bool OldMeetingHud
);