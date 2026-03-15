using UnityEngine;
using System.Diagnostics.CodeAnalysis;

namespace BetterCrewLink.Data;

public enum MapType
{
    TheSkeld = 0,
    MiraHQ = 1,
    Polus = 2,
    TheSkeldApril = 3,
    Airship = 4,
    Fungle = 5,
    Unknown = 6,
    Submerged = 105,
}

public enum CameraLocation
{
    East = 0,      // Engine Room
    Central = 1,   // Vault
    Northeast = 2, // Records
    South = 3,     // Security
    SouthWest = 4, // Cargo Bay
    NorthWest = 5, // Meeting Room
    Skeld = 6,
    None = 7,
}

public record struct CameraEntry(float X, float Y)
{
    public Vector2 ToVector2() => new(X, Y);
}

public record AmongUsMap(Dictionary<int, CameraEntry> Cameras);

public static class AmongUsMaps
{
    private static readonly AmongUsMap DefaultMap = new(new Dictionary<int, CameraEntry>());

    [SuppressMessage("Design", "S2386", Justification = "Static map data, intentionally public")]
    [SuppressMessage("Design", "S3887", Justification = "Static map data, intentionally public")]
    public static readonly Dictionary<MapType, AmongUsMap> Maps = new()
    {
        [MapType.TheSkeld] = new AmongUsMap(new Dictionary<int, CameraEntry>
        {
            [0] = new(13.2417f, -4.348f),
            [1] = new(0.6216f, -6.5642f),
            [2] = new(-7.1503f, 1.6709f),
            [3] = new(-17.8098f, -4.8983f),
        }),

        [MapType.Polus] = new AmongUsMap(new Dictionary<int, CameraEntry>
        {
            [(int)CameraLocation.East] = new(29f, -15.7f),
            [(int)CameraLocation.Central] = new(15.4f, -15.4f),
            [(int)CameraLocation.Northeast] = new(24.4f, -8.5f),
            [(int)CameraLocation.South] = new(17f, -20.6f),
            [(int)CameraLocation.SouthWest] = new(4.7f, -22.73f),
            [(int)CameraLocation.NorthWest] = new(11.6f, -8.2f),
        }),

        [MapType.TheSkeldApril] = DefaultMap,
        [MapType.MiraHQ] = DefaultMap,

        [MapType.Airship] = new AmongUsMap(new Dictionary<int, CameraEntry>
        {
            [(int)CameraLocation.East] = new(-8.2872f, 0.0527f),   // Engine Room
            [(int)CameraLocation.Central] = new(-4.0477f, 9.1447f),   // Vault
            [(int)CameraLocation.Northeast] = new(23.5616f, 9.8882f),   // Records
            [(int)CameraLocation.South] = new(4.881f, -11.1688f),  // Security
            [(int)CameraLocation.SouthWest] = new(30.3702f, -0.874f),    // Cargo Bay
            [(int)CameraLocation.NorthWest] = new(3.3018f, 16.2631f),  // Meeting Room
        }),

        [MapType.Fungle] = new AmongUsMap(new Dictionary<int, CameraEntry>()),
        [MapType.Submerged] = new AmongUsMap(new Dictionary<int, CameraEntry>()),
        [MapType.Unknown] = DefaultMap,
    };
}