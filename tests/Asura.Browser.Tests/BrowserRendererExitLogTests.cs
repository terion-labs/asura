namespace Asura.Browser.Tests;

public sealed class BrowserRendererExitLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "asura-renderer-exits-" + Guid.NewGuid().ToString("N"));

    public BrowserRendererExitLogTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ExitEvidenceSurvivesRestartAndRecordsWhetherNavigationWasActive()
    {
        var path = Path.Combine(_directory, "renderer-exits.log");
        new BrowserRendererExitLog(path).Record(12, 3, -9, navigating: true);
        new BrowserRendererExitLog(path).Record(13, 2, 5, navigating: false);

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        Assert.EndsWith("browser=12 status=3 exit=-9 navigating=1", lines[0]);
        Assert.EndsWith("browser=13 status=2 exit=5 navigating=0", lines[1]);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }

    [Fact]
    public void RepeatedFailuresCannotGrowTheLogWithoutBound()
    {
        var path = Path.Combine(_directory, "renderer-exits.log");
        File.WriteAllText(path, new string('x', BrowserRendererExitLog.MaximumBytes));

        new BrowserRendererExitLog(path).Record(1, 1, 9, navigating: false);

        Assert.True(new FileInfo(path).Length < BrowserRendererExitLog.MaximumBytes);
        Assert.EndsWith("browser=1 status=1 exit=9 navigating=0", Assert.Single(File.ReadAllLines(path)));
    }

    [Fact]
    public void UnwritableLogDoesNotPreventRecovery()
    {
        var path = Path.Combine(_directory, "missing", "renderer-exits.log");

        new BrowserRendererExitLog(path).Record(1, 2, 5, navigating: true);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void LogDoesNotOverwriteASymbolicLinkTarget()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var target = Path.Combine(_directory, "target");
        var path = Path.Combine(_directory, "renderer-exits.log");
        File.WriteAllText(target, "keep");
        File.CreateSymbolicLink(path, target);

        new BrowserRendererExitLog(path).Record(1, 2, 5, navigating: false);

        Assert.Equal("keep", File.ReadAllText(target));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
