using Asura.App;
using Asura.Desktop;

namespace Asura.Architecture.Tests;

/// <summary>
/// <c>asura open PATH [--as terminal|files|git]</c>: a terminal by default, a
/// path relative to the shell's folder, and a refusal that says what is
/// wrong rather than an app that opens nothing.
/// </summary>
public sealed class OpenFolderCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "asura-open-folder-tests", Guid.NewGuid().ToString("N"));

    public OpenFolderCommandTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "plain"));
        Directory.CreateDirectory(Path.Combine(_root, "repository", ".git"));
        Directory.CreateDirectory(Path.Combine(_root, "repository", "src", "app"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void A_folder_opens_as_a_terminal_unless_another_view_is_named()
    {
        var plain = Path.Combine(_root, "plain");
        var invocation = OpenFolderCommand.Parse(["open", plain], _root);
        Assert.Equal(plain, invocation.Request.Path);
        Assert.Equal(OpenFolderView.Terminal, invocation.Request.View);
        Assert.False(invocation.WasLaunched);

        Assert.Equal(OpenFolderView.Files, OpenFolderCommand.Parse(["open", plain, "--as", "files"], _root).Request.View);
        Assert.Equal(OpenFolderView.Files, OpenFolderCommand.Parse(["open", "--as=Files", plain], _root).Request.View);
        Assert.True(OpenFolderCommand.Parse(["open", plain, OpenFolderCommand.LaunchedSwitch], _root).WasLaunched);
    }

    [Fact]
    public void A_relative_path_is_the_shells_folder_and_git_takes_any_folder_in_a_repository()
    {
        var invocation = OpenFolderCommand.Parse(["open", Path.Combine("src", "app"), "--as", "git"], Path.Combine(_root, "repository"));
        Assert.Equal(Path.Combine(_root, "repository", "src", "app"), invocation.Request.Path);
        Assert.Equal(OpenFolderView.Git, invocation.Request.View);
        Assert.Equal(Path.Combine(_root, "repository"), OpenFolderRequest.FindRepositoryRoot(invocation.Request.Path));
    }

    [Theory]
    [InlineData(new[] { "open" }, "Name the folder")]
    [InlineData(new[] { "open", "plain", "--as" }, "--as needs a view")]
    [InlineData(new[] { "open", "plain", "--as", "browser" }, "Unknown view browser")]
    [InlineData(new[] { "open", "plain", "--verbose" }, "Unknown option --verbose")]
    [InlineData(new[] { "open", "plain", "repository" }, "one folder at a time")]
    [InlineData(new[] { "open", "missing" }, "There is no folder")]
    [InlineData(new[] { "open", "plain", "--as", "git" }, "not in a Git repository")]
    public void A_command_that_cannot_open_says_why(string[] arguments, string message)
    {
        var error = Assert.Throws<ArgumentException>(() => OpenFolderCommand.Parse(arguments, _root));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_request_crosses_to_the_running_instance_intact()
    {
        var path = Path.Combine(_root, "plain");
        foreach (var view in new OpenFolderView?[] { null, OpenFolderView.Terminal, OpenFolderView.Files, OpenFolderView.Git })
        {
            var request = OpenFolderRequest.TryCreate(path, view)!;
            Assert.Equal(request, OpenFolderRequest.Decode(request.Encode()));
        }

        Assert.Null(OpenFolderRequest.Decode(null));
        Assert.Null(OpenFolderRequest.Decode(path));
        Assert.Null(OpenFolderRequest.Decode("browser\n" + path));
        Assert.Null(OpenFolderRequest.Decode("files\nrelative/path"));
        Assert.Null(OpenFolderRequest.TryCreate(path + "\u0007", OpenFolderView.Files));
        Assert.Null(OpenFolderRequest.TryCreate("/" + new string('a', OpenFolderRequest.MaximumPathLength), null));
    }
}
