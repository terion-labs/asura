using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Asura.HeadlessDispatch.Tests;

/// <summary>
/// <c>HeadlessUnitTestSession.Dispatch</c> awaits a dispatched body only when
/// the body returns a value. An <c>async () =&gt; { … }</c> body that returns
/// nothing binds to <c>Dispatch&lt;Task&gt;</c>, which hands the body's task
/// back unawaited: the test passes while nothing after the body's first
/// await runs, and a failing assertion there is never seen.
/// Every test project that uses Avalonia.Headless links this file and so
/// checks its own assembly; an architecture test keeps that true.
/// </summary>
public sealed class HeadlessDispatchContractTests
{
    [Fact]
    public void NoDispatchedTestBodyReturnsAnUnawaitedTask()
    {
        using var stream = File.OpenRead(typeof(HeadlessDispatchContractTests).Assembly.Location);
        using var portableExecutable = new PEReader(stream);
        var metadata = portableExecutable.GetMetadataReader();

        var unawaited = new List<string>();
        var rows = metadata.GetTableRowCount(TableIndex.MethodSpec);
        for (var row = 1; row <= rows; row++)
        {
            var specification = metadata.GetMethodSpecification(MetadataTokens.MethodSpecificationHandle(row));
            if (specification.Method.Kind != HandleKind.MemberReference)
            {
                continue;
            }

            var method = metadata.GetMemberReference((MemberReferenceHandle)specification.Method);
            if (!string.Equals(metadata.GetString(method.Name), "Dispatch", StringComparison.Ordinal)
                || method.Parent.Kind != HandleKind.TypeReference
                || !string.Equals(
                    metadata.GetString(metadata.GetTypeReference((TypeReferenceHandle)method.Parent).Name),
                    "HeadlessUnitTestSession",
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (FirstTypeArgument(metadata, specification) is { } argument
                && string.Equals(argument.Namespace, "System.Threading.Tasks", StringComparison.Ordinal)
                && argument.Name is "Task" or "Task`1" or "ValueTask" or "ValueTask`1")
            {
                unawaited.Add($"Dispatch<{argument.Name}>");
            }
        }

        Assert.True(
            unawaited.Count == 0,
            "A dispatched test body returns a task the session does not await: "
            + string.Join(", ", unawaited)
            + ". Return a value from the body, as in `Assert.True(await session.Dispatch(async () => { …; return true; }, token))`.");
    }

    private static (string Namespace, string Name)? FirstTypeArgument(
        MetadataReader metadata,
        MethodSpecification specification)
    {
        var signature = metadata.GetBlobReader(specification.Signature);
        if (signature.ReadSignatureHeader().Kind != SignatureKind.MethodSpecification
            || signature.ReadCompressedInteger() < 1)
        {
            return null;
        }

        var code = signature.ReadSignatureTypeCode();
        if (code == SignatureTypeCode.GenericTypeInstance)
        {
            code = signature.ReadSignatureTypeCode();
        }

        if (code != SignatureTypeCode.TypeHandle)
        {
            return null;
        }

        var type = signature.ReadTypeHandle();
        return type.Kind == HandleKind.TypeReference
            && metadata.GetTypeReference((TypeReferenceHandle)type) is var reference
                ? (metadata.GetString(reference.Namespace), metadata.GetString(reference.Name))
                : null;
    }
}
