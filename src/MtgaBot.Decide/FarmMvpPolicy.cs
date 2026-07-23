using MtgaBot.State;

namespace MtgaBot.Decide;

/// <summary>
/// Conservative farm policy: land → safe creature/enchantment → attack all → pass.
/// Legal GRE actions are already filtered by mana; we do not re-check costs.
/// </summary>
public sealed class FarmMvpPolicy(CardPolicy? cardPolicy = null, FarmMvpMode mode = FarmMvpMode.FullMvp) : IPolicy
{
    public const string Name = "FarmMvp";
    public const string WaitingMain1PriorityReason = "waiting-our-main1-priority";

    private readonly CardPolicy _cardPolicy = cardPolicy ?? new CardPolicy();
    private readonly FarmMvpMode _mode = mode;
    private int _currentTurnNumber = -1;
    /// <summary>
    /// Land drop finished for this turn (GRE-confirmed or explicitly settled).
    /// Not set by Decide alone — live must call <see cref="NotifyLandSettled"/> /
    /// <see cref="NotifyLandActuateStarted"/> so a skipped actuate cannot burn the turn.
    /// </summary>
    private bool _landSettledThisTurn;

    public FarmMvpMode Mode => _mode;

    public bool LandSettledThisTurn => _landSettledThisTurn;

    /// <summary>Live started a land drag — do not pick another land until retry/turn change.</summary>
    public void NotifyLandActuateStarted() => _landSettledThisTurn = true;

    /// <summary>GRE confirmed the land left hand (or we intentionally skip land this turn).</summary>
    public void NotifyLandSettled() => _landSettledThisTurn = true;

    /// <summary>Allow another PlayLand after a failed/cancelled drag.</summary>
    public void AllowLandRetry() => _landSettledThisTurn = false;

    public Intent Decide(GameView view, ICardDatabase cards)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(cards);

        SyncTurn(view);

        // Never click Pass/Next without priority — each useless Pass is ~0.9s and desyncs
        // the live loop from Player.log (T3+ land scan then cancels with ElapsedMs=0).
        Intent PassOrWait() => PriorityWindow.IsOurPriority(view.Board)
            ? new PassPriorityIntent()
            : new NoOpIntent("waiting-priority");

        return view.Decision.Kind switch
        {
            DecisionKind.Mulligan => new KeepHandIntent(true),
            DecisionKind.Attackers => _mode >= FarmMvpMode.FullMvp
                ? new AttackAllIntent()
                : PassOrWait(),
            DecisionKind.Blockers => new DeclareNoBlocksIntent(),
            DecisionKind.GroupReq => new AcknowledgeGroupIntent(),
            DecisionKind.SelectTargets => DecideSelectTarget(view),
            DecisionKind.MainPhase when IsMainPhase(view.Board.Turn) => DecideMainPhase(view, cards, PassOrWait),
            // Sticky MainPhase during our Beginning/Draw — never Pass (skips Main1).
            DecisionKind.MainPhase when IsOurBeginning(view.Board) =>
                new NoOpIntent("waiting-main1-after-beginning"),
            DecisionKind.MainPhase => PassOrWait(),
            DecisionKind.PayCosts => PassOrWait(),
            DecisionKind.CastingTimeOptions => PassOrWait(),
            DecisionKind.AssignDamage => PassOrWait(),
            DecisionKind.SelectN => PassOrWait(),
            _ => PassOrWait(),
        };
    }

    private static bool IsMainPhase(TurnInfo turn) =>
        turn.Phase is "Phase_Main1" or "Phase_Main2";

    private static bool IsOurBeginning(GameSnapshot board)
    {
        if (board.Turn.Phase != "Phase_Beginning" || board.Turn.TurnNumber <= 0)
        {
            return false;
        }

        var active = board.Turn.ActivePlayer;
        return active <= 0 || active == board.MySeatId;
    }

    private Intent DecideMainPhase(GameView view, ICardDatabase cards, Func<Intent> passOrWait)
    {
        var actions = view.Decision.LegalActions;
        if (actions.Count == 0)
        {
            return passOrWait();
        }

        if (!_landSettledThisTurn)
        {
            var landIntent = TryPlayLand(view, actions);
            if (landIntent is not null)
            {
                return landIntent;
            }

            // Our Main1 with a playable land, but not our priority yet.
            // Passing clicks Next and burns the land window — wait instead.
            if (PriorityWindow.IsOurTurnMain1(view.Board) && HasPlayableLandInHand(view, actions))
            {
                return new NoOpIntent(WaitingMain1PriorityReason);
            }
        }

        if (_mode < FarmMvpMode.LandAndCast)
        {
            return passOrWait();
        }

        var safeCast = PickSafeCast(view, cards);
        if (safeCast is not null)
        {
            return safeCast;
        }

        return passOrWait();
    }

    private Intent? TryPlayLand(GameView view, IReadOnlyList<LegalAction> actions)
    {
        if (!PriorityWindow.IsOurMain1(view.Board))
        {
            return null;
        }

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

            // Do not settle here — live settles on actuate start / GRE ack.
            return new PlayLandIntent(landId);
        }

        if (playWithoutId is not null)
        {
            // Cannot aim without instanceId — skip land for this turn.
            _landSettledThisTurn = true;
            return new NoOpIntent("Play without instanceId — skipped land");
        }

        return null;
    }

    public static bool HasPlayableLandInHand(GameView view, IReadOnlyList<LegalAction> actions)
    {
        foreach (var action in actions)
        {
            if (!IsActionType(action.ActionType, "ActionType_Play") || action.InstanceId is not { } landId)
            {
                continue;
            }

            if (view.Board.HandInstanceIds.Contains(landId))
            {
                return true;
            }
        }

        return false;
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
            _landSettledThisTurn = false;
            return;
        }

        if (turnNumber > _currentTurnNumber)
        {
            _currentTurnNumber = turnNumber;
            _landSettledThisTurn = false;
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
