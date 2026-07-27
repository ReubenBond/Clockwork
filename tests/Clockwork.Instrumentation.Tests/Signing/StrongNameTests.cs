using Clockwork.Instrumentation.Imaging;
using Clockwork.Instrumentation.Rules;
using Clockwork.Instrumentation.Signing;
using Clockwork.Instrumentation.Tests.Infrastructure;
using Mono.Cecil;

namespace Clockwork.Instrumentation.Tests.Signing;

/// <summary>
/// Verifies strong-name detection, key loading, and re-signing: unsigned/signed/delay-signed
/// classification, public-key-token formatting, re-signing a rewritten assembly, clear failure when
/// signing is impossible, and public-key-token consistency across a rewritten dependency closure.
/// </summary>
public sealed class StrongNameTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "cwr-sign-tests", Guid.NewGuid().ToString("n"));

    public StrongNameTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void UnsignedAssemblyIsDetected()
    {
        string path = CompileLibrary("Unsigned", "namespace U { public static class C { public static int V() => 1; } }");
        StrongNameInfo info = StrongNameInspector.Inspect(path);

        Assert.Equal(StrongNameStatus.None, info.Status);
        Assert.False(info.HasPublicKey);
        Assert.Null(info.PublicKeyToken);
    }

    [Fact]
    public void SignedAssemblyIsDetectedWithToken()
    {
        string keyPath = WriteKey();
        string path = CompileLibrary(
            "Signed", "namespace S { public static class C { public static int V() => 1; } }", keyPath);

        StrongNameInfo info = StrongNameInspector.Inspect(path);

        Assert.Equal(StrongNameStatus.StrongNameSigned, info.Status);
        Assert.True(info.HasPublicKey);
        Assert.NotNull(info.PublicKeyToken);
        Assert.Equal(16, info.PublicKeyToken!.Length);
    }

    [Fact]
    public void FormatTokenIsLowerCaseHex()
    {
        Assert.Equal("00ff10", StrongNameInspector.FormatToken([0x00, 0xff, 0x10]));
        Assert.Null(StrongNameInspector.FormatToken([]));
        Assert.Null(StrongNameInspector.FormatToken(null));
    }

    [Fact]
    public void PrivateKeyBlobCanSignPublicOnlyCannot()
    {
        string privatePath = WriteKey();
        var privateKey = StrongNameKey.Load(privatePath);
        Assert.True(privateKey.CanSign);

        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        byte[] publicBlob = StrongNameKeys.ExportPublicKeyBlob(rsa);
        string publicPath = Path.Combine(_directory, "public.snk");
        File.WriteAllBytes(publicPath, publicBlob);

        var publicKey = StrongNameKey.Load(publicPath);
        Assert.False(publicKey.CanSign);
    }

    [Fact]
    public void LoadMissingKeyFails()
    {
        SigningException ex = Assert.Throws<SigningException>(
            () => StrongNameKey.Load(Path.Combine(_directory, "does-not-exist.snk")));
        Assert.Contains("was not found", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadUnrecognizedBlobFails()
    {
        Assert.Throws<SigningException>(() => StrongNameKey.FromBlob(new byte[16], "garbage"));
    }

    [Fact]
    public void ReSignPreservesTokenAndDifferentKeyChangesIdentity()
    {
        string keyPath = WriteKey();
        string path = CompileLibrary(
            "ReSignMe", "namespace R { public static class C { public static int V() => 1; } }", keyPath);
        string originalToken = StrongNameInspector.Inspect(path).PublicKeyToken!;
        Assert.Equal(StrongNameStatus.StrongNameSigned, StrongNameInspector.Inspect(path).Status);

        // Re-signing a rewritten copy with the original key keeps the public-key-token identity, so
        // references carrying that token still bind.
        string sameKeyOutput = RewriteNoOp(path, "same");
        StrongNameSigner.ReSign(sameKeyOutput, StrongNameKey.Load(keyPath));
        StrongNameInfo sameKey = StrongNameInspector.Inspect(sameKeyOutput);
        Assert.Equal(StrongNameStatus.StrongNameSigned, sameKey.Status);
        Assert.Equal(originalToken, sameKey.PublicKeyToken);

        // Re-signing with a *different* key changes the public-key token, proving the signer applies
        // the supplied key rather than merely preserving the input's recorded identity.
        string otherKeyPath = Path.Combine(_directory, "other.snk");
        File.WriteAllBytes(otherKeyPath, StrongNameKeys.CreatePrivateKeyBlob());
        string otherKeyOutput = RewriteNoOp(path, "other");
        StrongNameSigner.ReSign(otherKeyOutput, StrongNameKey.Load(otherKeyPath));
        Assert.NotEqual(originalToken, StrongNameInspector.Inspect(otherKeyOutput).PublicKeyToken);
    }

    [Fact]
    public void ReSignWithPublicOnlyKeyFails()
    {
        string keyPath = WriteKey();
        string path = CompileLibrary(
            "PubOnly", "namespace P { public static class C { public static int V() => 1; } }", keyPath);
        string rewritten = RewriteNoOp(path);

        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        string publicPath = Path.Combine(_directory, "pubonly.snk");
        File.WriteAllBytes(publicPath, StrongNameKeys.ExportPublicKeyBlob(rsa));

        SigningException ex = Assert.Throws<SigningException>(
            () => StrongNameSigner.ReSign(rewritten, StrongNameKey.Load(publicPath)));
        Assert.Contains("public-only", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicKeyTokenIsConsistentAcrossRewrittenClosure()
    {
        string keyPath = WriteKey();
        string depPath = CompileLibrary(
            "dep", "namespace Dep { public static class D { public static int V() => 1; } }", keyPath);
        string appPath = CompileLibrary(
            "app", "namespace App { public static class A { public static int Go() => Dep.D.V(); } }", keyPath,
            additionalReferences: [depPath]);

        string depToken = StrongNameInspector.Inspect(depPath).PublicKeyToken!;
        Assert.Equal(depToken, ReadReferenceToken(appPath, "dep"));

        string rewrittenDep = RewriteNoOp(depPath);
        string rewrittenApp = RewriteNoOp(appPath);
        var key = StrongNameKey.Load(keyPath);
        StrongNameSigner.ReSign(rewrittenDep, key);
        StrongNameSigner.ReSign(rewrittenApp, key);

        Assert.Equal(StrongNameStatus.StrongNameSigned, StrongNameInspector.Inspect(rewrittenDep).Status);
        Assert.Equal(StrongNameStatus.StrongNameSigned, StrongNameInspector.Inspect(rewrittenApp).Status);
        Assert.Equal(depToken, StrongNameInspector.Inspect(rewrittenDep).PublicKeyToken);
        Assert.Equal(depToken, ReadReferenceToken(rewrittenApp, "dep"));
    }

    [Fact]
    public void ImageInfoClassifiesOrdinaryManagedAssembly()
    {
        string path = CompileLibrary("Plain", "namespace X { public static class C { public static int V() => 1; } }");
        AssemblyImageInfo info = AssemblyImageInfo.Inspect(path);

        Assert.True(info.IsManagedAssembly);
        Assert.True(info.IsILOnly);
        Assert.False(info.IsMixedMode);
        Assert.False(info.IsReadyToRun);
        Assert.False(info.HasAuthenticodeSignature);
    }

    [Fact]
    public void ImageInfoDetectsReadyToRunInSharedFramework()
    {
        // The .NET shared framework ships ReadyToRun (crossgen'd) images; use one as a real R2R input
        // rather than publishing a fixture. This proves the detector fires on genuine native headers.
        string? r2r = FindReadyToRunAssembly();
        Assert.NotNull(r2r);

        AssemblyImageInfo info = AssemblyImageInfo.Inspect(r2r!);
        Assert.True(info.IsManagedAssembly);
        Assert.True(info.IsReadyToRun);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string? FindReadyToRunAssembly()
    {
        string trusted = (string)(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty);
        foreach (string path in trusted.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                if (AssemblyImageInfo.Inspect(path).IsReadyToRun)
                {
                    return path;
                }
            }
            catch (BadImageFormatException)
            {
            }
        }

        return null;
    }

    private static string ReadReferenceToken(string assemblyPath, string referenceName)
    {
        using AssemblyDefinition definition = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters
        {
            ReadSymbols = false,
            InMemory = true,
        });
        AssemblyNameReference reference = definition.MainModule.AssemblyReferences
            .Single(r => string.Equals(r.Name, referenceName, StringComparison.Ordinal));
        return StrongNameInspector.FormatToken(reference.PublicKeyToken)!;
    }

    private string WriteKey()
    {
        string keyPath = Path.Combine(_directory, "test.snk");
        File.WriteAllBytes(keyPath, StrongNameKeys.CreatePrivateKeyBlob());
        return keyPath;
    }

    private string CompileLibrary(
        string name, string source, string? keyPath = null, IEnumerable<string>? additionalReferences = null)
        => FixtureCompiler.Compile(
            name, source, _directory, FixtureSymbols.PortableFile, optimize: false,
            additionalReferencePaths: additionalReferences, strongNameKeyFile: keyPath);

    private string RewriteNoOp(string inputPath, string? suffix = null)
    {
        string baseName = Path.GetFileNameWithoutExtension(inputPath);
        string outputName = suffix is null ? baseName + ".rewritten.dll" : $"{baseName}.{suffix}.rewritten.dll";
        string outputPath = Path.Combine(_directory, outputName);
        var ruleSet = new RewriteRuleSet("clockwork.noop", "1.0", []);
        var request = new Instrumentation.Rewriting.RewriteRequest(
            inputPath,
            outputPath,
            ruleSet,
            new Instrumentation.Rewriting.RewriteOptions { ReferenceSearchDirectories = [_directory] });
        Instrumentation.Rewriting.RewriteResult result = Instrumentation.Rewriting.RewriteEngine.Rewrite(request);
        result.EnsureSuccess();
        return outputPath;
    }
}
