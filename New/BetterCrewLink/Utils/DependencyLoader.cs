using BepInEx;
using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BetterCrewLink.Utils;

internal static class DependencyLoader
{
    private static bool _initialized;

    public static void EnsureLoaded()
    {
        if (_initialized)
            return;

        _initialized = true;
        AppDomain.CurrentDomain.AssemblyResolve += ResolveFromPluginFolder;
    }

    private static Assembly? ResolveFromPluginFolder(object? sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name).Name;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var fileName = name + ".dll";
        var pluginDir = Paths.PluginPath;
        var localDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;

        var searchDirs = new[]
        {
            localDir,
            Path.Combine(pluginDir, "BetterCrewLink"),
            pluginDir
        }.Distinct();

        foreach (var dir in searchDirs)
        {
            var candidate = Path.Combine(dir, fileName);
            if (!File.Exists(candidate))
                continue;

            try
            {
                return Assembly.LoadFrom(candidate);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }
}
