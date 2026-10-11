using Asura.App.ViewModels;
using Asura.Application;

namespace Asura.App.Tests;

public sealed class WorkspaceIsolationTextTests
{
    public static TheoryData<WorkspaceIsolationErrorCode> Codes()
    {
        var data = new TheoryData<WorkspaceIsolationErrorCode>();
        foreach (var code in Enum.GetValues<WorkspaceIsolationErrorCode>()
            .Where(code => code != WorkspaceIsolationErrorCode.None))
        {
            data.Add(code);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void EveryDefaultMessageIsReplacedWithTheSettingsWords(WorkspaceIsolationErrorCode code)
    {
        var error = WorkspaceIsolationError.Create(code);

        var text = WorkspaceIsolationText.Message(error);

        Assert.NotEqual(error.Message, text, StringComparer.Ordinal);
        Assert.DoesNotContain("isolate ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("isolate.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OCI", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/sbin/init", text, StringComparison.Ordinal);
        Assert.EndsWith(".", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AMessageAProviderWroteForOneCaseIsKept()
    {
        var error = new WorkspaceIsolationError(
            WorkspaceIsolationErrorCode.PrepareFailed,
            "workspace_sdk_prepare_failed",
            "The connection service couldn't start, and Asura couldn't remove its private files. Restart Asura, then try again.",
            true,
            WorkspaceIsolationRecoveryAction.Retry);

        Assert.Equal(error.Message, WorkspaceIsolationText.Message(error));
    }

    [Fact]
    public void ACodeWithoutADefaultMessageKeepsItsOwnMessage()
    {
        var error = new WorkspaceIsolationError(
            WorkspaceIsolationErrorCode.None,
            "workspace_isolation_none",
            "Asura couldn't start the workspace environment. Try again.",
            false,
            WorkspaceIsolationRecoveryAction.None);

        Assert.Equal(error.Message, WorkspaceIsolationText.Message(error));
    }

    [Fact]
    public void ARecreateRequestNamesTheButtonInTheWorkspaceSettings()
    {
        var text = WorkspaceIsolationText.Message(
            WorkspaceIsolationError.Create(WorkspaceIsolationErrorCode.PersistentEnvironmentResetRequired));

        Assert.Contains("Recreate Environment", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AFolderThatIsNotSharedIsNamedAsTheSettingsNameIt()
    {
        var text = WorkspaceIsolationText.Message(
            WorkspaceIsolationError.Create(WorkspaceIsolationErrorCode.WorkingDirectoryNotMounted));

        Assert.Contains("Shared folders", text, StringComparison.Ordinal);
        Assert.DoesNotContain("directory", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnlyAFailureWhoseStepIsTryAgainHasAReasonBeforeRetry()
    {
        Assert.Equal(
            "Asura couldn’t prepare the workspace environment.",
            WorkspaceIsolationText.ReasonBeforeRetry(WorkspaceIsolationError.Create(WorkspaceIsolationErrorCode.PrepareFailed)));
        Assert.Null(WorkspaceIsolationText.ReasonBeforeRetry(
            WorkspaceIsolationError.Create(WorkspaceIsolationErrorCode.RuntimeUnavailable)));
        Assert.Null(WorkspaceIsolationText.ReasonBeforeRetry(new WorkspaceIsolationError(
            WorkspaceIsolationErrorCode.PrepareFailed,
            "workspace_sdk_prepare_failed",
            "The connection service couldn't start, and Asura couldn't remove its private files. Restart Asura, then try again.",
            true,
            WorkspaceIsolationRecoveryAction.Retry)));
    }
}
