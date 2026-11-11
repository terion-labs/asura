using System.Security.Cryptography;
using System.Text.Json;

namespace Asura.Packaging;

/// <summary>
/// Validates the agent-desktop library and helper inside a signed macOS
/// package against the receipt scripts/build-agent-desktop.sh wrote for them
/// (ADR 0061). Both are loaded or launched by Asura itself, so a package
/// carrying different program bytes, an evidence flavor, or a build with
/// another panic strategy must not pass as the reviewed one.
/// </summary>
/// <remarks>
/// The catalog's BLOCKED or READY state is not judged here: that is the
/// publication decision, not a property of the bytes.
/// </remarks>
internal static class AgentDesktopPackageProvenance
{
    private const int MaximumDocumentBytes = 1024 * 1024;
    private const string CatalogFormat =
        "asura-native-desktop-automation-component-catalog-v1";
    private const string ReceiptFormat = "asura-agent-desktop-build-receipt-v1";
    private const string CatalogFileName =
        "native-desktop-automation-components.json";
    private const string ReceiptFileName = "agent-desktop-build-receipt.json";
    private const string ExpectedTargetRid = "osx-arm64";
    private const string ReleaseFlavor = "release";
    // Any other strategy turns a Rust panic into an abort of the desktop
    // process instead of a fault the interop can trap.
    private const string RequiredPanicStrategy = "unwind";
    private const string LibraryFileName = "libagent_desktop_ffi.dylib";
    private const string HelperFileName = "agent-desktop-macos-helper";

    /// <summary>
    /// Validates receipt-bound content after macOS code signing. Signing
    /// rewrites both Mach-O files, so they are compared by the digest the
    /// receipt records for them with the signature removed; the release
    /// verifier separately checks the package's deep signature identity.
    /// </summary>
    public static void ValidateAfterCodeSigning(
        string executableDirectory,
        string licenseDirectory,
        string catalogPath,
        string receiptPath)
    {
        var catalogBytes = ReadDocument(catalogPath, "component catalog");
        var receiptBytes = ReadDocument(receiptPath, "build receipt");
        using var catalogDocument = Parse(catalogBytes, "component catalog");
        using var receiptDocument = Parse(receiptBytes, "build receipt");
        var catalog = catalogDocument.RootElement;
        var receipt = receiptDocument.RootElement;
        RequireFormat(catalog, CatalogFormat, "component catalog");
        RequireFormat(receipt, ReceiptFormat, "build receipt");

        var nativeLicenseDirectory = Path.Combine(licenseDirectory, "Native");
        RequireExactCopy(
            catalogBytes,
            Path.Combine(nativeLicenseDirectory, CatalogFileName),
            "component catalog");
        RequireExactCopy(
            receiptBytes,
            Path.Combine(nativeLicenseDirectory, ReceiptFileName),
            "build receipt");

        RequireString(
            receipt,
            "catalogSha256",
            Convert.ToHexStringLower(SHA256.HashData(catalogBytes)));
        RequireString(receipt, "targetRid", ExpectedTargetRid);
        RequireString(receipt, "flavor", ReleaseFlavor);

        var component = RequireObject(catalog, "component");
        RequireString(
            RequireObject(receipt, "source"),
            "commit",
            RequireString(component, "sourceCommit"));
        if (RequireInt64(receipt, "abiMajor") != RequireInt64(component, "abiMajor"))
        {
            throw new InvalidDataException(
                "The desktop automation build receipt has an unexpected abiMajor value.");
        }

        var reviewedToolchain = RequireObject(catalog, "toolchain");
        var toolchain = RequireObject(receipt, "toolchain");
        RequireString(reviewedToolchain, "panicStrategy", RequiredPanicStrategy);
        RequireString(toolchain, "panicStrategy", RequiredPanicStrategy);
        RequireString(
            toolchain,
            "profile",
            RequireString(reviewedToolchain, "cargoProfile"));
        RequireString(
            toolchain,
            "rustcCommit",
            RequireString(reviewedToolchain, "rustcCommit"));
        RequireBoolean(
            RequireObject(receipt, "abi"),
            "panicInjectionExport",
            expected: false);

        RequireString(
            RequireObject(catalog, "helper"),
            "macosExecutable",
            HelperFileName);
        ValidateSignedCode(
            executableDirectory,
            RequireObject(receipt, "library"),
            LibraryFileName);
        ValidateSignedCode(
            executableDirectory,
            RequireObject(receipt, "helper"),
            HelperFileName);

        foreach (var legalFile in new[] { "license", "thirdPartyNotices", "standardLibraryNotices" })
        {
            ValidateLegalFile(licenseDirectory, RequireObject(receipt, legalFile));
        }
    }

    private static void ValidateSignedCode(
        string executableDirectory,
        JsonElement expected,
        string fileName)
    {
        RequireString(expected, "file", fileName);
        if (RequireInt64(expected, "bytes") == 0)
        {
            throw new InvalidDataException(
                $"The desktop automation build receipt records an empty {fileName}.");
        }

        RequireSha256(RequireString(expected, "sha256"), fileName);
        var contentSha256 = RequireString(expected, "signatureRemovedSha256");
        RequireSha256(contentSha256, fileName);

        var path = Path.Combine(executableDirectory, fileName);
        var info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget is not null || info.Length == 0)
        {
            throw new InvalidDataException(
                $"The signed packaged {fileName} is missing, linked, or empty.");
        }

        if (!string.Equals(
                NativeTerminalPackageProvenance.ComputeSignatureRemovedSha256(path),
                contentSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The signed packaged {fileName} differs from its build receipt.");
        }
    }

    private static void ValidateLegalFile(string licenseDirectory, JsonElement expected)
    {
        var fileName = RequireString(expected, "file");
        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The desktop automation build receipt names a legal file outside the license directory.");
        }

        var info = new FileInfo(Path.Combine(licenseDirectory, fileName));
        if (!info.Exists
            || info.LinkTarget is not null
            || info.Length != RequireInt64(expected, "bytes"))
        {
            throw new InvalidDataException(
                $"The packaged {fileName} is missing, linked, or differs in length from its build receipt.");
        }

        using var stream = File.OpenRead(info.FullName);
        if (!string.Equals(
                Convert.ToHexStringLower(SHA256.HashData(stream)),
                RequireString(expected, "sha256"),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The packaged {fileName} differs from its build receipt.");
        }
    }

    private static void RequireSha256(string value, string fileName)
    {
        if (value.Length != 64
            || value.Any(static character =>
                character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new InvalidDataException(
                $"The desktop automation build receipt has an invalid SHA-256 for {fileName}.");
        }
    }

    private static void RequireExactCopy(
        byte[] expected,
        string copiedPath,
        string label)
    {
        var copied = ReadDocument(copiedPath, label);
        if (!expected.AsSpan().SequenceEqual(copied))
        {
            throw new InvalidDataException(
                $"The packaged desktop automation {label} differs from its reviewed source.");
        }
    }

    private static byte[] ReadDocument(string path, string label)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.LinkTarget is not null
            || info.Length < 2 || info.Length > MaximumDocumentBytes)
        {
            throw new InvalidDataException(
                $"The desktop automation {label} is missing, linked, empty, or exceeds its size bound.");
        }

        return File.ReadAllBytes(path);
    }

    private static JsonDocument Parse(byte[] content, string label)
    {
        try
        {
            return JsonDocument.Parse(
                content,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 12,
                });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"The desktop automation {label} is not valid JSON.",
                exception);
        }
    }

    private static void RequireFormat(
        JsonElement root,
        string expected,
        string label)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"The desktop automation {label} must be an object.");
        }

        RequireString(root, "format", expected);
    }

    private static JsonElement RequireObject(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"The desktop automation evidence requires object {name}.");
        }

        return value;
    }

    private static string RequireString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException(
                $"The desktop automation evidence requires string {name}.");
        }

        return value.GetString()!;
    }

    private static void RequireString(
        JsonElement parent,
        string name,
        string expected)
    {
        if (!string.Equals(
                RequireString(parent, name),
                expected,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The desktop automation evidence has an unexpected {name} value.");
        }
    }

    private static long RequireInt64(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt64(out var result)
            || result < 0)
        {
            throw new InvalidDataException(
                $"The desktop automation evidence requires non-negative integer {name}.");
        }

        return result;
    }

    private static void RequireBoolean(
        JsonElement parent,
        string name,
        bool expected)
    {
        if (!parent.TryGetProperty(name, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
            || value.GetBoolean() != expected)
        {
            throw new InvalidDataException(
                $"The desktop automation evidence has an unexpected {name} value.");
        }
    }
}
