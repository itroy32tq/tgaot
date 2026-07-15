using MtgaBot.State;

namespace MtgaBot.Decide;

/// <summary>
/// Conservative farm policy: land → safe creature/enchantment → attack all → pass.
/// Legal GRE actions are already filtered by mana; we do not re-check costs.
/// </summary>
public sealed class FarmMvpPolicy(CardPolicy? cardPolicy = null) : IPolicy
{
    public const string Name = "FarmMvp";

    private readonly CardPolicy _cardPolicy = cardPolicy ?? new CardPolicy();
    private int _currentTurnNumber;
    private bool _landPlayedThisTurn;

    public Intent Decide(GameView view, ICardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(cards);

        SyncTurn(view.Board.Turn.TurnNumber);

        return view.Decision.Kind switch
        {
            DecisionKind.Mulligan => new KeepHandIntent(true),
            DecisionKind.Attackers => new AttackAllIntent(),
            DecisionKind.Blockers => new DeclareNoBlocksIntent(),
            DecisionKind.GroupReq => new AcknowledgeGroupIntent(),
            DecisionKind.SelectTargets => DecideSelectTarget(view),
            DecisionKind.MainPhase => DecideMainPhase(view, cards),
            DecisionKind.PayCosts => new PassPriorityIntent(),
            DecisionKind.CastingTimeOptions => new PassPriorityIntent(),
            DecisionKind.AssignDamage => new PassPriorityIntent(),
            DecisionKind.SelectN => new PassPriorityIntent(),
            _ => new PassPriorityIntent(),
        };
    }

    private Intent DecideMainPhase(GameView view, ICardDatabase cards)
    {
        var actions = view.Decision.LegalActions;
        if (actions.Count == 0)
        {
            return new PassPriorityIntent();
        }

        if (!_landPlayedThisTurn)
        {
            var land = FindFirst(actions, "ActionType_Play");
            if (land?.InstanceId is { } landId)
            {
                _landPlayedThisTurn = true;
                return new CastIntent(landId);
            }
        }

        var safeCast = PickSafeCast(view, cards);
        if (safeCast is not null)
        {
            return safeCast;
        }

        return !HasPass(actions) ? new PassPriorityIntent() : new PassPriorityIntent();
    }

    private CastIntent? PickSafeCast(GameView view, ICardDatabase cards)
    {
        CastIntent? best = null;
        var bestScore = -1;

        foreach (var action in view.Decision.LegalActions)
        {
            if (!IsActionType(action.ActionType, "ActionType_Cast") || action.InstanceId is not int instanceId)
            {
                continue;
            }

            if (!view.Board.Objects.TryGetValue(instanceId, out var cardView))
            {
                continue;
            }

            if (!cards.TryGet(cardView.GrpId, out var cardInfo))
            {
                // Without cards.json we cannot classify permanents safely — skip.
                continue;
            }

            if (!_cardPolicy.IsSafePermanentToCast(cardInfo))
            {
                continue;
            }

            // Prefer higher CMC when available; fall back to instance id for stability.
            var score = EstimateCmc(cardInfo.ManaCost) * 1_000_000 + instanceId;
            if (score > bestScore)
            {
                bestScore = score;
                best = new CastIntent(instanceId);
            }
        }

        return best;
    }

    private static Intent DecideSelectTarget(GameView view)
    {
        var valid = view.Decision.Prompt?.ValidTargets;
        return valid is { Count: > 0 } ? new SelectTargetIntent(valid[0]) :
            // Convention from Burning Lotus: -1 ≈ opponent face when no list yet.
            new SelectTargetIntent(-1);
    }

    private void SyncTurn(int turnNumber)
    {
        if (turnNumber <= _currentTurnNumber) return;
        _currentTurnNumber = turnNumber;
        _landPlayedThisTurn = false;
    }

    private static LegalAction? FindFirst(IReadOnlyList<LegalAction> actions, string actionType) =>
        actions.FirstOrDefault(action => IsActionType(action.ActionType, actionType));

    private static bool HasPass(IReadOnlyList<LegalAction> actions) =>
        actions.Any(action =>
            IsActionType(action.ActionType, "ActionType_Pass")
            || string.Equals(action.ActionType, "Pass", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action.ActionType, "Prompt", StringComparison.OrdinalIgnoreCase));

    private static bool IsActionType(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.Ordinal)
        || string.Equals(actual, expected.Replace("ActionType_", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

    private static int EstimateCmc(string? manaCost)
    {
        if (string.IsNullOrWhiteSpace(manaCost))
        {
            return 0;
        }

        // RoughCMC from symbols like "{2}{G}{G}" → 4
        var cmc = 0;
        for (var i = 0; i < manaCost.Length; i++)
        {
            if (manaCost[i] != '{')
            {
                continue;
            }

            var end = manaCost.IndexOf('}', i + 1);
            if (end < 0)
            {
                break;
            }

            var symbol = manaCost[(i + 1)..end];
            if (int.TryParse(symbol, out var generic))
            {
                cmc += generic;
            }
            else if (symbol.Length > 0)
            {
                cmc += 1;
            }

            i = end;
        }

        return cmc;
    }
}
