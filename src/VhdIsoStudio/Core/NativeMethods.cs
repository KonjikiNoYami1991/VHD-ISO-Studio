using System.Runtime.InteropServices;

namespace VhdIsoStudio.Core;

internal static class NativeMethods
{
    internal const int WM_DROPFILES = 0x0233;
    internal const uint WM_COPYDATA = 0x004A;
    internal const uint WM_COPYGLOBALDATA = 0x0049;
    internal const uint MSGFLT_ALLOW = 1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X; public int Y; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr pChangeFilterStruct);

    [DllImport("ole32.dll")]
    internal static extern int RevokeDragDrop(IntPtr hwnd);

    [DllImport("shell32.dll")]
    internal static extern void DragAcceptFiles(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fAccept);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern uint DragQueryFile(IntPtr hDrop, uint iFile, System.Text.StringBuilder? lpszFile, uint cch);

    [DllImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DragQueryPoint(IntPtr hDrop, out POINT lppt);

    [DllImport("shell32.dll")]
    internal static extern void DragFinish(IntPtr hDrop);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("shell32.dll", SetLastError = true)]
    internal static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    [StructLayout(LayoutKind.Sequential)]
    internal struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("kernel32.dll")]
    internal static extern uint SetThreadExecutionState(uint esFlags);

    private const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;

    /// <summary>
    /// Impedisce (true) o consente di nuovo (false) la sospensione automatica del PC durante un lavoro lungo.
    /// Lo schermo puo' comunque spegnersi. Va chiamato sempre dallo stesso thread (UI): lo stato e' per-thread.
    /// </summary>
    internal static void KeepAwake(bool on)
    {
        try { SetThreadExecutionState(on ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS); }
        catch { }
    }

    /// <summary>Lampeggia in taskbar finche' la finestra non torna in primo piano.</summary>
    internal static void Flash(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        var fi = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = hwnd,
            dwFlags = 0x00000003 | 0x0000000C, // FLASHW_ALL | FLASHW_TIMERNOFG
            uCount = 5,
            dwTimeout = 0
        };
        FlashWindowEx(ref fi);
    }
}
