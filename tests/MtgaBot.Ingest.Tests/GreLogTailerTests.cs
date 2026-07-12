using MtgaBot.Ingest;

namespace MtgaBot.Ingest.Tests;

public class GreLogTailerTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void ParseFile_WhenMissing_ThrowsFileNotFound()
    {
        var tailer = new GreLogTailer();

        Assert.Throws<FileNotFoundException>(() => tailer.ParseFile("missing.log"));
    }

    [Fact]
    public void ParseFile_BatchedMessages_ExpandsAllGreMessages()
    {
        var tailer = new GreLogTailer();
        var events = tailer.ParseFile(FixturePath("batched-messages.log"));

        Assert.Equal(2, events.Count);
        Assert.Equal("GREMessageType_GameStateMessage", events[0].Message.Type);
        Assert.Equal("GREMessageType_TimerStateMessage", events[1].Message.Type);
        Assert.Equal(GreTrafficDirection.GreToClient, events[0].Message.Direction);
    }

    [Fact]
    public void ParseFile_GoldenLog_IsDeterministic()
    {
        var tailer = new GreLogTailer();
        var path = FixturePath("hand-select-log_tail.txt");

        var first = tailer.ParseFile(path);
        var second = tailer.ParseFile(path);

        Assert.Equal(first.Count, second.Count);
        Assert.True(first.Count > 100);
        Assert.Contains(first, e => e.Message.Type == "GREMessageType_GameStateMessage");
        Assert.Contains(first, e => e.Message.Type == "GREMessageType_UIMessage");
    }

    [Fact]
    public void ParseFile_GoldenLog_AssignsMonotonicSequence()
    {
        var tailer = new GreLogTailer();
        var events = tailer.ParseFile(FixturePath("hand-select-log_tail.txt"));

        for (ulong expected = 1; expected <= (ulong)events.Count; expected++)
        {
            Assert.Equal(expected, events[(int)expected - 1].Sequence);
        }
    }

    [Fact]
    public void ParseFile_EmptyAndNonGreLines_AreIgnored()
    {
        var parser = new GreLogParser();
        var events = parser.ParseLines(
        [
            string.Empty,
            "Unity engine initialized",
            "   ",
        ]);

        Assert.Empty(events);
    }
}
