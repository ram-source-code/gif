// GIF WinLive - Win32 helpers and desktop-layer discovery.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;

namespace GifWp
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public int dwFlags; }
    [StructLayout(LayoutKind.Sequential)] public struct POWERSTATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int LifeTime, FullLifeTime; }
    [StructLayout(LayoutKind.Sequential)] public struct THROTTLE { public uint Version, ControlMask, StateMask; }
    [StructLayout(LayoutKind.Sequential)]
    public struct CHOOSECOLOR
    {
        public int lStructSize; public IntPtr hwndOwner; public IntPtr hInstance; public int rgbResult;
        public IntPtr lpCustColors; public int Flags; public IntPtr lCustData; public IntPtr lpfnHook; public IntPtr lpTemplateName;
    }

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data);

    public static class Native
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string win);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string win);
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int index);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int index, int value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
        [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
        [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out POWERSTATUS s);
        [DllImport("kernel32.dll")] public static extern bool SetProcessInformation(IntPtr p, int cls, ref THROTTLE info, int size);
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr p, IntPtr min, IntPtr max);
        [DllImport("comdlg32.dll")] public static extern bool ChooseColor(ref CHOOSECOLOR cc);

        public static void SetLowPower()
        {
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
            try   // EcoQoS: efficiency cores / lower clocks where Windows supports it
            {
                THROTTLE t = new THROTTLE(); t.Version = 1; t.ControlMask = 1; t.StateMask = 1;
                SetProcessInformation(GetCurrentProcess(), 4, ref t, Marshal.SizeOf(typeof(THROTTLE)));
            }
            catch { }
        }

        // Returns working-set pages to Windows after big allocations are freed.
        public static void Trim()
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            try { SetProcessWorkingSetSize(GetCurrentProcess(), new IntPtr(-1), new IntPtr(-1)); } catch { }
        }

        static readonly StringBuilder classBuf = new StringBuilder(64);

        public static string ClassOf(IntPtr h)
        {
            classBuf.Length = 0;
            GetClassName(h, classBuf, 64);
            return classBuf.ToString();
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

        // Dark title bar to match a dark panel (Windows 10 21H2+ / 11 support attribute 20).
        public static void DarkTitleBar(IntPtr h, bool dark)
        {
            int v = dark ? 1 : 0;
            try { DwmSetWindowAttribute(h, 20, ref v, 4); } catch { }
        }

        static IntPtr customColors = IntPtr.Zero;

        // Native colour picker: avoids loading Windows Forms just for one dialog.
        public static bool PickColor(IntPtr owner, ref int r, ref int g, ref int b)
        {
            if (customColors == IntPtr.Zero)
            {
                customColors = Marshal.AllocHGlobal(16 * 4);
                for (int i = 0; i < 16; i++) Marshal.WriteInt32(customColors, i * 4, 0xFFFFFF);
            }
            CHOOSECOLOR cc = new CHOOSECOLOR();
            cc.lStructSize = Marshal.SizeOf(typeof(CHOOSECOLOR));
            cc.hwndOwner = owner;
            cc.rgbResult = r | (g << 8) | (b << 16);
            cc.lpCustColors = customColors;
            cc.Flags = 0x1 | 0x2;   // CC_RGBINIT | CC_FULLOPEN
            if (!ChooseColor(ref cc)) return false;
            r = cc.rgbResult & 0xFF; g = (cc.rgbResult >> 8) & 0xFF; b = (cc.rgbResult >> 16) & 0xFF;
            return true;
        }
    }

    public static class Desktop
    {
        // Physical-pixel rectangles to cover: primary screen, every screen, or the whole span.
        public static List<Int32Rect> Targets(string mode)
        {
            List<Int32Rect> list = new List<Int32Rect>();
            if (mode == "Span")
            {
                list.Add(new Int32Rect(Native.GetSystemMetrics(76), Native.GetSystemMetrics(77),
                                       Native.GetSystemMetrics(78), Native.GetSystemMetrics(79)));
            }
            else
            {
                List<KeyValuePair<Int32Rect, bool>> all = new List<KeyValuePair<Int32Rect, bool>>();
                MonitorEnumProc cb = delegate(IntPtr h, IntPtr dc, ref RECT rc, IntPtr d)
                {
                    MONITORINFO mi = new MONITORINFO();
                    mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                    if (Native.GetMonitorInfo(h, ref mi))
                    {
                        RECT r = mi.rcMonitor;
                        all.Add(new KeyValuePair<Int32Rect, bool>(new Int32Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), (mi.dwFlags & 1) != 0));
                    }
                    return true;
                };
                Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
                GC.KeepAlive(cb);
                all.Sort(delegate(KeyValuePair<Int32Rect, bool> a, KeyValuePair<Int32Rect, bool> b)
                {
                    return a.Key.X != b.Key.X ? a.Key.X.CompareTo(b.Key.X) : a.Key.Y.CompareTo(b.Key.Y);
                });
                foreach (KeyValuePair<Int32Rect, bool> m in all)
                    if (mode == "Each" || m.Value) list.Add(m.Key);
            }
            if (list.Count == 0 || list[0].Width < 1 || list[0].Height < 1)
            {
                list.Clear();
                list.Add(new Int32Rect(0, 0, Math.Max(640, Native.GetSystemMetrics(0)), Math.Max(480, Native.GetSystemMetrics(1))));
            }
            return list;
        }

        // Classic layout (Win10 / Win11 before 24H2): after message 0x052C the icons
        // move into a top-level WorkerW, and the WorkerW right behind it is ours.
        static IntPtr FindClassic(IntPtr progman)
        {
            IntPtr result = IntPtr.Zero;
            EnumWindowsProc cb = delegate(IntPtr h, IntPtr l)
            {
                if (h != progman && Native.FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                {
                    IntPtr w = Native.FindWindowEx(IntPtr.Zero, h, "WorkerW", null);
                    if (w != IntPtr.Zero) { result = w; return false; }
                }
                return true;
            };
            Native.EnumWindows(cb, IntPtr.Zero);
            GC.KeepAlive(cb);
            return result;
        }

        public static IntPtr FindHost(out IntPtr insertAfter, out string kind)
        {
            insertAfter = new IntPtr(1);   // HWND_BOTTOM
            kind = "";
            IntPtr progman = Native.FindWindow("Progman", null);
            if (progman == IntPtr.Zero) progman = Native.GetShellWindow();
            if (progman == IntPtr.Zero) return IntPtr.Zero;

            IntPtr w = FindClassic(progman);
            int[,] v = { { 0xD, 0 }, { 0xD, 1 }, { 0, 0 } };
            for (int i = 0; i < 3 && w == IntPtr.Zero; i++)
            {
                IntPtr r;
                Native.SendMessageTimeout(progman, 0x052C, new IntPtr(v[i, 0]), new IntPtr(v[i, 1]), 0, 1000, out r);
                Thread.Sleep(50);
                w = FindClassic(progman);
            }
            if (w != IntPtr.Zero) { kind = "WorkerW"; return w; }

            // Windows 11 24H2+: icons and wallpaper are children of Progman.
            // Our window goes between them: right behind SHELLDLL_DefView.
            IntPtr def = Native.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            IntPtr ww = Native.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            if (def != IntPtr.Zero && ww != IntPtr.Zero) { insertAfter = def; kind = "Progman (24H2 layout)"; return progman; }

            kind = "Progman (fallback)";
            return progman;
        }

        public static bool ExplorerRunning()
        {
            try
            {
                int sid = Process.GetCurrentProcess().SessionId;
                foreach (Process p in Process.GetProcessesByName("explorer"))
                {
                    bool mine = p.SessionId == sid;
                    p.Dispose();
                    if (mine) return true;
                }
            }
            catch { }
            return false;
        }
    }
}
