namespace MtgaBot.Ingest;

public sealed class PlayerLogLocator : IPlayerLogLocator
{
    public string GetDefaultPlayerLogPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                home,
                "AppData",
                "LocalLow",
                "Wizards Of The Coast",
                "MTGA",
                "Player.log");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(
                home,
                "Library",
                "Logs",
                "Wizards Of The Coast",
                "MTGA",
                "Player.log");
        }

        return Path.Combine(
            home,
            ".local",
            "share",
            "Steam",
            "steamapps",
            "compatdata",
            "2141910",
            "pfx",
            "drive_c",
            "users",
            "steamuser",
            "AppData",
            "LocalLow",
            "Wizards Of The Coast",
            "MTGA",
            "Player.log");
    }
}
