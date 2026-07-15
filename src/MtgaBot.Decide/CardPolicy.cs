using System.Text.RegularExpressions;

namespace MtgaBot.Decide;

public interface ICardPolicy
{
    bool IsUnsupportedToCast(int grpId, CardInfo? card);
}

/// <summary>
/// Ports Burning Lotus CardPolicy + StableFarm chooser patterns for farm MVP.
/// </summary>
public sealed partial class CardPolicy : ICardPolicy
{
    private static readonly HashSet<int> UnsupportedGrpIds =
    [
        93756, // Inspiration from Beyond
    ];

    private static readonly HashSet<int> StrategicSkipGrpIds =
    [
        78934,
        93677, // Undying Malice
    ];

    private static readonly Regex[] UnsupportedOraclePatterns =
    [
        MyRegex(),
    ];

    private static readonly Regex[] ChooserPatterns =
    [
        MyRegex1(),
        MyRegex2(),
        MyRegex3(),
        MyRegex4(),
        MyRegex5(),
        MyRegex6(),
        MyRegex7(),
        MyRegex8(),
        MyRegex9(),
        MyRegex10(),
        MyRegex11(),
    ];

    private readonly Dictionary<int, bool> _oracleMemo = new();

    public bool IsUnsupportedToCast(int grpId, CardInfo? card)
    {
        if (UnsupportedGrpIds.Contains(grpId) || StrategicSkipGrpIds.Contains(grpId))
        {
            return true;
        }

        if (card is null || string.IsNullOrWhiteSpace(card.OracleText))
        {
            return false;
        }

        if (_oracleMemo.TryGetValue(grpId, out var cached))
        {
            return cached;
        }

        var text = card.OracleText.Replace('\n', ' ');
        var unsupported = UnsupportedOraclePatterns.Any(pattern => pattern.IsMatch(text));
        _oracleMemo[grpId] = unsupported;
        return unsupported;
    }

    public bool HasChooserEffects(CardInfo card)
    {
        if (string.IsNullOrWhiteSpace(card.OracleText))
        {
            return false;
        }

        var text = card.OracleText.Replace('\n', ' ');
        return ChooserPatterns.Any(pattern => pattern.IsMatch(text));
    }

    public bool IsSafePermanentToCast(CardInfo card)
    {
        if (IsUnsupportedToCast(card.GrpId, card))
        {
            return false;
        }

        if (card.IsInstant || card.IsSorcery || card.IsLand)
        {
            return false;
        }

        if (card.IsCreature)
        {
            return !HasChooserEffects(card);
        }

        // Simple enchantment (not creature) without choosers.
        if (card.IsEnchantment)
        {
            return !HasChooserEffects(card);
        }

        return false;
    }

    [GeneratedRegex(@"return\s+(?:a|an|one|two|three|up to \w+)\b(?:(?!target).)*?\bfrom (?:your )?graveyard\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline, "ru-RU")]
    private static partial Regex MyRegex();
    [GeneratedRegex(@"\bchoose\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, "ru-RU")]
    private static partial Regex MyRegex1();
    [GeneratedRegex(@"\bscry\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, "ru-RU")]
    private static partial Regex MyRegex2();
    [GeneratedRegex(@"\bsurveil\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, "ru-RU")]
    private static partial Regex MyRegex3();
    [GeneratedRegex(@"\bdiscard\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, "ru-RU")]
    private static partial Regex MyRegex4();
    [GeneratedRegex(@"\bsacrifice\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, "ru-RU")]
    private static partial Regex MyRegex5();
    [GeneratedRegex(@"\bsearch\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, "ru-RU")]
    private static partial Regex MyRegex6();
    [GeneratedRegex(@"\breveal\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, "ru-RU")]
    private static partial Regex MyRegex7();
    [GeneratedRegex(@"\breturn\b.*\bfrom (?:your )?graveyard\b", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline, "ru-RU")]
    private static partial Regex MyRegex8();
    [GeneratedRegex(@"\bfight\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, "ru-RU")]
    private static partial Regex MyRegex9();
    [GeneratedRegex(@"\bkicker\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, "ru-RU")]
    private static partial Regex MyRegex10();
    [GeneratedRegex(@"\bmodal\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, "ru-RU")]
    private static partial Regex MyRegex11();
}
