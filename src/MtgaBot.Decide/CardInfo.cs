namespace MtgaBot.Decide;

public sealed record CardInfo(
    int GrpId,
    string Name,
    IReadOnlyList<string> Types,
    string? ManaCost,
    string OracleText)
{
    public bool IsCreature => Types.Contains("Creature", StringComparer.OrdinalIgnoreCase);

    public bool IsEnchantment => Types.Contains("Enchantment", StringComparer.OrdinalIgnoreCase);

    public bool IsLand => Types.Contains("Land", StringComparer.OrdinalIgnoreCase);

    public bool IsInstant => Types.Contains("Instant", StringComparer.OrdinalIgnoreCase);

    public bool IsSorcery => Types.Contains("Sorcery", StringComparer.OrdinalIgnoreCase);
}
