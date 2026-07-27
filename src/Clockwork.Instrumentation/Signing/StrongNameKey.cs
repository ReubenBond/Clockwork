namespace Clockwork.Instrumentation.Signing;

/// <summary>
/// A strong-name key loaded from an <c>.snk</c> file. The file is expected to hold a Microsoft
/// CryptoAPI key blob: a full private-key blob (as produced by <c>sn -k</c>), which can sign, or a
/// public-key-only blob (as produced by <c>sn -p</c>), which cannot. The raw blob bytes are handed
/// to Mono.Cecil's <c>WriterParameters.StrongNameKeyBlob</c> to (re-)sign a rewritten assembly.
/// </summary>
public sealed class StrongNameKey
{
    private const byte PrivateKeyBlobType = 0x07;
    private const byte PublicKeyBlobType = 0x06;

    private StrongNameKey(byte[] blob, bool canSign)
    {
        Blob = blob;
        CanSign = canSign;
    }

    /// <summary>Gets the raw CryptoAPI key blob bytes.</summary>
    public byte[] Blob { get; }

    /// <summary>
    /// Gets a value indicating whether this key contains private-key material and can therefore sign
    /// an assembly. A public-only key cannot.
    /// </summary>
    public bool CanSign { get; }

    /// <summary>Loads a strong-name key from the file at <paramref name="path"/>.</summary>
    /// <param name="path">The path of the <c>.snk</c> file.</param>
    /// <returns>The loaded key.</returns>
    /// <exception cref="SigningException">The file is missing, empty, or not a recognized key blob.</exception>
    public static StrongNameKey Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
        {
            throw new SigningException($"Strong-name key file was not found: '{path}'.");
        }

        byte[] blob;
        try
        {
            blob = File.ReadAllBytes(path);
        }
        catch (IOException ex)
        {
            throw new SigningException($"Failed to read strong-name key file '{path}': {ex.Message}", ex);
        }

        return FromBlob(blob, path);
    }

    /// <summary>Creates a strong-name key from raw CryptoAPI key blob bytes.</summary>
    /// <param name="blob">The key blob bytes.</param>
    /// <param name="sourceName">A descriptive name of the blob's origin, used in error messages.</param>
    /// <returns>The key.</returns>
    /// <exception cref="SigningException">The blob is empty or not a recognized key blob.</exception>
    public static StrongNameKey FromBlob(byte[] blob, string sourceName = "<blob>")
    {
        ArgumentNullException.ThrowIfNull(blob);
        if (blob.Length < 12)
        {
            throw new SigningException(
                $"Strong-name key '{sourceName}' is not a valid CryptoAPI key blob (too short).");
        }

        // PUBLICKEYSTRUC.bType is the first byte: 0x07 = PRIVATEKEYBLOB, 0x06 = PUBLICKEYBLOB.
        byte blobType = blob[0];
        return blobType switch
        {
            PrivateKeyBlobType => new StrongNameKey(blob, canSign: true),
            PublicKeyBlobType => new StrongNameKey(blob, canSign: false),
            _ => throw new SigningException(
                $"Strong-name key '{sourceName}' is not a recognized CryptoAPI key blob " +
                $"(unexpected blob type 0x{blobType:x2})."),
        };
    }
}
