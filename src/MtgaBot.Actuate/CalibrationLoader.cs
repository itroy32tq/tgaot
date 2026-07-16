using System.Text.Json;

namespace MtgaBot.Actuate;

public static class CalibrationLoader
{
    /// <summary>
    /// Loads calibration from Lotus-style JSON (<c>click_targets</c>) or our compact schema.
    /// Missing file / keys fall back to <see cref="CalibrationProfile.CreateDefault"/>.
    /// </summary>
    public static CalibrationProfile Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return CalibrationProfile.CreateDefault();
        }

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        return Parse(doc.RootElement);
    }

    public static CalibrationProfile Parse(JsonElement root)
    {
        var defaults = CalibrationProfile.CreateDefault();
        var targets = root.TryGetProperty("click_targets", out var ct) ? ct : root;

        return new CalibrationProfile(
            DesignWidth: ReadInt(root, "design_width", defaults.DesignWidth),
            DesignHeight: ReadInt(root, "design_height", defaults.DesignHeight),
            KeepHand: ReadPoint(targets, "keep_hand", defaults.KeepHand),
            Mulligan: ReadPoint(targets, "mulligan", defaults.Mulligan),
            Next: ReadPoint(targets, "next", defaults.Next),
            AttackAll: ReadPoint(targets, "attack_all", defaults.AttackAll),
            OpponentAvatar: ReadPoint(targets, "opponent_avatar", defaults.OpponentAvatar),
            HandScanP1: ReadNestedPoint(targets, "hand_scan_points", "p1", defaults.HandScanP1),
            HandScanP2: ReadNestedPoint(targets, "hand_scan_points", "p2", defaults.HandScanP2),
            HandScanStep: ReadInt(root, "hand_scan_step", defaults.HandScanStep));
    }

    private static DesignPoint ReadPoint(JsonElement parent, string name, DesignPoint fallback)
    {
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object)
        {
            return fallback;
        }

        var x = el.TryGetProperty("x", out var xEl) && xEl.TryGetInt32(out var xv) ? xv : fallback.X;
        var y = el.TryGetProperty("y", out var yEl) && yEl.TryGetInt32(out var yv) ? yv : fallback.Y;
        return new DesignPoint(x, y);
    }

    private static DesignPoint ReadNestedPoint(
        JsonElement parent,
        string objectName,
        string pointName,
        DesignPoint fallback)
    {
        if (!parent.TryGetProperty(objectName, out var obj) || obj.ValueKind != JsonValueKind.Object)
        {
            return fallback;
        }

        return ReadPoint(obj, pointName, fallback);
    }

    private static int ReadInt(JsonElement root, string name, int fallback) =>
        root.TryGetProperty(name, out var el) && el.TryGetInt32(out var value) ? value : fallback;
}
