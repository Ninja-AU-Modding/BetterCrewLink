using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using MiraAPI.PluginLoading;
using Reactor;
using Reactor.Networking;
using Reactor.Networking.Attributes;
using Rewired.Utils.Classes.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

namespace BetterCrewLink;

[BepInAutoPlugin("com.goober.bettercrewlink", "BetterCrewLink")]
[BepInProcess("Among Us.exe")]
[BepInDependency(ReactorPlugin.Id)]
[ReactorModFlags(ModFlags.None)]
public partial class BetterCrewLinkPlugin : BasePlugin
{
    public const string VersionString = "1.0.0";
    [SuppressMessage("Style", "S1104", Justification = "Required by BepInEx plugin metadata")]
    public static readonly Version PluginVersion = System.Version.Parse(VersionString);
    public Harmony Harmony { get; } = new(Id);

    /////////////////////
    public const bool IsDevRelease = true;
    /////////////////////

    public override void Load()
    {
        BCLLogger.Debug("BetterCrewLink is loading...");
        BCLLogger.Debug($"BCL Version: {VersionString + (IsDevRelease ? "-indev" : "")}");

        ReactorCredits.Register(
            "BetterCrewLink",
            VersionString + (IsDevRelease ? "-indev" : ""),
            false,
            ReactorCredits.AlwaysShow
        );

        try
        {
            Harmony.PatchAll();
        }
        catch (Exception exception)
        {
            BCLLogger.Fatal(
                $"An error occurred while loading BetterCrewLink-{VersionString + (IsDevRelease ? "-indev" : "")} ({Id})"
            );
            BCLLogger.Fatal(exception.Message);
        }

        BCLLogger.Debug("BetterCrewLink finished loading!");
    }
}
