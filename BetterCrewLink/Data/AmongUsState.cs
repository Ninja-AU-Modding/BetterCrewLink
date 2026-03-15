using BetterCrewLink.Data;

namespace BetterCrewLink.Data;

public enum GameState
{
    Lobby,
    Tasks,
    Discussion,
    Menu,
    Unknown,
}

public record AmongUsState(
    GameState GameState,
    GameState OldGameState,
    int LobbyCodeInt,
    string LobbyCode,
    List<Player> Players,
    bool IsHost,
    int ClientId,
    int HostId,
    bool ComsSabotaged,
    CameraLocation CurrentCamera,
    MapType Map,
    float LightRadius,
    bool LightRadiusChanged,
    List<int> ClosedDoors,
    string CurrentServer,
    int MaxPlayers,
    ModsType Mod,
    bool OldMeetingHud
);

public record Player(
    int Ptr,
    int Id,
    int ClientId,
    string Name,
    int NameHash,
    int ColorId,
    string HatId,
    int PetId,
    string SkinId,
    string VisorId,
    bool Disconnected,
    bool IsImpostor,
    bool IsDead,
    int TaskPtr,
    int ObjectPtr,
    bool IsLocal,
    int ShiftedColor,
    bool Bugged,
    float X,
    float Y,
    bool InVent,
    bool IsDummy
);

public record Client(int PlayerId, int ClientId);

public record VoiceState(
    Dictionary<int, bool> OtherTalking,
    Dictionary<int, string> PlayerSocketIds,
    Dictionary<int, bool> OtherDead,
    Dictionary<string, Client> SocketClients,
    Dictionary<string, bool> AudioConnected,
    int ImpostorRadioClientId,
    bool LocalTalking,
    bool LocalIsAlive,
    bool Muted,
    bool Deafened,
    ModsType Mod
);