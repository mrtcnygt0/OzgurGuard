using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace OzgurAuth.Service.Native;

/// <summary>
/// WTS tabanlı oturum işlemlerinin yönetilen sarmalayıcısı: oturum bilgisi okuma,
/// etkileşimli süreç başlatma ve oturumu kesme.
/// </summary>
public sealed class SessionInterop
{
    private readonly ILogger<SessionInterop> _logger;

    public SessionInterop(ILogger<SessionInterop> logger) => _logger = logger;

    /// <summary>Oturumun DOMAIN\kullanıcı adını döndürür (bulunamazsa boş).</summary>
    public string GetUserName(int sessionId)
    {
        string user = QueryString(sessionId, WtsNative.WtsInfoClass.WTSUserName);
        string domain = QueryString(sessionId, WtsNative.WtsInfoClass.WTSDomainName);

        if (string.IsNullOrEmpty(user))
        {
            return string.Empty;
        }

        return string.IsNullOrEmpty(domain) ? user : $"{domain}\\{user}";
    }

    /// <summary>RDP istemcisinin IP adresini döndürür (konsol/bilinmiyorsa boş).</summary>
    public string GetClientAddress(int sessionId)
    {
        if (!WtsNative.WTSQuerySessionInformation(
                (IntPtr)WtsNative.WTS_CURRENT_SERVER_HANDLE,
                sessionId,
                WtsNative.WtsInfoClass.WTSClientAddress,
                out IntPtr buffer,
                out _))
        {
            return string.Empty;
        }

        try
        {
            var address = Marshal.PtrToStructure<WtsNative.WTS_CLIENT_ADDRESS>(buffer);
            // AF_INET = 2
            if (address.AddressFamily == 2 && address.Address is { Length: >= 6 })
            {
                // Address[0] ayrılmıştır; IPv4 baytları 2..5 arasındadır.
                return new IPAddress(new[] { address.Address[2], address.Address[3], address.Address[4], address.Address[5] }).ToString();
            }

            return string.Empty;
        }
        finally
        {
            WtsNative.WTSFreeMemory(buffer);
        }
    }

    /// <summary>
    /// Oturum açmış kullanıcının SID'ini döndürür (pipe ACL için). SYSTEM gerektirir;
    /// alınamazsa null.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public SecurityIdentifier? GetUserSid(int sessionId)
    {
        IntPtr userToken = IntPtr.Zero;
        try
        {
            if (!WtsNative.WTSQueryUserToken(sessionId, out userToken))
            {
                return null;
            }

            using var identity = new WindowsIdentity(userToken);
            return identity.User;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Oturum {SessionId} için kullanıcı SID alınamadı.", sessionId);
            return null;
        }
        finally
        {
            if (userToken != IntPtr.Zero) WtsNative.CloseHandle(userToken);
        }
    }

    /// <summary>
    /// Oturumu standart WTSDisconnectSession ile keser. Yalnızca masaüstü oturumunu
    /// koparır; ağ servislerini, IIS'i veya portları etkilemez.
    /// </summary>
    public bool DisconnectSession(int sessionId)
    {
        bool ok = WtsNative.WTSDisconnectSession((IntPtr)WtsNative.WTS_CURRENT_SERVER_HANDLE, sessionId, false);
        if (!ok)
        {
            _logger.LogError("WTSDisconnectSession oturum {SessionId} için başarısız. Win32 hata: {Error}",
                sessionId, Marshal.GetLastWin32Error());
        }

        return ok;
    }

    /// <summary>
    /// Verilen oturumda, oturum açmış kullanıcının belirteciyle etkileşimli bir süreç başlatır.
    /// Başlatılan sürecin PID'ini döndürür; başarısızsa null.
    /// </summary>
    public int? LaunchInSession(int sessionId, string applicationPath, string arguments)
    {
        IntPtr userToken = IntPtr.Zero;
        IntPtr primaryToken = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;

        try
        {
            if (!WtsNative.WTSQueryUserToken(sessionId, out userToken))
            {
                _logger.LogWarning("WTSQueryUserToken oturum {SessionId} için başarısız (henüz hazır değil). Win32: {Error}",
                    sessionId, Marshal.GetLastWin32Error());
                return null;
            }

            uint desiredAccess = WtsNative.TOKEN_ASSIGN_PRIMARY | WtsNative.TOKEN_DUPLICATE |
                                 WtsNative.TOKEN_QUERY | WtsNative.TOKEN_ADJUST_DEFAULT |
                                 WtsNative.TOKEN_ADJUST_SESSIONID;

            if (!WtsNative.DuplicateTokenEx(
                    userToken,
                    desiredAccess,
                    IntPtr.Zero,
                    WtsNative.SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                    WtsNative.TOKEN_TYPE.TokenPrimary,
                    out primaryToken))
            {
                _logger.LogError("DuplicateTokenEx başarısız. Win32: {Error}", Marshal.GetLastWin32Error());
                return null;
            }

            if (!WtsNative.CreateEnvironmentBlock(out environment, primaryToken, false))
            {
                _logger.LogWarning("CreateEnvironmentBlock başarısız; ortam bloğu olmadan devam ediliyor. Win32: {Error}",
                    Marshal.GetLastWin32Error());
                environment = IntPtr.Zero;
            }

            var startupInfo = new WtsNative.STARTUPINFO
            {
                cb = Marshal.SizeOf<WtsNative.STARTUPINFO>(),
                lpDesktop = @"winsta0\default",
                wShowWindow = WtsNative.SW_SHOW,
                dwFlags = 0x00000001 // STARTF_USESHOWWINDOW
            };

            uint creationFlags = WtsNative.CREATE_UNICODE_ENVIRONMENT |
                                 WtsNative.NORMAL_PRIORITY_CLASS;

            string commandLine = $"\"{applicationPath}\" {arguments}";
            string workingDirectory = Path.GetDirectoryName(applicationPath) ?? Environment.SystemDirectory;

            if (!WtsNative.CreateProcessAsUser(
                    primaryToken,
                    applicationPath,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    creationFlags,
                    environment,
                    workingDirectory,
                    ref startupInfo,
                    out WtsNative.PROCESS_INFORMATION processInfo))
            {
                _logger.LogError("CreateProcessAsUser başarısız. Win32: {Error}", Marshal.GetLastWin32Error());
                return null;
            }

            WtsNative.CloseHandle(processInfo.hThread);
            WtsNative.CloseHandle(processInfo.hProcess);
            return processInfo.dwProcessId;
        }
        finally
        {
            if (environment != IntPtr.Zero) WtsNative.DestroyEnvironmentBlock(environment);
            if (primaryToken != IntPtr.Zero) WtsNative.CloseHandle(primaryToken);
            if (userToken != IntPtr.Zero) WtsNative.CloseHandle(userToken);
        }
    }

    private static string QueryString(int sessionId, WtsNative.WtsInfoClass infoClass)
    {
        if (!WtsNative.WTSQuerySessionInformation(
                (IntPtr)WtsNative.WTS_CURRENT_SERVER_HANDLE,
                sessionId,
                infoClass,
                out IntPtr buffer,
                out int bytes))
        {
            return string.Empty;
        }

        try
        {
            return bytes > 0 ? Marshal.PtrToStringUni(buffer) ?? string.Empty : string.Empty;
        }
        finally
        {
            WtsNative.WTSFreeMemory(buffer);
        }
    }
}
