using MtgaBot.Ingest;

namespace MtgaBot.Ingest.Tests;

public class GreLogTailerTests
{
    [Fact]
    public void ParseFile_WhenMissing_ThrowsFileNotFound()
    {
        var tailer = new GreLogTailer();

        Assert.Throws<FileNotFoundException>(() => tailer.ParseFile("missing.log"));
    }
}
