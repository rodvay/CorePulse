using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CorePulse
{
    // True 32-bit Per-Pixel Alpha (UpdateLayeredWindow), Click-Through, Zero-Focus-Stealing,
    // Multi-Monitor Always-On-Top Gaming HUD Overlay — zero jagged TransparencyKey edges!
    internal sealed class OverlayHudForm : Form
    {
        private sealed class HudEntry
        {
            public string Label;
            public string Value;
            public Color Accent;
            public Color ValueColor;
        }

        private static readonly Color ColHudBgBase = Color.FromArgb(12, 16, 23);
        private static readonly Color ColHudBorder = Color.FromArgb(62, 78, 102);
        private static readonly Color ColSep = Color.FromArgb(46, 58, 76);
        private static readonly Color ColLabel = Color.FromArgb(148, 163, 184);
        private static readonly Color ColText = Color.FromArgb(241, 245, 249);
        private static readonly Color ColCyan = Color.FromArgb(56, 189, 248);
        private static readonly Color ColEmerald = Color.FromArgb(52, 211, 153);
        private static readonly Color ColAmber = Color.FromArgb(251, 191, 36);
        private static readonly Color ColCoral = Color.FromArgb(248, 113, 113);
        private static readonly Color ColViolet = Color.FromArgb(167, 139, 250);

        private CorePulseSettings _cfg = new CorePulseSettings();
        private TelemetrySnapshot _snap = new TelemetrySnapshot();
        private float _dpiScale = 1.0f;
        private bool _dragUnlocked;
        private bool _dragging;
        private Point _dragStartMouse;
        private Point _dragStartWin;
        private int _lastBmpW;
        private int _lastBmpH;

        public event Action<int, int> CustomPositionChanged;
        public event Action LockRequested;

        public bool DragUnlocked { get { return _dragUnlocked; } }

        public OverlayHudForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WinNative.WS_EX_TOPMOST
                            | WinNative.WS_EX_LAYERED
                            | WinNative.WS_EX_NOACTIVATE
                            | WinNative.WS_EX_TOOLWINDOW;
                if (!_dragUnlocked)
                    cp.ExStyle |= WinNative.WS_EX_TRANSPARENT;
                return cp;
            }
        }

        public void SetDragUnlocked(bool unlocked)
        {
            _dragUnlocked = unlocked;
            if (!unlocked)
            {
                _dragging = false;
                try { Capture = false; } catch { }
            }
            Cursor = unlocked ? Cursors.SizeAll : Cursors.Default;
            ApplyClickThroughStyle();
            if (IsHandleCreated)
                UpdateHud(_cfg, _snap, _dpiScale);
        }

        private void ApplyClickThroughStyle()
        {
            if (!IsHandleCreated) return;
            try
            {
                long ex = WinNative.GetWindowLongPtr(Handle, WinNative.GWL_EXSTYLE).ToInt64();
                if (_dragUnlocked)
                    ex &= ~(long)WinNative.WS_EX_TRANSPARENT;
                else
                    ex |= (long)WinNative.WS_EX_TRANSPARENT;
                WinNative.SetWindowLongPtr(Handle, WinNative.GWL_EXSTYLE, new IntPtr(ex));
                WinNative.SetWindowPos(
                    Handle, WinNative.HWND_TOPMOST, 0, 0, 0, 0,
                    WinNative.SWP_NOMOVE | WinNative.SWP_NOSIZE | WinNative.SWP_NOACTIVATE | WinNative.SWP_FRAMECHANGED);
            }
            catch { }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!_dragUnlocked) return;
            if (e.Button == MouseButtons.Right)
            {
                if (LockRequested != null) LockRequested();
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragStartMouse = Cursor.Position;
                _dragStartWin = Location;
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging || !_dragUnlocked) return;
            Point cur = Cursor.Position;
            int nx = _dragStartWin.X + (cur.X - _dragStartMouse.X);
            int ny = _dragStartWin.Y + (cur.Y - _dragStartMouse.Y);
            Point clamped = ClampToVirtualScreen(nx, ny, _lastBmpW > 0 ? _lastBmpW : Width, _lastBmpH > 0 ? _lastBmpH : Height);
            WinNative.SetWindowPos(
                Handle, WinNative.HWND_TOPMOST, clamped.X, clamped.Y, 0, 0,
                WinNative.SWP_NOSIZE | WinNative.SWP_NOACTIVATE);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging && e.Button == MouseButtons.Left)
            {
                _dragging = false;
                Capture = false;
                Point pos = Location;
                _cfg.OverlayCustomPos = true;
                _cfg.OverlayCustomX = pos.X;
                _cfg.OverlayCustomY = pos.Y;
                if (CustomPositionChanged != null)
                    CustomPositionChanged(pos.X, pos.Y);
            }
        }

        public static Color TempColor(float c)
        {
            if (c < 0f) return ColLabel;
            if (c >= 82f) return ColCoral;
            if (c >= 68f) return ColAmber;
            return ColEmerald;
        }

        public static Screen ResolveTargetScreen(int monitorIndex)
        {
            Screen[] all = Screen.AllScreens;
            if (monitorIndex > 0 && all != null && monitorIndex <= all.Length)
                return all[monitorIndex - 1];
            return Screen.PrimaryScreen ?? (all != null && all.Length > 0 ? all[0] : null);
        }

        public void UpdateHud(CorePulseSettings cfg, TelemetrySnapshot snap, float dpiScale)
        {
            _cfg = cfg ?? new CorePulseSettings();
            _snap = snap ?? new TelemetrySnapshot();
            _dpiScale = dpiScale > 0.5f ? dpiScale : 1.0f;

            using (Bitmap bmp = RenderHudBitmap(_cfg, _snap, _dpiScale, _dragUnlocked))
            {
                _lastBmpW = bmp.Width;
                _lastBmpH = bmp.Height;
                Point dst = _dragging ? Location : ComputeTargetPosition(bmp.Width, bmp.Height);
                if (IsHandleCreated)
                {
                    PushLayeredBitmap(bmp, dst);
                    EnsureTopMost();
                }
                else
                {
                    SetBounds(dst.X, dst.Y, bmp.Width, bmp.Height);
                }
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyClickThroughStyle();
            UpdateHud(_cfg, _snap, _dpiScale);
        }

        public void EnsureTopMost()
        {
            if (Visible && IsHandleCreated)
            {
                WinNative.SetWindowPos(
                    Handle,
                    WinNative.HWND_TOPMOST,
                    0, 0, 0, 0,
                    WinNative.SWP_NOMOVE | WinNative.SWP_NOSIZE | WinNative.SWP_NOACTIVATE);
            }
        }

        internal static Point ClampToVirtualScreen(int x, int y, int w, int h)
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            if (vs.Width <= 0 || vs.Height <= 0)
                vs = new Rectangle(0, 0, 1920, 1080);
            if (x < vs.Left) x = vs.Left;
            if (y < vs.Top) y = vs.Top;
            if (x + w > vs.Right) x = Math.Max(vs.Left, vs.Right - w);
            if (y + h > vs.Bottom) y = Math.Max(vs.Top, vs.Bottom - h);
            return new Point(x, y);
        }

        internal static Point ComputeAnchoredPosition(CorePulseSettings cfg, Rectangle bounds, Rectangle workingArea, int w, int h, float dpiScale)
        {
            if (cfg.OverlayCustomPos)
                return ClampToVirtualScreen(cfg.OverlayCustomX, cfg.OverlayCustomY, w, h);

            int margin = (int)Math.Round(Math.Max(0, cfg.OverlayMargin) * dpiScale);

            int x = bounds.Left + margin;
            int y = bounds.Top + margin;

            if (cfg.OverlayPosition == OverlayAnchor.TopCenter || cfg.OverlayPosition == OverlayAnchor.BottomCenter)
                x = bounds.Left + (bounds.Width - w) / 2;
            else if (cfg.OverlayPosition == OverlayAnchor.TopRight || cfg.OverlayPosition == OverlayAnchor.BottomRight)
                x = bounds.Right - w - margin;

            if (cfg.OverlayPosition == OverlayAnchor.BottomLeft || cfg.OverlayPosition == OverlayAnchor.BottomCenter || cfg.OverlayPosition == OverlayAnchor.BottomRight)
            {
                int workBottom = workingArea.Height > 0 ? workingArea.Bottom : bounds.Bottom;
                y = Math.Min(bounds.Bottom, workBottom) - h - margin;
            }

            // Clamp flush to target monitor edges (allows exact 0px flush top/side/bottom!)
            if (x < bounds.Left) x = bounds.Left;
            if (y < bounds.Top) y = bounds.Top;
            if (x + w > bounds.Right) x = Math.Max(bounds.Left, bounds.Right - w);
            if (y + h > bounds.Bottom) y = Math.Max(bounds.Top, bounds.Bottom - h);

            return new Point(x, y);
        }

        private Point ComputeTargetPosition(int w, int h)
        {
            Screen sc = ResolveTargetScreen(_cfg.OverlayMonitorIndex);
            Rectangle bounds = sc != null ? sc.Bounds : new Rectangle(0, 0, 1920, 1080);
            Rectangle work = sc != null ? sc.WorkingArea : bounds;
            return ComputeAnchoredPosition(_cfg, bounds, work, w, h, _dpiScale);
        }

        private void PushLayeredBitmap(Bitmap bmp, Point dst)
        {
            IntPtr screenDc = WinNative.GetDC(IntPtr.Zero);
            IntPtr memDc = WinNative.CreateCompatibleDC(screenDc);
            IntPtr hBitmap = IntPtr.Zero;
            IntPtr oldBitmap = IntPtr.Zero;
            try
            {
                hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
                oldBitmap = WinNative.SelectObject(memDc, hBitmap);

                WinNative.SIZE size = new WinNative.SIZE(bmp.Width, bmp.Height);
                WinNative.POINT ptSrc = new WinNative.POINT(0, 0);
                WinNative.POINT ptDst = new WinNative.POINT(dst.X, dst.Y);
                WinNative.BLENDFUNCTION blend = new WinNative.BLENDFUNCTION();
                blend.BlendOp = WinNative.AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255; // Per-pixel alpha is baked smoothly into the ARGB bitmap!
                blend.AlphaFormat = WinNative.AC_SRC_ALPHA;

                WinNative.UpdateLayeredWindow(Handle, screenDc, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, WinNative.ULW_ALPHA);
            }
            finally
            {
                if (oldBitmap != IntPtr.Zero) WinNative.SelectObject(memDc, oldBitmap);
                if (hBitmap != IntPtr.Zero) WinNative.DeleteObject(hBitmap);
                WinNative.DeleteDC(memDc);
                WinNative.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static List<HudEntry> BuildEntries(CorePulseSettings c, TelemetrySnapshot s)
        {
            List<HudEntry> list = new List<HudEntry>();

            if (c.HudShowFps)
            {
                if (HudSensorResolver.HasCustomSensor(c, "HudShowFps"))
                {
                    Color vc;
                    string txt = HudSensorResolver.FormatSelectedForHud(HudSensorResolver.ResolveSelectedOptions(c, s, "HudShowFps"), out vc);
                    list.Add(new HudEntry { Label = "FPS", Value = txt, Accent = ColEmerald, ValueColor = vc });
                }
                else
                {
                    string val = s.Fps > 0.5f
                        ? Loc.F("{0:0} ({1:0.0}ms)", s.Fps, s.FrametimeMs)
                        : (s.FpsEtwActive ? "--" : "ADMIN");
                    Color vc = s.Fps >= 60f ? ColEmerald : (s.Fps >= 30f ? ColAmber : ColLabel);
                    list.Add(new HudEntry { Label = "FPS", Value = val, Accent = ColEmerald, ValueColor = vc });
                }
            }

            if (c.HudShowFps1Low && (HudSensorResolver.HasCustomSensor(c, "HudShowFps1Low") || s.Fps > 0.5f))
            {
                if (HudSensorResolver.HasCustomSensor(c, "HudShowFps1Low"))
                {
                    Color vc;
                    string txt = HudSensorResolver.FormatSelectedForHud(HudSensorResolver.ResolveSelectedOptions(c, s, "HudShowFps1Low"), out vc);
                    list.Add(new HudEntry { Label = "1% LOW", Value = txt, Accent = ColEmerald, ValueColor = vc });
                }
                else
                {
                    list.Add(new HudEntry { Label = "1% LOW", Value = Loc.F("{0:0}", s.Fps1Low), Accent = ColEmerald, ValueColor = s.Fps1Low >= 45f ? ColText : ColAmber });
                }
            }

            if (c.HudShowCpuTemp || c.HudShowCpuLoad || c.HudShowCpuClockPower)
            {
                List<string> parts = new List<string>();
                Color cpuValCol = ColText;
                if (c.HudShowCpuTemp)
                {
                    if (HudSensorResolver.HasCustomSensor(c, "HudShowCpuTemp"))
                    {
                        Color customCol;
                        parts.Add(HudSensorResolver.FormatSelectedForHud(HudSensorResolver.ResolveSelectedOptions(c, s, "HudShowCpuTemp"), out customCol));
                        cpuValCol = customCol;
                    }
                    else
                    {
                        parts.Add(s.CpuTempC > 0f ? Loc.F("{0:0}°C", s.CpuTempC) : "--°C");
                        if (s.CpuTempC > 0f) cpuValCol = TempColor(s.CpuTempC);
                    }
                }
                if (c.HudShowCpuLoad)
                {
                    if (HudSensorResolver.HasCustomSensor(c, "HudShowCpuLoad"))
                    {
                        Color unused;
                        parts.Add(HudSensorResolver.FormatSelectedForHud(HudSensorResolver.ResolveSelectedOptions(c, s, "HudShowCpuLoad"), out unused));
                    }
                    else
                    {
                        parts.Add(Loc.F("{0:0}%", s.CpuLoadPct));
                    }
                }
                if (c.HudShowCpuClockPower)
                {
                    if (HudSensorResolver.HasCustomSensor(c, "HudShowCpuClockPower"))
                    {
                        Color unused;
                        parts.Add(HudSensorResolver.FormatSelectedForHud(HudSensorResolver.ResolveSelectedOptions(c, s, "HudShowCpuClockPower"), out unused));
                    }
                    else
                    {
                        if (s.CpuClockMhz > 100f) parts.Add(Loc.F("{0:0.0}GHz", s.CpuClockMhz / 1000f));
                        if (s.CpuPowerW > 1f) parts.Add(Loc.F("{0:0}W", s.CpuPowerW));
                    }
                }
                list.Add(new HudEntry
                {
                    Label = "CPU",
                    Value = string.Join(" · ", parts.ToArray()),
                    Accent = ColCyan,
                    ValueColor = cpuValCol
                });
            }

            if (c.HudShowGpuTemp || c.HudShowGpuLoad || c.HudShowGpuVram || c.HudShowGpuFan || c.HudShowGpuPower)
            {
                List<string> parts = new List<string>();
                Color gpuValCol = ColText;
                if (c.HudShowGpuTemp)
                {
                    if (HudSensorResolver.HasCustomSensor(c, "HudShowGpuTemp"))
                    {
                        Color customCol;
                        parts.Add(HudSensorResolver.FormatSelectedForHud(HudSensorResolver.ResolveSelectedOptions(c, s, "HudShowGpuTemp"), out customCol));
                        gpuValCol = customCol;
                    }
                    else
                    {
                        parts.Add(s.GpuTempC > 0f ? Loc.F("{0:0}°C", s.GpuTempC) : "--°C");
                        if (s.GpuTempC > 0f) gpuValCol = TempColor(s.GpuTempC);
                    }
                }
                if (c.HudShowGpuLoad)
                {
                    if (HudSensorResolver.HasCustomSensor(c, "HudShowGpuLoad"))
                    {
                        Color unused;
                        parts.Add(HudSensorResolver.FormatSelectedForHud(HudSensorResolver.ResolveSelectedOptions(c, s, "HudShowGpuLoad"), out unused));
                    }
                    else if (s.GpuLoadPct >= 0f)
                    {
                        parts.Add(Loc.F("{0:0}%", s.GpuLoadPct));
                    }
                }
                if (c.HudShowGpuVram)
                {
                    if (HudSensorResolver.HasCustomSensor(c, "HudShowGpuVram"))
                    {
                        Color unused;
                        parts.Add(HudSensorResolver.FormatSelectedForHud(HudSensorResolver.ResolveSelectedOptions(c, s, "HudShowGpuVram"), out unused));
                    }
                    else if (s.GpuVramUsedGb >= 0f)
                    {
                        parts.Add(Loc.F("{0:0.0}GB", s.GpuVramUsedGb));
                    }
                }
                if (c.HudShowGpuFan)
                {
                    if (HudSensorResolver.HasCustomSensor(c, "HudShowGpuFan"))
                    {
                        Color unused;
                        parts.Add(HudSensorResolver.FormatSelectedForHud(HudSensorResolver.ResolveSelectedOptions(c, s, "HudShowGpuFan"), out unused));
                    }
                    else if (s.GpuFanRpm > 0f)
                    {
                        parts.Add(Loc.F("{0:0} RPM", s.GpuFanRpm));
                    }
                    else if (s.GpuFanPct >= 0f)
                    {
                        parts.Add(Loc.F("FAN {0:0}%", s.GpuFanPct));
                    }
                }
                if (c.HudShowGpuPower)
                {
                    if (HudSensorResolver.HasCustomSensor(c, "HudShowGpuPower"))
                    {
                        Color unused;
                        parts.Add(HudSensorResolver.FormatSelectedForHud(HudSensorResolver.ResolveSelectedOptions(c, s, "HudShowGpuPower"), out unused));
                    }
                    else if (s.GpuPowerW > 1f)
                    {
                        parts.Add(Loc.F("{0:0}W", s.GpuPowerW));
                    }
                }

                if (parts.Count > 0)
                {
                    list.Add(new HudEntry
                    {
                        Label = "GPU",
                        Value = string.Join(" · ", parts.ToArray()),
                        Accent = ColEmerald,
                        ValueColor = gpuValCol
                    });
                }
            }

            if (c.HudShowRam && (HudSensorResolver.HasCustomSensor(c, "HudShowRam") || s.RamTotalGb > 0f))
            {
                if (HudSensorResolver.HasCustomSensor(c, "HudShowRam"))
                {
                    Color vc;
                    string txt = HudSensorResolver.FormatSelectedForHud(HudSensorResolver.ResolveSelectedOptions(c, s, "HudShowRam"), out vc);
                    list.Add(new HudEntry { Label = "RAM", Value = txt, Accent = ColViolet, ValueColor = vc });
                }
                else
                {
                    list.Add(new HudEntry
                    {
                        Label = "RAM",
                        Value = Loc.F("{0:0.0}/{1:0}GB ({2:0}%)", s.RamUsedGb, s.RamTotalGb, s.RamLoadPct),
                        Accent = ColViolet,
                        ValueColor = s.RamLoadPct >= 88f ? ColAmber : ColText
                    });
                }
            }

            if (c.HudShowCaseFans)
            {
                if (HudSensorResolver.HasCustomSensor(c, "HudShowCaseFans"))
                {
                    Color vc;
                    string txt = HudSensorResolver.FormatSelectedForHud(HudSensorResolver.ResolveSelectedOptions(c, s, "HudShowCaseFans"), out vc);
                    list.Add(new HudEntry { Label = "FAN", Value = txt, Accent = ColCyan, ValueColor = vc });
                }
                else
                {
                    string fVal = s.PrimaryCaseFanRpm > 0f
                        ? Loc.F("{0:0} RPM", s.PrimaryCaseFanRpm)
                        : (s.GpuFanRpm > 0f ? Loc.F("{0:0} RPM", s.GpuFanRpm) : "-- RPM");
                    list.Add(new HudEntry { Label = "FAN", Value = fVal, Accent = ColCyan, ValueColor = ColText });
                }
            }

            if (c.HudShowBoardDiskTemp)
            {
                if (HudSensorResolver.HasCustomSensor(c, "HudShowBoardDiskTemp"))
                {
                    Color vc;
                    string txt = HudSensorResolver.FormatSelectedForHud(HudSensorResolver.ResolveSelectedOptions(c, s, "HudShowBoardDiskTemp"), out vc);
                    list.Add(new HudEntry { Label = "SYS", Value = txt, Accent = ColAmber, ValueColor = vc });
                }
                else
                {
                    List<string> pts = new List<string>();
                    if (s.BoardTempC > 0f) pts.Add(Loc.F("MB {0:0}°C", s.BoardTempC));
                    if (s.StorageTempC > 0f) pts.Add(Loc.F("SSD {0:0}°C", s.StorageTempC));
                    if (pts.Count == 0) pts.Add("--°C");
                    list.Add(new HudEntry { Label = "SYS", Value = string.Join(" · ", pts.ToArray()), Accent = ColAmber, ValueColor = ColText });
                }
            }

            if (c.HudShowNetClock)
            {
                if (HudSensorResolver.HasCustomSensor(c, "HudShowNetClock"))
                {
                    Color vc;
                    string txt = HudSensorResolver.FormatSelectedForHud(HudSensorResolver.ResolveSelectedOptions(c, s, "HudShowNetClock"), out vc);
                    list.Add(new HudEntry { Label = "NET", Value = txt, Accent = ColCyan, ValueColor = vc });
                }
                else
                {
                    list.Add(new HudEntry
                    {
                        Label = "NET",
                        Value = Loc.F("↓{0:0.0} ↑{1:0.0} · {2}", s.NetDownMbps, s.NetUpMbps, DateTime.Now.ToString("HH:mm")),
                        Accent = ColCyan,
                        ValueColor = ColText
                    });
                }
            }

            if (list.Count == 0)
                list.Add(new HudEntry { Label = "COREPULSE", Value = Loc.F("CPU {0:0}%", s.CpuLoadPct), Accent = ColCyan, ValueColor = ColText });

            return list;
        }

        public static Bitmap RenderHudBitmap(CorePulseSettings c, TelemetrySnapshot s, float dpiScale)
        {
            return RenderHudBitmap(c, s, dpiScale, false);
        }

        public static Bitmap RenderHudBitmap(CorePulseSettings c, TelemetrySnapshot s, float dpiScale, bool dragUnlocked)
        {
            float sizeMult = c.OverlayScale == 0 ? 0.86f : (c.OverlayScale == 2 ? 1.18f : 1.0f);
            float sf = (dpiScale > 0.5f ? dpiScale : 1.0f) * sizeMult;
            string fam = Loc.FontFamily(c.Language);

            List<HudEntry> entries = BuildEntries(c, s);

            using (Font fLabel = new Font(fam, 7.4f * (96f / 72f) * sf, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font fVal = new Font("Segoe UI", 9.4f * (96f / 72f) * sf, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                int w, h;
                float maxLbl = 0f;
                using (Bitmap tmp = new Bitmap(8, 8))
                using (Graphics tg = Graphics.FromImage(tmp))
                {
                    tg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    if (c.OverlayHorizontal)
                    {
                        float totalW = 11f * sf;
                        for (int i = 0; i < entries.Count; i++)
                        {
                            float lw = MeasureTight(tg, entries[i].Label, fLabel).Width + 2f * sf;
                            float vw = MeasureTight(tg, entries[i].Value, fVal).Width + 2f * sf;
                            totalW += 6.5f * sf + lw + 5f * sf + vw + (i < entries.Count - 1 ? 15f * sf : 12f * sf);
                        }
                        w = Math.Max((int)Math.Ceiling(130f * sf), (int)Math.Ceiling(totalW));
                        h = (int)Math.Ceiling(34f * sf);
                    }
                    else
                    {
                        float maxVal = 0f;
                        for (int i = 0; i < entries.Count; i++)
                        {
                            maxLbl = Math.Max(maxLbl, MeasureTight(tg, entries[i].Label, fLabel).Width);
                            maxVal = Math.Max(maxVal, MeasureTight(tg, entries[i].Value, fVal).Width);
                        }
                        w = (int)Math.Ceiling(34f * sf + maxLbl + 14f * sf + maxVal + 14f * sf);
                        h = (int)Math.Ceiling(16f * sf + entries.Count * 22f * sf);
                    }
                }

                // True 32-bit premultiplied ARGB bitmap for glass-smooth per-pixel alpha!
                Bitmap bmp = new Bitmap(Math.Max(2, w), Math.Max(2, h), PixelFormat.Format32bppPArgb);
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);

                    int clampedOp = Math.Max(0, Math.Min(100, c.OverlayOpacity));
                    int bgAlpha = (int)Math.Round((clampedOp / 100.0) * 245.0);
                    int bdAlpha = bgAlpha == 0 ? 175 : Math.Min(255, bgAlpha + 30);
                    RectangleF rc = new RectangleF(0.5f, 0.5f, w - 1.5f, h - 1.5f);
                    float rad = (c.OverlayHorizontal ? 9f : 10f) * sf;

                    using (GraphicsPath gp = Rounded(rc, rad))
                    {
                        if (bgAlpha > 0)
                        {
                            using (SolidBrush bg = new SolidBrush(Color.FromArgb(bgAlpha, ColHudBgBase)))
                                g.FillPath(bg, gp);
                        }
                        if (dragUnlocked || c.OverlayBorder)
                        {
                            Color borderCol = dragUnlocked ? Color.FromArgb(255, ColCyan) : Color.FromArgb(bdAlpha, ColHudBorder);
                            float borderW = dragUnlocked ? 2.0f : 1.2f;
                            using (Pen bd = new Pen(borderCol, borderW))
                                g.DrawPath(bd, gp);
                        }
                    }

                    if (c.OverlayHorizontal)
                    {
                        float x = 11f * sf;
                        for (int i = 0; i < entries.Count; i++)
                        {
                            HudEntry it = entries[i];
                            if (i > 0)
                            {
                                using (Pen sep = new Pen(Color.FromArgb(190, ColSep), 1f))
                                    g.DrawLine(sep, x - 7f * sf, 7f * sf, x - 7f * sf, h - 7f * sf);
                            }

                            using (SolidBrush ab = new SolidBrush(Color.FromArgb(255, it.Accent)))
                                g.FillRectangle(ab, x, (h - 13f * sf) * 0.5f, 2.6f * sf, 13f * sf);
                            x += 6.5f * sf;

                            float lw = MeasureTight(g, it.Label, fLabel).Width + 2f * sf;
                            DrawTextTight(g, it.Label, fLabel, Color.FromArgb(245, ColLabel), new RectangleF(x, 0, lw, h));
                            x += lw + 5f * sf;

                            float vw = MeasureTight(g, it.Value, fVal).Width + 2f * sf;
                            DrawTextTight(g, it.Value, fVal, Color.FromArgb(255, it.ValueColor), new RectangleF(x, 0, vw, h));
                            x += vw + 15f * sf;
                        }
                    }
                    else
                    {
                        float y = 8f * sf;
                        float rowH = 22f * sf;
                        for (int i = 0; i < entries.Count; i++)
                        {
                            HudEntry it = entries[i];
                            using (SolidBrush ab = new SolidBrush(Color.FromArgb(255, it.Accent)))
                                g.FillRectangle(ab, 11f * sf, y + 4f * sf, 2.6f * sf, rowH - 8f * sf);

                            DrawTextTight(g, it.Label, fLabel, Color.FromArgb(245, ColLabel), new RectangleF(18f * sf, y, maxLbl + 4f * sf, rowH));
                            DrawTextTight(g, it.Value, fVal, Color.FromArgb(255, it.ValueColor), new RectangleF(18f * sf + maxLbl + 10f * sf, y, w - (28f * sf + maxLbl), rowH));
                            y += rowH;
                        }
                    }
                }
                return bmp;
            }
        }

        private static SizeF MeasureTight(Graphics g, string text, Font font)
        {
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                return g.MeasureString(text, font, PointF.Empty, sf);
            }
        }

        private static void DrawTextTight(Graphics g, string text, Font font, Color color, RectangleF r)
        {
            using (SolidBrush b = new SolidBrush(color))
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                g.DrawString(text, font, b, r, sf);
            }
        }

        private static GraphicsPath Rounded(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            float d = rad * 2f;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180f, 90f); p.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f); p.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
            p.CloseFigure(); return p;
        }
    }
}
