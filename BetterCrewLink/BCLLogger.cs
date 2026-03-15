namespace BetterCrewLink;

public static class BCLLogger
{
    public static void Debug(string message)
    {
        if (BetterCrewLinkPlugin.IsDevRelease)
        {
            Logger<BetterCrewLinkPlugin>.Debug(message);
        }
    }

    public static void Info(string message)
    {
        Logger<BetterCrewLinkPlugin>.Info(message);
    }

    public static void Warn(string message)
    {
        Logger<BetterCrewLinkPlugin>.Warning(message);
    }

    public static void Error(string message)
    {
        Logger<BetterCrewLinkPlugin>.Error(message);
    }

    public static void Fatal(string message)
    {
        Logger<BetterCrewLinkPlugin>.Fatal(message);
    }
}