namespace OzgurAuth.Service;

/// <summary>
/// "Gate" bölümünden bağlanan yapılandırma. Parola yalnızca PBKDF2 salt + hash
/// olarak tutulur; düz parola hiçbir zaman burada bulunmaz.
/// </summary>
public sealed class GateOptions
{
    public const string SectionName = "Gate";

    /// <summary>
    /// true iken oturum kesilmez; yalnızca Event Log'a ve arayüze simülasyon
    /// mesajı yazılır. Sunucuda gerçek kesme için false yapılmalıdır.
    /// </summary>
    public bool TestMode { get; set; } = true;

    /// <summary>Doğrulama için tanınan süre (saniye).</summary>
    public int TimeoutSeconds { get; set; } = 45;

    /// <summary>İzin verilen ardışık hatalı parola denemesi.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>PBKDF2 salt (Base64).</summary>
    public string PasswordSalt { get; set; } = string.Empty;

    /// <summary>PBKDF2 türetilmiş anahtar (Base64).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// OzgurAuth.VerifierUI.exe tam yolu. Boşsa servis exe'sine göre
    /// ..\Verifier\OzgurAuth.VerifierUI.exe tahmin edilir.
    /// </summary>
    public string VerifierPath { get; set; } = string.Empty;
}
