using System.Text.Json;

namespace MtgaBot.Actuate.Tests;

public class CalibrationLoaderTests
{
    [Fact]
    public void Parse_LotusClickTargets()
    {
        const string json = """
                            {
                              "click_targets": {
                                "keep_hand": { "x": 1101, "y": 870 },
                                "next": { "x": 1755, "y": 944 },
                                "attack_all": { "x": 1755, "y": 944 },
                                "opponent_avatar": { "x": 1286, "y": 216 },
                                "hand_scan_points": {
                                  "p1": { "x": 0, "y": 1050 },
                                  "p2": { "x": 1920, "y": 1050 }
                                }
                              }
                            }
                            """;

        using var doc = JsonDocument.Parse(json);
        var profile = CalibrationLoader.Parse(doc.RootElement);

        Assert.Equal(1101, profile.KeepHand.X);
        Assert.Equal(1755, profile.Next.X);
        Assert.Equal(1050, profile.HandScanP1.Y);
        Assert.Equal(801, profile.Mulligan.X); // default fallback
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var profile = CalibrationLoader.Load(@"C:\no-such-calibration-file.json");
        Assert.Equal(1920, profile.DesignWidth);
        Assert.Equal(1101, profile.KeepHand.X);
    }

    [Fact]
    public void ResolvePath_FindsRepoDefaultsWhenCwdIsRepoRoot()
    {
        var path = CalibrationLoader.ResolvePath(null);
        if (path is null)
        {
            // CI / odd cwd — skip soft assert
            return;
        }

        Assert.True(File.Exists(path));
        Assert.Contains("calibration", path, StringComparison.OrdinalIgnoreCase);
    }
}
