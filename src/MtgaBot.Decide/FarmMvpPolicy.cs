using MtgaBot.State;

namespace MtgaBot.Decide;

/// <summary>
/// Conservative farm policy: land → safe creature/enchantment → attack all → pass.
/// Legal GRE actions are already filtered by mana; we do not re-check costs.
/// </summary>
public sealed class FarmMvpPolicy(CardPolicy? cardPolicy = null, FarmMvpMode mode = FarmMvpMode.FullMvp) : IPolicy
{
    public const string Name = "FarmMvp";

    private readonly CardPolicy _cardPolicy = cardPolicy ?? new CardPolicy();
    private readonly FarmMvpMode _mode = mode;
    private int _currentTurnNumber = -1;
    private bool _landAttemptedThisTurn;

    public FarmMvpMode Mode => _mode;

    public Intent Decide(GameView view, ICardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(cards);

        SyncTurn(view);

        return view.Decision.Kind switch
        {
            DecisionKind.Mulligan => new KeepHandIntent(true),
            DecisionKind.Attackers => _mode >= FarmMvpMode.FullMvp
                ? new AttackAllIntent()
                : new PassPriorityIntent(),
            DecisionKind.Blockers => new DeclareNoBlocksIntent(),
            DecisionKind.GroupReq => new AcknowledgeGroupIntent(),
            DecisionKind.SelectTargets => DecideSelectTarget(view),
            DecisionKind.MainPhase when IsMainPhase(view.Board.Turn) => DecideMainPhase(view, cards),
            DecisionKind.MainPhase => new PassPriorityIntent(),
            DecisionKind.PayCosts => new PassPriorityIntent(),
            DecisionKind.CastingTimeOptions => new PassPriorityIntent(),
            DecisionKind.AssignDamage => new PassPriorityIntent(),
            DecisionKind.SelectN => new PassPriorityIntent(),
            _ => new PassPriorityIntent(),
        };
    }

    private static bool IsMainPhase(TurnInfo turn) =>
        turn.Phase is "Phase_Main1" or "Phase_Main2";

    private Intent DecideMainPhase(GameView view, ICardDatabase cards)
    {
        var actions = view.Decision.LegalActions;
        if (actions.Count == 0)
        {
            return new PassPriorityIntent();
        }

        if (!_landAttemptedThisTurn)
        {
            var landIntent = TryPlayLand(view, actions);
            if (landIntent is not null)
            {
                return landIntent;
            }
        }

        if (_mode < FarmMvpMode.LandAndCast)
        {
            return new PassPriorityIntent();
        }

        var safeCast = PickSafeCast(view, cards);
        if (safeCast is not null)
        {
            return safeCast;
        }

        return new PassPriorityIntent();
    }

    private Intent? TryPlayLand(GameView view, IReadOnlyList<LegalAction> actions)
    {
        if (view.Decision.SystemSeatId != 0
            && view.Board.MySeatId != 0
            && view.Decision.SystemSeatId != view.Board.MySeatId)
        {
            return null;
        }

        LegalAction? playWithoutId = null;
        foreach (var action in actions)
        {
            if (!IsActionType(action.ActionType, "ActionType_Play"))
            {
                continue;
            }

            if (action.InstanceId is not { } landId)
            {
                playWithoutId = action;
                continue;
            }

            if (!view.Board.HandInstanceIds.Contains(landId))
            {
                continue;
            }

            _landAttemptedThisTurn = true;
            return new PlayLandIntent(landId);
        }

        if (playWithoutId is not null)
        {
            // Cannot aim without instanceId — skip land for this turn (do not fall through to Cast as "land").
            _landAttemptedThisTurn = true;
            return new NoOpIntent("Play without instanceId — skipped land");
        }

        return null;
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

    private void SyncTurn(GameView view)
    {
        var turnNumber = view.Board.Turn.TurnNumber;

        // New match / mulligan / turn rewind must clear land-once state.
        if (view.Decision.Kind == DecisionKind.Mulligan
            || turnNumber < _currentTurnNumber
            || _currentTurnNumber < 0)
        {
            _currentTurnNumber = turnNumber;
            _landAttemptedThisTurn = false;
            return;
        }

        if (turnNumber > _currentTurnNumber)
        {
            _currentTurnNumber = turnNumber;
            _landAttemptedThisTurn = false;
        }
    }

    private static bool IsActionType(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.Ordinal)
        || string.Equals(actual, expected.Replace("ActionType_", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

    private static int EstimateCmc(string? manaCost)
    {
        if (string.IsNullOrWhiteSpace(manaCost))
        {
            return 0;
        }

        // Rough CMC from symbols like "{2}{G}{G}" → 4
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
