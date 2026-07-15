namespace MtgaBot.Decide;

public static class PolicyFactory
{
    public static IPolicy Create(string? name) => Normalize(name) switch
    {
        FarmMvpPolicy.Name => new FarmMvpPolicy(),
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
}
