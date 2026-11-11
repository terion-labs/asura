using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Xml.Linq;
using Asura.Agent.Runtime;
using Asura.Application;
using Asura.Core;
using Asura.SessionHost;

namespace Asura.Architecture.Tests;

/// <summary>
/// ADR 0061 runs agent-desktop inside the desktop process, so the only
/// thing between a model and another application's controls is the shape of
/// the code: one interop project owns the native library, SessionHost alone
/// drives it after consuming a permit, and the runtime reaches it only
/// through the governed host surface. These tests read the sources and the
/// compiled metadata of the shipped assemblies to keep that shape.
/// </summary>
public sealed class AgentDesktopExecutionBoundaryTests
{
    private const string InteropAssembly = "Asura.DesktopAutomation";
    private const string SessionHostAssembly = "Asura.SessionHost";
    private const string EngineInterface = nameof(IAgentDesktopAutomationEngine);
    private const string HostDispatchSource =
        "src/Asura.SessionHost/InMemorySessionHostClient.AgentDesktop.cs";

    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Only_the_interop_project_names_an_agent_desktop_entrypoint_or_library()
    {
        var namingSources = ProductionSources()
            .Where(file => NamesNativeEntrypoint(File.ReadAllText(file)))
            .Select(RepositoryPath)
            .ToArray();

        Assert.NotEmpty(namingSources);
        Assert.All(
            namingSources,
            path => Assert.StartsWith(
                "src/Asura.DesktopAutomation/",
                path,
                StringComparison.Ordinal));

        foreach (var assembly in ProductionAssemblies())
        {
            using var metadata = OpenMetadata(assembly.Path);
            var reader = metadata.GetMetadataReader();
            var nativeNames = UserStrings(reader)
                .Where(value => IsEntrypointName(value)
                    || value.Contains("agent_desktop_ffi", StringComparison.Ordinal))
                .Concat(PlatformImports(reader).Where(IsEntrypointName))
                .ToArray();
            if (!string.Equals(assembly.Name, InteropAssembly, StringComparison.Ordinal))
            {
                Assert.True(
                    nativeNames.Length == 0,
                    $"{assembly.Name} names agent-desktop natives: {string.Join(", ", nativeNames)}");
            }
            else
            {
                Assert.Contains("ad_execute_by_ref_timeout", nativeNames, StringComparer.Ordinal);
            }
        }
    }

    [Fact]
    public void Only_session_host_calls_the_automation_engine()
    {
        var callers = new List<string>();
        var referrers = new List<string>();
        foreach (var assembly in ProductionAssemblies())
        {
            using var metadata = OpenMetadata(assembly.Path);
            var reader = metadata.GetMetadataReader();
            var engineTypes = TypeReferences(reader, "Asura.Application", EngineInterface);
            if (engineTypes.Count == 0)
            {
                continue;
            }

            referrers.Add(assembly.Name);
            if (reader.MemberReferences.Any(handle =>
                    engineTypes.Contains(reader.GetMemberReference(handle).Parent)))
            {
                callers.Add(assembly.Name);
            }
        }

        Assert.Equal([SessionHostAssembly], callers);
        // The composition root registers it, the interop implements it, and
        // SessionHost holds it; nothing else may even name it.
        Assert.Equal(
            ["Asura", InteropAssembly, SessionHostAssembly],
            referrers.Order(StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    [Fact]
    public void Agent_runtime_reaches_the_desktop_only_through_the_governed_host()
    {
        var runtimeAssembly = typeof(GovernedAgentRuntime).Assembly;
        Assert.DoesNotContain(
            runtimeAssembly.GetReferencedAssemblies(),
            reference => string.Equals(reference.Name, InteropAssembly, StringComparison.Ordinal));
        Assert.DoesNotContain(
            XDocument.Load(Path.Combine(RepositoryRoot, "src/Asura.Agent.Runtime/Asura.Agent.Runtime.csproj"))
                .Descendants("ProjectReference"),
            reference => ((string?)reference.Attribute("Include"))?.Contains(
                InteropAssembly,
                StringComparison.Ordinal) == true);

        using var metadata = OpenMetadata(runtimeAssembly.Location);
        var reader = metadata.GetMetadataReader();
        Assert.Empty(TypeReferences(reader, "Asura.Application", EngineInterface));
        Assert.NotEmpty(TypeReferences(reader, "Asura.Application", nameof(IAgentDesktopSessionHost)));
        Assert.DoesNotContain(
            ProductionSources("src/Asura.Agent.Runtime"),
            file => File.ReadAllText(file).Contains(EngineInterface, StringComparison.Ordinal)
                || File.ReadAllText(file).Contains(InteropAssembly, StringComparison.Ordinal));

        var hostParameters = typeof(GovernedAgentRuntime)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        Assert.Contains(typeof(IAgentDesktopSessionHost), hostParameters);
        Assert.Contains(typeof(AgentDesktopActionComposer), hostParameters);
        Assert.DoesNotContain(typeof(IAgentDesktopAutomationEngine), hostParameters);
    }

    [Fact]
    public void Desktop_host_surface_accepts_only_prepared_actions_and_authorization()
    {
        var methods = typeof(IAgentDesktopSessionHost)
            .GetMethods()
            .OrderBy(method => method.Name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                nameof(IAgentDesktopSessionHost.CheckAgentDesktopTargetAsync),
                nameof(IAgentDesktopSessionHost.GetAgentDesktopAccessAsync),
                nameof(IAgentDesktopSessionHost.ReleaseAgentDesktopRun),
                nameof(IAgentDesktopSessionHost.RunAgentDesktopActionAsync),
            ],
            methods.Select(method => method.Name),
            StringComparer.Ordinal);
        Assert.Equal(
            [typeof(AgentDesktopAction), typeof(CancellationToken)],
            Parameters(methods[0]));
        Assert.Equal([typeof(CancellationToken)], Parameters(methods[1]));
        Assert.Equal([typeof(AgentRunId)], Parameters(methods[2]));
        Assert.Equal(
            [typeof(AgentAuthorizationId), typeof(AgentDesktopAction), typeof(CancellationToken)],
            Parameters(methods[3]));

        var concrete = typeof(InMemorySessionHostClient)
            .GetMethods()
            .Where(method => method.Name.Contains("AgentDesktop", StringComparison.Ordinal))
            .Select(method => method.Name)
            .Order(StringComparer.Ordinal);
        Assert.Equal(
            methods.Select(method => method.Name),
            concrete,
            StringComparer.Ordinal);
        Assert.All(
            typeof(InMemorySessionHostClient)
                .GetMethods()
                .Where(method => method.Name.Contains("AgentDesktop", StringComparison.Ordinal))
                .SelectMany(method => method.GetParameters()),
            parameter => Assert.False(IsForbiddenExecutionParameter(parameter.ParameterType)));
    }

    [Fact]
    public void Desktop_actions_are_created_only_by_the_trusted_composer()
    {
        Assert.True(typeof(AgentDesktopAction).IsSealed);
        Assert.Empty(typeof(AgentDesktopAction).GetConstructors());
        Assert.All(
            typeof(AgentDesktopAction).GetProperties(),
            property => Assert.Null(property.SetMethod));

        var constructingSources = ProductionSources()
            .Where(file => File.ReadAllText(file).Contains(
                "new AgentDesktopAction(",
                StringComparison.Ordinal))
            .Select(RepositoryPath)
            .ToArray();
        Assert.Equal(["src/Asura.Application/AgentDesktopActionComposer.cs"], constructingSources);

        // Asura.Application grants internals to a few production assemblies;
        // none of them may use that to mint an action.
        foreach (var assembly in ProductionAssemblies())
        {
            using var metadata = OpenMetadata(assembly.Path);
            var reader = metadata.GetMetadataReader();
            var actionTypes = TypeReferences(reader, "Asura.Application", nameof(AgentDesktopAction));
            Assert.DoesNotContain(
                reader.MemberReferences.Select(reader.GetMemberReference),
                member => actionTypes.Contains(member.Parent)
                    && string.Equals(reader.GetString(member.Name), ".ctor", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Desktop_requests_carry_no_approval_or_authorization_identity()
    {
        Type[] intendedRequestTypes =
        [
            typeof(AgentDesktopRequest.ListApps),
            typeof(AgentDesktopRequest.ListWindows),
            typeof(AgentDesktopRequest.Snapshot),
            typeof(AgentDesktopRequest.Wait),
            typeof(AgentDesktopRequest.Click),
            typeof(AgentDesktopRequest.SetValue),
            typeof(AgentDesktopRequest.TypeText),
            typeof(AgentDesktopRequest.Clear),
            typeof(AgentDesktopRequest.Select),
            typeof(AgentDesktopRequest.Toggle),
            typeof(AgentDesktopRequest.Expand),
            typeof(AgentDesktopRequest.Scroll),
            typeof(AgentDesktopRequest.PressKey),
            typeof(AgentDesktopRequest.FocusWindow),
            typeof(AgentDesktopRequest.LaunchApp),
            typeof(AgentDesktopRequest.CloseApp),
        ];
        var requestTypes = typeof(AgentDesktopRequest)
            .GetNestedTypes()
            .Where(type => !type.IsAbstract)
            .ToArray();

        Assert.Equal(
            intendedRequestTypes.OrderBy(type => type.FullName, StringComparer.Ordinal),
            requestTypes.OrderBy(type => type.FullName, StringComparer.Ordinal));
        Assert.All(
            [.. requestTypes, typeof(AgentDesktopElementTarget), typeof(AgentDesktopWindowTarget)],
            type =>
            {
                Assert.DoesNotContain(
                    type.GetProperties(),
                    property => IsAuthorityIdentity(property.PropertyType));
                Assert.DoesNotContain(
                    type.GetConstructors().SelectMany(constructor => constructor.GetParameters()),
                    parameter => IsAuthorityIdentity(parameter.ParameterType));
            });
    }

    [Fact]
    public void Host_dispatch_consumes_the_permit_before_its_one_engine_call_and_never_retries()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot, HostDispatchSource));
        var run = MethodBody(source, "RunAgentDesktopActionAsync(");
        var dispatch = MethodBody(source, "DispatchAgentDesktopActionAsync(");

        var consume = run.IndexOf(".ConsumeAsync(", StringComparison.Ordinal);
        var dispatchCall = run.IndexOf("DispatchAgentDesktopActionAsync(", StringComparison.Ordinal);
        Assert.True(consume >= 0, "The desktop dispatch path no longer consumes a permit.");
        Assert.True(
            dispatchCall > consume,
            "The desktop engine is reachable before the permit is consumed.");

        var guard = dispatch.IndexOf("GuardAgentDesktopTargetAsync(", StringComparison.Ordinal);
        var execute = dispatch.IndexOf(".ExecuteAsync(", StringComparison.Ordinal);
        Assert.True(guard >= 0, "The self-target guard is not repeated after permit consumption.");
        Assert.True(execute > guard, "The engine is called before the repeated self-target guard.");

        var hostSources = ProductionSources("src/Asura.SessionHost")
            .Select(File.ReadAllText)
            .ToArray();
        Assert.Equal(1, hostSources.Sum(text => EngineCalls(text, "ExecuteAsync")));
        Assert.Equal(1, EngineCalls(dispatch, "ExecuteAsync"));
        // One definition and one call: nothing else can enter the commit region.
        Assert.Equal(2, hostSources.Sum(text => Occurrences(text, "DispatchAgentDesktopActionAsync(")));

        string[] loopTokens = ["for (", "foreach (", "while (", "do {", "goto ", "Retry"];
        foreach (var body in new[] { run, dispatch })
        {
            Assert.All(
                loopTokens,
                token => Assert.DoesNotContain(token, body, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void No_production_code_reads_the_environment_to_locate_the_library()
    {
        var library = File.ReadAllText(
            Path.Combine(RepositoryRoot, "src/Asura.DesktopAutomation/AgentDesktopLibrary.cs"));
        Assert.Contains("AppContext.BaseDirectory", library, StringComparison.Ordinal);

        foreach (var file in ProductionSources())
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("ASURA_AGENT_DESKTOP", source, StringComparison.Ordinal);
            if (RepositoryPath(file).StartsWith("src/Asura.DesktopAutomation/", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("GetEnvironmentVariable", source, StringComparison.Ordinal);
                Assert.DoesNotContain("ResolvingUnmanagedDll", source, StringComparison.Ordinal);
                Assert.DoesNotContain("SetDllImportResolver", source, StringComparison.Ordinal);
            }
        }

        var interop = ProductionAssemblies().Single(assembly =>
            string.Equals(assembly.Name, InteropAssembly, StringComparison.Ordinal));
        using var metadata = OpenMetadata(interop.Path);
        var reader = metadata.GetMetadataReader();
        Assert.DoesNotContain(
            reader.MemberReferences.Select(handle => reader.GetString(reader.GetMemberReference(handle).Name)),
            name => name.StartsWith("GetEnvironmentVariable", StringComparison.Ordinal));
        Assert.DoesNotContain(
            PlatformImports(reader),
            name => name.Contains("getenv", StringComparison.Ordinal));
    }

    private static Type[] Parameters(System.Reflection.MethodInfo method) =>
        [.. method.GetParameters().Select(parameter => parameter.ParameterType)];

    private static bool IsForbiddenExecutionParameter(Type type) =>
        type == typeof(AgentActionPermit)
        || type == typeof(AgentActionProposal)
        || type == typeof(string)
        || type == typeof(object)
        || type == typeof(JsonElement)
        || typeof(Delegate).IsAssignableFrom(type);

    private static bool IsAuthorityIdentity(Type type) =>
        type == typeof(AttachmentId)
        || type == typeof(ClientId)
        || type == typeof(AgentApprovalId)
        || type == typeof(AgentAuthorizationId)
        || type == typeof(AgentActionPermit);

    private static bool NamesNativeEntrypoint(string source)
    {
        if (source.Contains("agent_desktop_ffi", StringComparison.Ordinal))
        {
            return true;
        }

        for (var index = source.IndexOf("\"ad_", StringComparison.Ordinal);
             index >= 0;
             index = source.IndexOf("\"ad_", index + 1, StringComparison.Ordinal))
        {
            var end = source.IndexOf('"', index + 1);
            if (end > index && IsEntrypointName(source[(index + 1)..end]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEntrypointName(string value) =>
        value.Length > 3
        && value.StartsWith("ad_", StringComparison.Ordinal)
        && value.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

    private static int Occurrences(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Counts calls of <paramref name="member"/> on the host's engine field,
    /// however the call is wrapped across lines.
    /// </summary>
    private static int EngineCalls(string text, string member)
    {
        const string field = "_agentDesktopAutomationEngine";
        var count = 0;
        for (var index = text.IndexOf(field, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(field, index + field.Length, StringComparison.Ordinal))
        {
            var rest = text[(index + field.Length)..].TrimStart('!', '?').TrimStart();
            if (rest.StartsWith("." + member + "(", StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>The body of the first method whose declaration contains <paramref name="signature"/>.</summary>
    private static string MethodBody(string source, string signature)
    {
        var declaration = source.IndexOf("ValueTask<HostResult<AgentDesktopResult>> " + signature, StringComparison.Ordinal);
        Assert.True(declaration >= 0, $"The desktop host no longer declares {signature}.");
        var open = source.IndexOf('{', declaration);
        var depth = 0;
        for (var index = open; index < source.Length; index++)
        {
            depth += source[index] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0,
            };
            if (depth == 0)
            {
                return source[open..(index + 1)];
            }
        }

        throw new InvalidDataException($"The body of {signature} is not balanced.");
    }

    private static HashSet<EntityHandle> TypeReferences(MetadataReader reader, string ns, string name) =>
        [.. reader.TypeReferences
            .Where(handle =>
            {
                var reference = reader.GetTypeReference(handle);
                return string.Equals(reader.GetString(reference.Namespace), ns, StringComparison.Ordinal)
                    && string.Equals(reader.GetString(reference.Name), name, StringComparison.Ordinal);
            })
            .Select(handle => (EntityHandle)handle)];

    private static IEnumerable<string> UserStrings(MetadataReader reader)
    {
        for (var handle = MetadataTokens.UserStringHandle(1);
             !handle.IsNil;
             handle = reader.GetNextHandle(handle))
        {
            yield return reader.GetUserString(handle);
        }
    }

    private static IEnumerable<string> PlatformImports(MetadataReader reader) =>
        reader.MethodDefinitions
            .Select(reader.GetMethodDefinition)
            .Select(method => method.GetImport())
            .Where(import => !import.Module.IsNil)
            .Select(import => reader.GetString(import.Name));

    private static PEReader OpenMetadata(string path) =>
        new(File.OpenRead(path));

    /// <summary>
    /// Every assembly built from src/, located beside this test where the
    /// project closure copied it. The composition root's assembly is named
    /// "Asura".
    /// </summary>
    private static IEnumerable<(string Name, string Path)> ProductionAssemblies() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !HasPathSegment(path, "bin") && !HasPathSegment(path, "obj"))
            .Select(path => (string?)XDocument.Load(path).Descendants("AssemblyName").FirstOrDefault()
                ?? Path.GetFileNameWithoutExtension(path))
            .Select(name => (name, Path.Combine(AppContext.BaseDirectory, name + ".dll")))
            .Where(assembly => File.Exists(assembly.Item2));

    private static IEnumerable<string> ProductionSources(string root = "src") =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot, root), "*.cs", SearchOption.AllDirectories)
            .Where(path => !HasPathSegment(path, "bin") && !HasPathSegment(path, "obj"));

    private static string RepositoryPath(string path) =>
        Path.GetRelativePath(RepositoryRoot, path).Replace('\\', '/');

    private static bool HasPathSegment(string path, string segment) =>
        path.Split(Path.DirectorySeparatorChar).Contains(segment, StringComparer.Ordinal);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Asura.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Unable to locate the Asura repository root.");
    }
}
