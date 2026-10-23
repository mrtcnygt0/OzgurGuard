namespace OzgurGuard.Service;

public sealed class GuardOptions
{
    public const string SectionName = "OzgurGuard";

    /// <summary>
    /// Geliştirme aşamasında sistemden kilitlenmeyi önlemek için 'Test Çıkışı' butonunu açar.
    /// Canlı sunucuya (Production) alınırken 'false' yapılmalıdır.
    /// </summary>
    public bool DevelopmentMode { get; set; } = true;

    /// <summary>RDP bağlantısı kurulduktan sonra tanınan süre (saniye). Varsayılan 45 sn.</summary>
    public int TimeoutSeconds { get; set; } = 45;

    /// <summary>İzin verilen ardışık hatalı parola denemesi. Varsayılan 3.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Watchdog denetim aralığı (milisaniye). Varsayılan 300ms.</summary>
    public int HeartbeatIntervalMs { get; set; } = 300;

    /// <summary>PBKDF2 Salt (Base64).</summary>
    public string PasswordSalt { get; set; } = "XeunT+yH3aYYp2Tx5O7L6g==";

    /// <summary>PBKDF2 SHA-256 100.000 iterasyon Hash (Base64). Varsayılan şifre: OzurGuvenlik2026!</summary>
    public string PasswordHash { get; set; } = "FmeP957MIzvl59PaEJJZ9tVawxj6rdys0eiw0j4bvWA=";

    /// <summary>OzgurGuard.LockerUI.exe tam yolu.</summary>
    public string LockerPath { get; set; } = @"C:\Program Files\OzgurGuard\Locker\OzgurGuard.LockerUI.exe";

    /// <summary>Güvenlik log dizini.</summary>
    public string LogDirectory { get; set; } = @"C:\ProgramData\OzgurGuard";
}
