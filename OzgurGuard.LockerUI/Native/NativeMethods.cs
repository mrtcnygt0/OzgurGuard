using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace OzgurGuard.LockerUI.Native;

public static class NativeMethods
{
    #region Win32 Constants

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_SYSKEYDOWN = 0x0104;

    public const int VK_TAB = 0x09;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_SPACE = 0x20;
    public const int VK_F4 = 0x73;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const int VK_CONTROL = 0x11;
    public const int VK_SHIFT = 0x10;

    public const int LLKHF_ALTDOWN = 0x20;

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_SHOWWINDOW = 0x0040;

    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    #endregion

    #region Delegates & P/Invoke Signatures

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool ClipCursor(ref RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool ClipCursor(IntPtr lpRect);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool SetKernelObjectSecurity(
        IntPtr Handle,
        int SecurityInformation,
        [In] byte[] pSecurityDescriptor);

    #endregion

    #region Keyboard Hook Management

    private static IntPtr _hookId = IntPtr.Zero;
    private static LowLevelKeyboardProc? _proc;

    public static void InstallKeyboardHook()
    {
        if (_hookId != IntPtr.Zero) return;

        _proc = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule?.ModuleName), 0);
    }

    public static void UninstallKeyboardHook()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            _proc = null;
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            bool alt = (kbd.flags & LLKHF_ALTDOWN) != 0;
            bool ctrl = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool shift = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;

            // 1. Alt + Tab
            if (kbd.vkCode == VK_TAB && alt)
            {
                return (IntPtr)1;
            }

            // 2. Alt + F4
            if (kbd.vkCode == VK_F4 && alt)
            {
                return (IntPtr)1;
            }

            // 3. Alt + Space
            if (kbd.vkCode == VK_SPACE && alt)
            {
                return (IntPtr)1;
            }

            // 4. Windows Tuşları (Sol / Sağ)
            if (kbd.vkCode == VK_LWIN || kbd.vkCode == VK_RWIN)
            {
                return (IntPtr)1;
            }

            // 5. Ctrl + Esc veya Alt + Esc
            if (kbd.vkCode == VK_ESCAPE && (ctrl || alt))
            {
                return (IntPtr)1;
            }

            // 6. Ctrl + Shift + Esc (Görev Yöneticisi Kısayolu)
            if (kbd.vkCode == VK_ESCAPE && ctrl && shift)
            {
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    #endregion

    #region Task Manager Registry Policy

    /// <summary>
    /// Görev Yöneticisi'ni (TaskMgr) Registry üzerinden kilitler (1) veya açar (0).
    /// </summary>
    public static void SetTaskManagerDisabled(bool disabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System");
            if (key != null)
            {
                if (disabled)
                {
                    key.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
                    key.SetValue("DisableLockWorkstation", 1, RegistryValueKind.DWord);
                    key.SetValue("DisableChangePassword", 1, RegistryValueKind.DWord);
                }
                else
                {
                    key.DeleteValue("DisableTaskMgr", false);
                    key.DeleteValue("DisableLockWorkstation", false);
                    key.DeleteValue("DisableChangePassword", false);
                }
            }
        }
        catch
        {
            // İzin yetersizse yoksay
        }
    }

    #endregion

    #region Explorer Isolation

    /// <summary>
    /// Masaüstü görev çubuğunu (Shell_TrayWnd) gizler veya gösterir.
    /// </summary>
    public static void SetTaskbarVisible(bool visible)
    {
        try
        {
            IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
            if (taskbar != IntPtr.Zero)
            {
                ShowWindow(taskbar, visible ? SW_SHOW : SW_HIDE);
            }

            IntPtr secondaryTaskbar = FindWindow("Shell_SecondaryTrayWnd", null);
            if (secondaryTaskbar != IntPtr.Zero)
            {
                ShowWindow(secondaryTaskbar, visible ? SW_SHOW : SW_HIDE);
            }
        }
        catch { }
    }

    #endregion

    #region Process DACL Protection

    /// <summary>
    /// Locker sürecinin PROCESS_TERMINATE ve PROCESS_SUSPEND izinlerini sınırlandırarak
    /// yetkisiz sonlandırılmasını engeller.
    /// </summary>
    public static void ProtectCurrentProcess()
    {
        try
        {
            IntPtr hProcess = Process.GetCurrentProcess().Handle;
            var rawSecurityDescriptor = new RawSecurityDescriptor("D:P(A;;GA;;;SY)(A;;GRGWGX;;;BA)");
            byte[] binaryForm = new byte[rawSecurityDescriptor.BinaryLength];
            rawSecurityDescriptor.GetBinaryForm(binaryForm, 0);

            // DACL_SECURITY_INFORMATION = 4
            SetKernelObjectSecurity(hProcess, 4, binaryForm);
        }
        catch
        {
            // Koruma başarısız olursa sistem devam eder
        }
    }

    #endregion
}
