namespace MtgaBot.Decide;

public static class PolicyFactory
{
    public static IPolicy Create(string? name, FarmMvpMode mode = FarmMvpMode.FullMvp) => Normalize(name) switch
    {
        FarmMvpPolicy.Name => new FarmMvpPolicy(mode: mode),
        PassPolicy.Name => new PassPolicy(),
        _ => throw new ArgumentException(
            $"Unknown policy '{name}'. Available: {FarmMvpPolicy.Name}, {PassPolicy.Name}."),
    };

    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return FarmMvpPolicy.Name;
        }

        return name.Trim() switch
        {
            "FarmMvp" or "farm" or "stable" or "FarmMVP" => FarmMvpPolicy.Name,
            "Pass" or "pass" or "PassOnly" => PassPolicy.Name,
            var other => other,
        };
    }

    public static FarmMvpMode ParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FarmMvpMode.FullMvp;
        }

        return value.Trim() switch
        {
            "LandOnly" or "land-only" or "landonly" or "land" => FarmMvpMode.LandOnly,
            "LandAndCast" or "land-and-cast" or "landandcast" or "cast" => FarmMvpMode.LandAndCast,
            "FullMvp" or "full" or "full-mvp" or "fullmvp" or "mvp" => FarmMvpMode.FullMvp,
            _ => throw new ArgumentException(
                $"Unknown mode '{value}'. Available: LandOnly, LandAndCast, FullMvp."),
        };
    }
}
