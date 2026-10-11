using Asura.App.Controls;
using Asura.App.Views;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class AgentAttachmentFailureTextTests
{
    [Fact]
    public void ImportRefusalWrittenForTheComposerIsShownAsWritten()
    {
        var refusal = FailureReason.Readable(new InvalidOperationException("Choose files, not folders."));

        Assert.Equal("Choose files, not folders.", AgentAttachmentImport.FailureText(refusal));
    }

    [Fact]
    public void AttachmentRecordCheckNeverShowsItsParameterSuffix()
    {
        var files = Enumerable.Range(0, AgentFileAttachment.MaximumPerMessage + 1)
            .Select(index => new AgentFileAttachment(Guid.NewGuid().ToString("N"), $"file-{index}.txt", 1));
        var exception = Assert.Throws<ArgumentException>(() => AgentFileAttachment.CopyBatch(files));
        Assert.Contains("(Parameter", exception.Message, StringComparison.Ordinal);

        var text = AgentAttachmentImport.FailureText(exception);

        Assert.DoesNotContain("Parameter", text, StringComparison.Ordinal);
        Assert.Equal(
            "One of the files can’t be attached. Attach files with plain names, up to 50 MiB each and 200 MiB together.",
            text);
    }

    [Theory]
    [MemberData(nameof(LowerLayerFailures))]
    public void LowerLayerFailureIsReplacedWithANextStep(Exception exception, string expectedStart)
    {
        var text = AgentAttachmentImport.FailureText(exception);

        Assert.StartsWith(expectedStart, text, StringComparison.Ordinal);
        Assert.DoesNotContain("/private/secret", text, StringComparison.Ordinal);
        Assert.EndsWith(".", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerReportsTheFailuresItUsedToReport()
    {
        Assert.True(AgentAttachmentImport.IsAttachmentFailure(new ArgumentException("x")));
        Assert.True(AgentAttachmentImport.IsAttachmentFailure(new InvalidOperationException("x")));
        Assert.True(AgentAttachmentImport.IsAttachmentFailure(new IOException("x")));
        Assert.True(AgentAttachmentImport.IsAttachmentFailure(new UnauthorizedAccessException("x")));
        Assert.True(AgentAttachmentImport.IsAttachmentFailure(new TimeoutException("x")));
        Assert.False(AgentAttachmentImport.IsAttachmentFailure(new OperationCanceledException()));
    }

    public static TheoryData<Exception, string> LowerLayerFailures() => new()
    {
        { new FileNotFoundException("Could not find file '/private/secret/notes.txt'."), "One of the files is no longer there." },
        { new UnauthorizedAccessException("Access to the path '/private/secret' is denied."), "Asura doesn’t have permission" },
        { new IOException("The process cannot access the file '/private/secret' because it is being used."), "Couldn’t read or save one of the files." },
        { new TimeoutException("The clipboard request at /private/secret timed out."), "The clipboard didn’t respond in time." },
        { new InvalidOperationException("Sequence contains no elements /private/secret"), "Asura couldn’t attach the files." },
    };
}
