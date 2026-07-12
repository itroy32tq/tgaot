namespace MtgaBot.Ingest;

public static class GreLogPatterns
{
    public const string GameState = "\"type\": \"GREMessageType_GameStateMessage\"";
    public const string QueuedGameState = "\"type\": \"GREMessageType_QueuedGameStateMessage\"";
    public const string TimerState = "\"type\": \"GREMessageType_TimerStateMessage\"";
    public const string HoverObjectId = "objectId";
    public const string MatchCompleted = "MatchGameRoomStateType_MatchCompleted";
    public const string AssignDamage = "\"type\": \"GREMessageType_AssignDamageReq\"";
    public const string DeclareAttackers = "\"type\": \"GREMessageType_DeclareAttackersReq\"";
    public const string DeclareBlockers = "\"type\": \"GREMessageType_DeclareBlockersReq\"";
    public const string SelectN = "\"type\": \"GREMessageType_SelectNReq\"";
    public const string GroupReq = "\"type\": \"GREMessageType_GroupReq\"";
    public const string SelectTargets = "\"type\": \"GREMessageType_SelectTargetsReq\"";
    public const string PayCosts = "\"type\": \"GREMessageType_PayCostsReq\"";
    public const string CastingTimeOptions = "\"type\": \"GREMessageType_CastingTimeOptionsReq\"";
    public const string Mulligan = "\"type\": \"GREMessageType_MulliganReq\"";
    public const string ActionsAvailable = "\"type\": \"GREMessageType_ActionsAvailableReq\"";
    public const string ClientSelectTargetsResp = "\"type\": \"ClientMessageType_SelectTargetsResp\"";
    public const string ClientSubmitAttackers = "\"type\": \"ClientMessageType_SubmitAttackersReq\"";
    public const string ClientSetSettings = "\"type\": \"ClientMessageType_SetSettingsReq\"";

    public static IReadOnlyList<string> All { get; } =
    [
        GameState,
        QueuedGameState,
        TimerState,
        HoverObjectId,
        MatchCompleted,
        AssignDamage,
        DeclareAttackers,
        DeclareBlockers,
        SelectN,
        GroupReq,
        SelectTargets,
        PayCosts,
        CastingTimeOptions,
        Mulligan,
        ActionsAvailable,
        ClientSelectTargetsResp,
        ClientSubmitAttackers,
        ClientSetSettings,
    ];
}
