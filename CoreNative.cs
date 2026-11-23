using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace CorePulse
{
    internal static class Loc
    {
        public static string DetectDefaultLanguage()
        {
            try
            {
                string iso = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
                if (iso == "tr") return "tr";
                if (iso == "zh") return "zh";
            }
            catch { }
            return "en";
        }

        public static string T(string lang, string en, string tr, string zh)
        {
            if (lang == "tr") return tr;
            if (lang == "zh") return zh;
            return en;
        }

        public static string FontFamily(string lang)
        {
            return lang == "zh" ? "Microsoft YaHei UI" : "Segoe UI";
        }
    }

    internal enum OverlayAnchor
    {
        TopLeft = 0,
        TopCenter = 1,
        TopRight = 2,
        BottomLeft = 3,
        BottomCenter = 4,
        BottomRight = 5
    }

    internal enum FanProfileMode
    {
        BiosAuto = 0,
        Silent = 1,
        Balanced = 2,
        Turbo = 3,
        Custom = 4
    }

    internal sealed class CorePulseSettings
    {
        public string Language = Loc.DetectDefaultLanguage();

        // Always-On-Top Overlay HUD configuration
        public bool OverlayEnabled = false;
        public OverlayAnchor OverlayPosition = OverlayAnchor.TopLeft;
        public bool OverlayHorizontal = true;
        public int OverlayOpacity = 88; // 55..100
        public int OverlayScale = 1;    // 0=Compact, 1=Normal, 2=Large
        public int OverlayMargin = 14;

        // 14 Individual HUD Metric Checkboxes
        public bool HudShowFps = true;
        public bool HudShowFps1Low = true;
        public bool HudShowCpuTemp = true;
        public bool HudShowCpuLoad = true;
        public bool HudShowCpuClockPower = false;
        public bool HudShowGpuTemp = true;
        public bool HudShowGpuLoad = true;
        public bool HudShowGpuVram = true;
        public bool HudShowGpuFan = true;
        public bool HudShowGpuPower = false;
        public bool HudShowRam = true;
        public bool HudShowCaseFans = false;
        public bool HudShowBoardDiskTemp = false;
        public bool HudShowNetClock = false;

        // Fan Control Profile & Thermal Safety Override
        public FanProfileMode FanProfile = FanProfileMode.BiosAuto;
        public bool CurveFollowsGpu = false; // false = Max(CPU, GPU) / CPU, true = GPU
        public int[] CurveTemps = new int[] { 35, 50, 65, 75, 85 };
        public int[] CurveDuties = new int[] { 35, 50, 70, 88, 100 };

        public static string GetSettingsPath()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(Path.Combine(appData, "CorePulse"), "settings.ini");
        }

        public static CorePulseSettings Load()
        {
            CorePulseSettings s = new CorePulseSettings();
            try
            {
                string path = GetSettingsPath();
                if (!File.Exists(path)) return s;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = raw.Substring(0, eq).Trim();
                    string v = raw.Substring(eq + 1).Trim();
                    int iv;
                    if (k == "Language" && (v == "tr" || v == "en" || v == "zh")) s.Language = v;
                    else if (k == "OverlayEnabled") s.OverlayEnabled = (v == "1");
                    else if (k == "OverlayPosition" && int.TryParse(v, out iv) && iv >= 0 && iv <= 5) s.OverlayPosition = (OverlayAnchor)iv;
                    else if (k == "OverlayHorizontal") s.OverlayHorizontal = (v == "1");
                    else if (k == "OverlayOpacity" && int.TryParse(v, out iv)) s.OverlayOpacity = Math.Max(55, Math.Min(100, iv));
                    else if (k == "OverlayScale" && int.TryParse(v, out iv) && iv >= 0 && iv <= 2) s.OverlayScale = iv;
                    else if (k == "OverlayMargin" && int.TryParse(v, out iv)) s.OverlayMargin = Math.Max(4, Math.Min(60, iv));
                    else if (k == "HudShowFps") s.HudShowFps = (v == "1");
                    else if (k == "HudShowFps1Low") s.HudShowFps1Low = (v == "1");
                    else if (k == "HudShowCpuTemp") s.HudShowCpuTemp = (v == "1");
                    else if (k == "HudShowCpuLoad") s.HudShowCpuLoad = (v == "1");
                    else if (k == "HudShowCpuClockPower") s.HudShowCpuClockPower = (v == "1");
                    else if (k == "HudShowGpuTemp") s.HudShowGpuTemp = (v == "1");
                    else if (k == "HudShowGpuLoad") s.HudShowGpuLoad = (v == "1");
                    else if (k == "HudShowGpuVram") s.HudShowGpuVram = (v == "1");
                    else if (k == "HudShowGpuFan") s.HudShowGpuFan = (v == "1");
                    else if (k == "HudShowGpuPower") s.HudShowGpuPower = (v == "1");
                    else if (k == "HudShowRam") s.HudShowRam = (v == "1");
                    else if (k == "HudShowCaseFans") s.HudShowCaseFans = (v == "1");
                    else if (k == "HudShowBoardDiskTemp") s.HudShowBoardDiskTemp = (v == "1");
                    else if (k == "HudShowNetClock") s.HudShowNetClock = (v == "1");
                    else if (k == "FanProfile" && int.TryParse(v, out iv) && iv >= 0 && iv <= 4) s.FanProfile = (FanProfileMode)iv;
                    else if (k == "CurveFollowsGpu") s.CurveFollowsGpu = (v == "1");
                    else if (k == "CurveDuties")
                    {
                        string[] parts = v.Split(',');
                        if (parts.Length == 5)
                        {
                            for (int i = 0; i < 5; i++)
                            {
                                int d;
                                if (int.TryParse(parts[i], out d)) s.CurveDuties[i] = Math.Max(25, Math.Min(100, d));
                            }
                        }
                    }
                }
            }
            catch { }
            return s;
        }

        public void Save()
        {
            try
            {
                string path = GetSettingsPath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Language=" + (Language ?? "en"));
                sb.AppendLine("OverlayEnabled=" + (OverlayEnabled ? "1" : "0"));
                sb.AppendLine("OverlayPosition=" + ((int)OverlayPosition));
                sb.AppendLine("OverlayHorizontal=" + (OverlayHorizontal ? "1" : "0"));
                sb.AppendLine("OverlayOpacity=" + OverlayOpacity);
                sb.AppendLine("OverlayScale=" + OverlayScale);
                sb.AppendLine("OverlayMargin=" + OverlayMargin);
                sb.AppendLine("HudShowFps=" + (HudShowFps ? "1" : "0"));
                sb.AppendLine("HudShowFps1Low=" + (HudShowFps1Low ? "1" : "0"));
                sb.AppendLine("HudShowCpuTemp=" + (HudShowCpuTemp ? "1" : "0"));
                sb.AppendLine("HudShowCpuLoad=" + (HudShowCpuLoad ? "1" : "0"));
                sb.AppendLine("HudShowCpuClockPower=" + (HudShowCpuClockPower ? "1" : "0"));
                sb.AppendLine("HudShowGpuTemp=" + (HudShowGpuTemp ? "1" : "0"));
                sb.AppendLine("HudShowGpuLoad=" + (HudShowGpuLoad ? "1" : "0"));
                sb.AppendLine("HudShowGpuVram=" + (HudShowGpuVram ? "1" : "0"));
                sb.AppendLine("HudShowGpuFan=" + (HudShowGpuFan ? "1" : "0"));
                sb.AppendLine("HudShowGpuPower=" + (HudShowGpuPower ? "1" : "0"));
                sb.AppendLine("HudShowRam=" + (HudShowRam ? "1" : "0"));
                sb.AppendLine("HudShowCaseFans=" + (HudShowCaseFans ? "1" : "0"));
                sb.AppendLine("HudShowBoardDiskTemp=" + (HudShowBoardDiskTemp ? "1" : "0"));
                sb.AppendLine("HudShowNetClock=" + (HudShowNetClock ? "1" : "0"));
                sb.AppendLine("FanProfile=" + ((int)FanProfile));
                sb.AppendLine("CurveFollowsGpu=" + (CurveFollowsGpu ? "1" : "0"));
                sb.AppendLine("CurveDuties=" + string.Format("{0},{1},{2},{3},{4}", CurveDuties[0], CurveDuties[1], CurveDuties[2], CurveDuties[3], CurveDuties[4]));
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public void ApplyHudPreset(string preset)
        {
            if (preset == "minimal")
            {
                HudShowFps = true; HudShowFps1Low = false;
                HudShowCpuTemp = true; HudShowCpuLoad = false; HudShowCpuClockPower = false;
                HudShowGpuTemp = true; HudShowGpuLoad = false; HudShowGpuVram = false; HudShowGpuFan = false; HudShowGpuPower = false;
                HudShowRam = false; HudShowCaseFans = false; HudShowBoardDiskTemp = false; HudShowNetClock = false;
            }
            else if (preset == "gamer")
            {
                HudShowFps = true; HudShowFps1Low = true;
                HudShowCpuTemp = true; HudShowCpuLoad = true; HudShowCpuClockPower = false;
                HudShowGpuTemp = true; HudShowGpuLoad = true; HudShowGpuVram = true; HudShowGpuFan = true; HudShowGpuPower = false;
                HudShowRam = true; HudShowCaseFans = false; HudShowBoardDiskTemp = false; HudShowNetClock = false;
            }
            else if (preset == "thermal")
            {
                HudShowFps = false; HudShowFps1Low = false;
                HudShowCpuTemp = true; HudShowCpuLoad = true; HudShowCpuClockPower = true;
                HudShowGpuTemp = true; HudShowGpuLoad = true; HudShowGpuVram = false; HudShowGpuFan = true; HudShowGpuPower = true;
                HudShowRam = false; HudShowCaseFans = true; HudShowBoardDiskTemp = true; HudShowNetClock = false;
            }
        }
    }

    internal static class WinNative
    {
        public const int WM_DPICHANGED = 0x02E0;
        public const int WM_HOTKEY = 0x0312;
        public const int HOTKEY_TOGGLE_HUD = 0x4350;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint VK_O = 0x4F;

        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;
        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new IntPtr(-4);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }

        public static void ApplyDarkTitleBar(IntPtr hwnd, Color captionColor, Color textColor)
        {
            try
            {
                int enabled = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref enabled, sizeof(int));
                int capRef = captionColor.R | (captionColor.G << 8) | (captionColor.B << 16);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref capRef, sizeof(int));
                int txtRef = textColor.R | (textColor.G << 8) | (textColor.B << 16);
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref txtRef, sizeof(int));
            }
            catch { }
        }

        public static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity id = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal p = new WindowsPrincipal(id);
                    return p.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        public static bool IsPawnIoInstalled()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO", false))
                    return k != null;
            }
            catch { return false; }
        }

        public static bool RestartAsAdministrator(string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Process.GetCurrentProcess().MainModule.FileName;
                psi.Arguments = args ?? "";
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                Process.Start(psi);
                return true;
            }
            catch { return false; }
        }

        public static void OpenUrl(string url)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(url);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch { }
        }

        public static uint GetForegroundPid()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return 0;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                return pid;
            }
            catch { return 0; }
        }
    }
}
