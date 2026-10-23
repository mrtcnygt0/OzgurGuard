using System.Security.Cryptography;

namespace OzgurGuard.Service.Security;

/// <summary>
/// PBKDF2 (SHA256, 100.000 iterasyon) ile parola doğrulama ve hash üretimi.
/// Düz parola bellekte yalnızca doğrulama anında tutulur, diske/günlüğe asla yazılmaz.
/// </summary>
public static class PasswordManager
{
    public const int Iterations = 100_000;
    public const int SaltSize = 16;
    public const int HashSize = 32;

    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>Rastgele salt üretir ve parolanın PBKDF2 hash'ini hesaplar.</summary>
    public static (string SaltBase64, string HashBase64) CreateHash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashSize);
        return (Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    /// <summary>Parolayı saklanan salt + hash karşısında sabit zamanlı (constant-time) doğrular.</summary>
    public static bool Verify(string password, string saltBase64, string hashBase64)
    {
        if (string.IsNullOrEmpty(saltBase64) || string.IsNullOrEmpty(hashBase64))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(saltBase64);
            expected = Convert.FromBase64String(hashBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
