using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Asura.Packaging;

namespace Asura.AccessibilityAcceptance;

public sealed class AgentDesktopPackageProvenanceTests : IDisposable
{
    private const string Commit = "2b0a7334fc111730454b726e6800aa41cdafc093";
    private const string RustcCommit = "29483883eed69d5fb4db01964cdf2af4d86e9cb2";
    private const string Library = "libagent_desktop_ffi.dylib";
    private const string Helper = "agent-desktop-macos-helper";

    private readonly string _temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        $"asura-agent-desktop-tests-{Guid.NewGuid():N}");

    public AgentDesktopPackageProvenanceTests() =>
        Directory.CreateDirectory(_temporaryDirectory);

    [Fact]
    public void Release_signing_preserves_the_receipt_bound_library_and_helper()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var fixture = CreateFixture();
        var unsigned = Sha256(Path.Combine(fixture.ExecutableDirectory, Library));
        Sign(Path.Combine(fixture.ExecutableDirectory, Library));
        Sign(Path.Combine(fixture.ExecutableDirectory, Helper));

        Assert.NotEqual(
            unsigned,
            Sha256(Path.Combine(fixture.ExecutableDirectory, Library)),
            StringComparer.Ordinal);
        Validate(fixture);
    }

    [Fact]
    public void The_staged_release_build_passes_the_post_signing_check_once_signed()
    {
        var repositoryRoot = FindRepositoryRoot();
        var staged = Path.Combine(repositoryRoot, "native", "artifacts", "osx-arm64", "agent-desktop");
        if (!OperatingSystem.IsMacOS() || !Directory.Exists(staged))
        {
            return;
        }

        // What scripts/build-agent-desktop.sh wrote, laid out as
        // scripts/package-macos.sh bundles it, against the reviewed catalog.
        var executableDirectory = Path.Combine(_temporaryDirectory, "MacOS");
        var licenseDirectory = Path.Combine(_temporaryDirectory, "Licenses");
        var nativeLicenseDirectory = Path.Combine(licenseDirectory, "Native");
        Directory.CreateDirectory(executableDirectory);
        Directory.CreateDirectory(nativeLicenseDirectory);
        foreach (var code in new[] { Library, Helper })
        {
            File.Copy(Path.Combine(staged, code), Path.Combine(executableDirectory, code));
            Sign(Path.Combine(executableDirectory, code));
        }

        foreach (var legal in new[]
                 {
                     "AGENT-DESKTOP-LICENSE",
                     "AGENT-DESKTOP-THIRD-PARTY-NOTICES.md",
                     "RUST-STANDARD-LIBRARY-COPYRIGHT.html",
                 })
        {
            File.Copy(Path.Combine(staged, legal), Path.Combine(licenseDirectory, legal));
        }

        var catalogPath = Path.Combine(repositoryRoot, "licenses", "native-desktop-automation-components.json");
        var receiptPath = Path.Combine(staged, "agent-desktop-build-receipt.json");
        File.Copy(catalogPath, Path.Combine(nativeLicenseDirectory, "native-desktop-automation-components.json"));
        File.Copy(receiptPath, Path.Combine(nativeLicenseDirectory, "agent-desktop-build-receipt.json"));

        AgentDesktopPackageProvenance.ValidateAfterCodeSigning(
            executableDirectory,
            licenseDirectory,
            catalogPath,
            receiptPath);
    }

    [Theory]
    [InlineData(Library)]
    [InlineData(Helper)]
    public void Different_program_bytes_are_rejected_after_signing(string fileName)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var fixture = CreateFixture();
        var path = Path.Combine(fixture.ExecutableDirectory, fileName);
        File.Copy("/bin/echo", path, overwrite: true);
        Sign(path);

        var exception = Assert.Throws<InvalidDataException>(() => Validate(fixture));

        Assert.Contains(fileName, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("library")]
    [InlineData("helper")]
    public void A_receipt_without_the_signature_independent_digest_is_rejected(string artifact)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var fixture = CreateFixture();
        RewriteReceipt(
            fixture,
            receipt => receipt[artifact]!.AsObject().Remove("signatureRemovedSha256"));

        var exception = Assert.Throws<InvalidDataException>(() => Validate(fixture));

        Assert.Contains("signatureRemovedSha256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_evidence_flavor_is_never_accepted_as_the_release_build()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var fixture = CreateFixture();
        RewriteReceipt(fixture, receipt => receipt["flavor"] = "panic");

        Assert.Throws<InvalidDataException>(() => Validate(fixture));
    }

    [Fact]
    public void A_build_that_exports_panic_injection_is_rejected()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var fixture = CreateFixture();
        RewriteReceipt(fixture, receipt => receipt["abi"]!["panicInjectionExport"] = true);

        Assert.Throws<InvalidDataException>(() => Validate(fixture));
    }

    [Fact]
    public void A_build_that_aborts_on_panic_is_rejected()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var fixture = CreateFixture();
        RewriteReceipt(fixture, receipt => receipt["toolchain"]!["panicStrategy"] = "abort");

        Assert.Throws<InvalidDataException>(() => Validate(fixture));
    }

    [Fact]
    public void A_receipt_for_another_source_commit_is_rejected()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var fixture = CreateFixture();
        RewriteReceipt(fixture, receipt => receipt["source"]!["commit"] = new string('0', 40));

        Assert.Throws<InvalidDataException>(() => Validate(fixture));
    }

    [Fact]
    public void A_receipt_bound_to_another_catalog_is_rejected()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var fixture = CreateFixture();
        var catalog = JsonNode.Parse(File.ReadAllText(fixture.CatalogPath))!.AsObject();
        catalog["catalogId"] = "another-review";
        File.WriteAllText(fixture.CatalogPath, catalog.ToJsonString());
        File.Copy(
            fixture.CatalogPath,
            Path.Combine(fixture.LicenseDirectory, "Native", "native-desktop-automation-components.json"),
            overwrite: true);

        Assert.Throws<InvalidDataException>(() => Validate(fixture));
    }

    [Fact]
    public void A_packaged_receipt_that_differs_from_the_validated_one_is_rejected()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var fixture = CreateFixture();
        File.AppendAllText(
            Path.Combine(fixture.LicenseDirectory, "Native", "agent-desktop-build-receipt.json"),
            " ");

        Assert.Throws<InvalidDataException>(() => Validate(fixture));
    }

    [Fact]
    public void Changed_third_party_notices_are_rejected()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var fixture = CreateFixture();
        File.AppendAllText(
            Path.Combine(fixture.LicenseDirectory, "AGENT-DESKTOP-THIRD-PARTY-NOTICES.md"),
            "changed");

        Assert.Throws<InvalidDataException>(() => Validate(fixture));
    }

    public void Dispose()
    {
        if (Directory.Exists(_temporaryDirectory))
        {
            Directory.Delete(_temporaryDirectory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static void Validate(Fixture fixture) =>
        AgentDesktopPackageProvenance.ValidateAfterCodeSigning(
            fixture.ExecutableDirectory,
            fixture.LicenseDirectory,
            fixture.CatalogPath,
            fixture.ReceiptPath);

    private static void RewriteReceipt(Fixture fixture, Action<JsonObject> change)
    {
        var receipt = JsonNode.Parse(File.ReadAllText(fixture.ReceiptPath))!.AsObject();
        change(receipt);
        File.WriteAllText(fixture.ReceiptPath, receipt.ToJsonString());
        File.Copy(
            fixture.ReceiptPath,
            Path.Combine(fixture.LicenseDirectory, "Native", "agent-desktop-build-receipt.json"),
            overwrite: true);
    }

    private Fixture CreateFixture()
    {
        var executableDirectory = Path.Combine(_temporaryDirectory, "MacOS");
        var licenseDirectory = Path.Combine(_temporaryDirectory, "Licenses");
        var nativeLicenseDirectory = Path.Combine(licenseDirectory, "Native");
        Directory.CreateDirectory(executableDirectory);
        Directory.CreateDirectory(nativeLicenseDirectory);

        // Two different system Mach-O files stand in for the library and the
        // helper: only real Mach-O content survives codesign.
        var libraryPath = Path.Combine(executableDirectory, Library);
        var helperPath = Path.Combine(executableDirectory, Helper);
        File.Copy("/usr/bin/true", libraryPath);
        File.Copy("/usr/bin/false", helperPath);
        var licensePath = Path.Combine(licenseDirectory, "AGENT-DESKTOP-LICENSE");
        var noticesPath = Path.Combine(licenseDirectory, "AGENT-DESKTOP-THIRD-PARTY-NOTICES.md");
        var standardLibraryPath = Path.Combine(licenseDirectory, "RUST-STANDARD-LIBRARY-COPYRIGHT.html");
        File.WriteAllText(licensePath, "Apache license fixture");
        File.WriteAllText(noticesPath, "crate notices fixture");
        File.WriteAllText(standardLibraryPath, "standard library notices fixture");

        var catalogPath = Path.Combine(_temporaryDirectory, "catalog.json");
        File.WriteAllBytes(catalogPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            format = "asura-native-desktop-automation-component-catalog-v1",
            catalogId = "fixture",
            component = new { sourceCommit = Commit, abiMajor = 7 },
            helper = new { macosExecutable = Helper },
            toolchain = new
            {
                rustcCommit = RustcCommit,
                cargoProfile = "release-ffi",
                panicStrategy = "unwind",
            },
        }));

        var receiptPath = Path.Combine(_temporaryDirectory, "receipt.json");
        File.WriteAllBytes(receiptPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            format = "asura-agent-desktop-build-receipt-v1",
            catalogSha256 = Sha256(catalogPath),
            targetRid = "osx-arm64",
            flavor = "release",
            source = new { commit = Commit },
            toolchain = new
            {
                rustcCommit = RustcCommit,
                profile = "release-ffi",
                panicStrategy = "unwind",
            },
            abiMajor = 7,
            abi = new { panicInjectionExport = false },
            library = Code(libraryPath),
            helper = Code(helperPath),
            license = Legal(licensePath),
            thirdPartyNotices = Legal(noticesPath),
            standardLibraryNotices = Legal(standardLibraryPath),
        }));

        File.Copy(catalogPath, Path.Combine(nativeLicenseDirectory, "native-desktop-automation-components.json"));
        File.Copy(receiptPath, Path.Combine(nativeLicenseDirectory, "agent-desktop-build-receipt.json"));
        return new Fixture(executableDirectory, licenseDirectory, catalogPath, receiptPath);
    }

    private static object Code(string path) => new
    {
        file = Path.GetFileName(path),
        bytes = new FileInfo(path).Length,
        sha256 = Sha256(path),
        signatureRemovedSha256 = NativeTerminalPackageProvenance.ComputeSignatureRemovedSha256(path),
    };

    private static object Legal(string path) => new
    {
        file = Path.GetFileName(path),
        bytes = new FileInfo(path).Length,
        sha256 = Sha256(path),
    };

    private static void Sign(string path)
    {
        var start = new ProcessStartInfo("/usr/bin/codesign")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            ArgumentList =
            {
                "--force", "--sign", "-", "--identifier", "sh.asura.fixture",
                "--options", "runtime", "--timestamp=none", path,
            },
        };
        using var process = Process.Start(start)!;
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
    }

    private static string Sha256(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

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

        throw new InvalidOperationException(
            "The Asura repository root was not found.");
    }

    private sealed record Fixture(
        string ExecutableDirectory,
        string LicenseDirectory,
        string CatalogPath,
        string ReceiptPath);
}
