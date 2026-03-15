namespace BetterCrewLink.Data;

public enum ModsType
{
    None,
    TownOfUsMira,
    TownOfUs,
    TheOtherRoles,
    LasMonjas,
    Other,
}

public record AmongUsMod(ModsType Id, string Label, string? DllStartsWith = null);

public static class ModList
{
    public static readonly AmongUsMod[] Mods =
    [
        new(ModsType.None,         "None"),
        new(ModsType.TownOfUsMira, "Town of Us: Mira",          "TownOfUsMira"),
        new(ModsType.TownOfUs,     "Town of Us: Reactivated",   "TownOfUs"),
        new(ModsType.TheOtherRoles,"The Other Roles",            "TheOtherRoles"),
        new(ModsType.LasMonjas,    "Las Monjas",                 "LasMonjas"),
        new(ModsType.Other,        "Other"),
    ];
}