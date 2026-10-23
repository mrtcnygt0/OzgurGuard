# ÖzgürGuard / ÖzgürAuth - RDP Oturum Doğrulama Sistemi

Özgür Zaman Kontrol Sistemleri Windows Server RDP (Uzak Masaüstü) ve konsol oturumları için ikinci faktör parola ve oturum kapısı doğrulama sistemi.

## Özellikler

- **Fail-Closed Güvenlik Mimarisi**: Doğrulama yapılmadan pencere kapatılırsa, süre dolarsa veya maksimum hatalı deneme hakkı biterse oturum sonlandırılır (`WTSDisconnectSession`).
- **PBKDF2 Kriptografik Doğrulama**: Parolalar düz metin olarak asla tutulmaz. 16 bayt salt ile 100.000 iterasyon PBKDF2-SHA256 türetilerek sabit zamanlı (constant-time) kontrol yapılır.
- **WPF Arayüz Simülasyonu**: Orijinal `OzgurAuth.VerifierUI` karanlık tema arayüzü, sarsıntı animasyonu, gerçek zamanlı geri sayım ve kalan deneme göstergeleri.
- **Hizmet Konsolu & Ayarlar**: `GateOptions` (TestMode, TimeoutSeconds, MaxAttempts) ve `PasswordTool` (set-password CLI) entegrasyonu.
- **Windows Olay Günlüğü (Event Viewer)**: `Application` günlüğü altında `OzgurAuth` olay kaynak kayıtları (Information, Warning, Error) ve filtreleme.

## Çalıştırma

```bash
npm install
npm run dev
```

Dev sunucusu port 3000 üzerinde başlatılır.
