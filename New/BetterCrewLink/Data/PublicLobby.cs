namespace BetterCrewLink.Data;

public record PublicLobby(
    int Id,
    string Title,
    string Host,
    int CurrentPlayers,
    int MaxPlayers,
    string Language,
    string Mods,
    bool IsPublic,
    string Server,
    GameState GameState,
    int StateTime
);