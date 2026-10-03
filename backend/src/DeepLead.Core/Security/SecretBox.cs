using System.Security.Cryptography;
using System.Text;

namespace DeepLead.Core.Security;

/// <summary>
/// AES-256-GCM for data at rest (saved browser sessions of connected accounts).
/// Layout: [12-byte nonce][16-byte tag][ciphertext]. Key: 32 random bytes, base64, from config "Secrets:EncryptionKey".
/// </summary>
public sealed class SecretBox
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public SecretBox(string base64Key)
    {
        _key = Convert.FromBase64String(base64Key);
        if (_key.Length != 32)
            throw new ArgumentException("Secrets:EncryptionKey must be 32 bytes (base64).");
    }

    public byte[] Encrypt(string plaintext)
    {
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var output = new byte[NonceSize + TagSize + plain.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plain, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize));
        return output;
    }

    public string Decrypt(byte[] payload)
    {
        var plain = new byte[payload.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize + TagSize), payload.AsSpan(NonceSize, TagSize), plain);
        return Encoding.UTF8.GetString(plain);
    }
}
