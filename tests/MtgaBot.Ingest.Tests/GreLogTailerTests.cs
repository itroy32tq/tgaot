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

    [Fact]
    public async Task TailLive_SeesLinesAppendedAfterEof()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mtgabot-tail-{Guid.NewGuid():N}.log");
        await File.WriteAllTextAsync(path, "bootstrap\n");

        try
        {
            var tailer = new GreLogTailer();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

            var consume = Task.Run(async () =>
            {
                await foreach (var evt in tailer.TailLive(path, cts.Token))
                {
                    return evt;
                }

                throw new InvalidOperationException("Tail ended without events.");
            }, cts.Token);

            // Let TailLive seek to EOF before we append.
            await Task.Delay(400);

            const string greLine =
                """{"timestamp":"1000","greToClientEvent":{"greToClientMessages":[{"type":"GREMessageType_TimerStateMessage","msgId":1,"timerStateMessage":{"seatId":1}}]}}""";
            await File.AppendAllTextAsync(path, greLine + Environment.NewLine);

            var got = await consume.WaitAsync(TimeSpan.FromSeconds(5), cts.Token);
            Assert.Equal("GREMessageType_TimerStateMessage", got.Message.Type);
            await cts.CancelAsync();
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // ignore cleanup races on Windows
            }
        }
    }
}
