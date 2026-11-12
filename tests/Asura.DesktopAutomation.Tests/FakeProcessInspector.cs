namespace Asura.DesktopAutomation.Tests;

/// <summary>A process table the guard tests write by hand.</summary>
internal sealed class FakeProcessInspector : IAgentDesktopProcessInspector
{
    private readonly Dictionary<uint, (string? Path, uint? Parent)> _processes = [];

    public FakeProcessInspector With(uint processId, string? path, uint? parent = 1)
    {
        _processes[processId] = (path, parent);
        return this;
    }

    public string? GetExecutablePath(uint processId) =>
        _processes.TryGetValue(processId, out var process) ? process.Path : null;

    public uint? GetParentProcessId(uint processId) =>
        _processes.TryGetValue(processId, out var process) ? process.Parent : null;
}
