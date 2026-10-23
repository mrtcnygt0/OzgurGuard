using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace OzgurGuard.Service.Native;

public static class NativeMethods
{
    public static readonly IntPtr WTS_CURRENT_SERVER_HANDLE = IntPtr.Zero;

    #region Win32 Constants

    public const uint MAXIMUM_ALLOWED = 0x02000000;
    public const int TOKEN_DUPLICATE = 0x0002;
    public const int TOKEN_QUERY = 0x0008;
    public const int TOKEN_ASSIGN_PRIMARY = 0x0001;

    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const uint CREATE_NEW_CONSOLE = 0x00000010;
    public const uint NORMAL_PRIORITY_CLASS = 0x00000020;
    public const int STARTF_USESHOWWINDOW = 0x00000001;
    public const short SW_SHOW = 5;

    public enum SECURITY_IMPERSONATION_LEVEL
    {
        SecurityAnonymous,
        SecurityIdentification,
        SecurityImpersonation,
        SecurityDelegation
    }

    public enum TOKEN_TYPE
    {
        TokenPrimary = 1,
        TokenImpersonation
    }

    public enum WTS_INFO_CLASS
    {
        WTSInitialProgram,
        WTSApplicationName,
        WTSWorkingDirectory,
        WTSOEMId,
        WTSSessionId,
        WTSUserName,
        WTSWinStationName,
        WTSDomainName,
        WTSConnectState,
        WTSClientBuildNumber,
        WTSClientName,
        WTSClientDirectory,
        WTSClientProductId,
        WTSClientHardwareId,
        WTSClientAddress,
        WTSClientDisplay,
        WTSClientProtocolType
    }

    public enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct WTS_SESSION_INFO
    {
        public int SessionId;
        public string pWinStationName;
        public WTS_CONNECTSTATE_CLASS State;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WTS_CLIENT_ADDRESS
    {
        public int AddressFamily;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] Address;
    }

    #endregion

    #region P/Invoke Signatures

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSQueryUserToken(int sessionId, out IntPtr phToken);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSEnumerateSessions(
        IntPtr hServer,
        int Reserved,
        int Version,
        out IntPtr ppSessionInfo,
        out int pCount);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSDisconnectSession(IntPtr hServer, int sessionId, bool bWait);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSQuerySessionInformation(
        IntPtr hServer,
        int sessionId,
        WTS_INFO_CLASS wtsInfoClass,
        out IntPtr ppBuffer,
        out int pBytesReturned);

    [DllImport("wtsapi32.dll")]
    public static extern void WTSFreeMemory(IntPtr pMemory);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        SECURITY_IMPERSONATION_LEVEL impersonationLevel,
        TOKEN_TYPE tokenType,
        out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string? lpApplicationName,
        string? lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("userenv.dll", SetLastError = true)]
    public static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    public static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    #endregion

    #region High-Level Helpers

    /// <summary>
    /// Aktif olan tüm WTS / RDP oturum ID'lerini listeler.
    /// </summary>
    public static List<int> GetActiveSessionIds()
    {
        var list = new List<int>();
        if (WTSEnumerateSessions(WTS_CURRENT_SERVER_HANDLE, 0, 1, out IntPtr pSessionInfo, out int count))
        {
            try
            {
                int structSize = Marshal.SizeOf(typeof(WTS_SESSION_INFO));
                long current = pSessionInfo.ToInt64();

                for (int i = 0; i < count; i++)
                {
                    var si = (WTS_SESSION_INFO)Marshal.PtrToStructure(new IntPtr(current), typeof(WTS_SESSION_INFO))!;
                    current += structSize;

                    // Session 0 SYSTEM servisidir; 0'dan büyük ve Active oturumları al
                    if (si.SessionId > 0 && si.State == WTS_CONNECTSTATE_CLASS.WTSActive)
                    {
                        list.Add(si.SessionId);
                    }
                }
            }
            catch { }
            finally
            {
                WTSFreeMemory(pSessionInfo);
            }
        }
        return list;
    }

    /// <summary>
    /// Belirtilen oturum (Session ID) içerisinde SYSTEM yetkisinden kullanıcı belirtecine
    /// geçerek etkileşimli bir Win32/WPF sürecini başlatır.
    /// </summary>
    public static int? LaunchInSession(int sessionId, string applicationPath, string arguments, out int win32Error)
    {
        win32Error = 0;

        if (!WTSQueryUserToken(sessionId, out IntPtr userToken))
        {
            win32Error = Marshal.GetLastWin32Error();
            return null;
        }

        IntPtr primaryToken = IntPtr.Zero;
        IntPtr environment = IntPtr.Zero;

        try
        {
            if (!DuplicateTokenEx(
                userToken,
                MAXIMUM_ALLOWED,
                IntPtr.Zero,
                SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation,
                TOKEN_TYPE.TokenPrimary,
                out primaryToken))
            {
                win32Error = Marshal.GetLastWin32Error();
                return null;
            }

            var si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(si);
            si.lpDesktop = @"winsta0\default"; // Kullanıcının etkileşimli masaüstü
            si.dwFlags = STARTF_USESHOWWINDOW;
            si.wShowWindow = SW_SHOW;

            CreateEnvironmentBlock(out environment, primaryToken, false);

            string commandLine = $"\"{applicationPath}\" {arguments}";
            uint flags = CREATE_UNICODE_ENVIRONMENT | NORMAL_PRIORITY_CLASS;

            if (CreateProcessAsUser(
                primaryToken,
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                flags,
                environment,
                Path.GetDirectoryName(applicationPath),
                ref si,
                out PROCESS_INFORMATION pi))
            {
                CloseHandle(pi.hProcess);
                CloseHandle(pi.hThread);
                return pi.dwProcessId;
            }

            win32Error = Marshal.GetLastWin32Error();
            return null;
        }
        finally
        {
            if (environment != IntPtr.Zero) DestroyEnvironmentBlock(environment);
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            CloseHandle(userToken);
        }
    }

    /// <summary>RDP oturumunu anında koparır (Fail-Closed).</summary>
    public static bool DisconnectSession(int sessionId)
    {
        return WTSDisconnectSession(WTS_CURRENT_SERVER_HANDLE, sessionId, false);
    }

    /// <summary>Oturumdaki kullanıcı adını sorgular.</summary>
    public static string GetSessionUserName(int sessionId)
    {
        if (WTSQuerySessionInformation(WTS_CURRENT_SERVER_HANDLE, sessionId, WTS_INFO_CLASS.WTSUserName, out IntPtr buffer, out _))
        {
            try
            {
                return Marshal.PtrToStringUni(buffer) ?? string.Empty;
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }
        return string.Empty;
    }

    /// <summary>Oturumun istemci IP adresini sorgular.</summary>
    public static string GetClientAddress(int sessionId)
    {
        if (WTSQuerySessionInformation(WTS_CURRENT_SERVER_HANDLE, sessionId, WTS_INFO_CLASS.WTSClientAddress, out IntPtr buffer, out int bytes))
        {
            try
            {
                if (bytes < Marshal.SizeOf<WTS_CLIENT_ADDRESS>()) return "127.0.0.1";
                var clientAddr = Marshal.PtrToStructure<WTS_CLIENT_ADDRESS>(buffer);

                // AF_INET = 2 (IPv4)
                if (clientAddr.AddressFamily == 2)
                {
                    byte[] ipBytes = new byte[4];
                    Array.Copy(clientAddr.Address, 2, ipBytes, 0, 4);
                    return new IPAddress(ipBytes).ToString();
                }
            }
            catch { }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }
        return "127.0.0.1";
    }

    #endregion
}
