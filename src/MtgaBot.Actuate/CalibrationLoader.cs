using System.Text.Json;

namespace MtgaBot.Actuate;

public static class CalibrationLoader
{
    public const string DefaultRelativePath = "data/calibration.defaults.json";

    /// <summary>
    /// Loads calibration from Lotus-style JSON (<c>click_targets</c>) or our compact schema.
    /// Missing file / keys fall back to <see cref="CalibrationProfile.CreateDefault"/>.
    /// When <paramref name="path"/> is null, searches <see cref="DefaultRelativePath"/>.
    /// </summary>
    public static CalibrationProfile Load(string? path = null)
    {
        var resolved = ResolvePath(path);
        if (resolved is null)
        {
            return CalibrationProfile.CreateDefault();
        }

        using var stream = File.OpenRead(resolved);
        using var doc = JsonDocument.Parse(stream);
        return Parse(doc.RootElement);
    }

    /// <summary>Resolved calibration file path, or null if using in-code defaults.</summary>
    public static string? ResolvePath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            var full = Path.GetFullPath(explicitPath);
            return File.Exists(full) ? full : null;
        }

        foreach (var candidate in EnumerateDefaultCandidates(DefaultRelativePath))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateDefaultCandidates(string relative)
    {
        yield return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relative));

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            yield return Path.GetFullPath(Path.Combine(dir.FullName, relative));
        }
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
            HandScanStep: ReadInt(root, "hand_scan_step", defaults.HandScanStep),
            LandDragUpDesign: ReadInt(root, "land_drag_up", defaults.LandDragUpDesign));
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
