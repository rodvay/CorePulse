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
                            | WinNative.WS_EX_TRANSPARENT
                            | WinNative.WS_EX_NOACTIVATE
                            | WinNative.WS_EX_TOOLWINDOW;
                return cp;
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

            using (Bitmap bmp = RenderHudBitmap(_cfg, _snap, _dpiScale))
            {
                Point dst = ComputeTargetPosition(bmp.Width, bmp.Height);
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

        private Point ComputeTargetPosition(int w, int h)
        {
            Screen sc = ResolveTargetScreen(_cfg.OverlayMonitorIndex);
            Rectangle bounds = sc != null ? sc.Bounds : new Rectangle(0, 0, 1920, 1080);
            int margin = (int)Math.Round(_cfg.OverlayMargin * _dpiScale);

            int x = bounds.Left + margin;
            int y = bounds.Top + margin;

            if (_cfg.OverlayPosition == OverlayAnchor.TopCenter || _cfg.OverlayPosition == OverlayAnchor.BottomCenter)
                x = bounds.Left + (bounds.Width - w) / 2;
            else if (_cfg.OverlayPosition == OverlayAnchor.TopRight || _cfg.OverlayPosition == OverlayAnchor.BottomRight)
                x = bounds.Right - w - margin;

            if (_cfg.OverlayPosition == OverlayAnchor.BottomLeft || _cfg.OverlayPosition == OverlayAnchor.BottomCenter || _cfg.OverlayPosition == OverlayAnchor.BottomRight)
            {
                int workBottom = sc != null ? sc.WorkingArea.Bottom : bounds.Bottom;
                y = Math.Min(bounds.Bottom, workBottom) - h - margin;
            }

            // Clamp safely inside target monitor
            if (x < bounds.Left + 4) x = bounds.Left + 4;
            if (y < bounds.Top + 4) y = bounds.Top + 4;
            if (x + w > bounds.Right - 4) x = Math.Max(bounds.Left + 4, bounds.Right - w - 4);
            if (y + h > bounds.Bottom - 4) y = Math.Max(bounds.Top + 4, bounds.Bottom - h - 4);

            return new Point(x, y);
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
                string val = s.Fps > 0.5f
                    ? Loc.F("{0:0} ({1:0.0}ms)", s.Fps, s.FrametimeMs)
                    : (s.FpsEtwActive ? "--" : "ADMIN");
                Color vc = s.Fps >= 60f ? ColEmerald : (s.Fps >= 30f ? ColAmber : ColLabel);
                list.Add(new HudEntry { Label = "FPS", Value = val, Accent = ColEmerald, ValueColor = vc });
            }

            if (c.HudShowFps1Low && s.Fps > 0.5f)
            {
                list.Add(new HudEntry { Label = "1% LOW", Value = Loc.F("{0:0}", s.Fps1Low), Accent = ColEmerald, ValueColor = s.Fps1Low >= 45f ? ColText : ColAmber });
            }

            if (c.HudShowCpuTemp || c.HudShowCpuLoad || c.HudShowCpuClockPower)
            {
                List<string> parts = new List<string>();
                if (c.HudShowCpuTemp) parts.Add(s.CpuTempC > 0f ? Loc.F("{0:0}°C", s.CpuTempC) : "--°C");
                if (c.HudShowCpuLoad) parts.Add(Loc.F("{0:0}%", s.CpuLoadPct));
                if (c.HudShowCpuClockPower)
                {
                    if (s.CpuClockMhz > 100f) parts.Add(Loc.F("{0:0.0}GHz", s.CpuClockMhz / 1000f));
                    if (s.CpuPowerW > 1f) parts.Add(Loc.F("{0:0}W", s.CpuPowerW));
                }
                list.Add(new HudEntry
                {
                    Label = "CPU",
                    Value = string.Join(" · ", parts.ToArray()),
                    Accent = ColCyan,
                    ValueColor = c.HudShowCpuTemp && s.CpuTempC > 0f ? TempColor(s.CpuTempC) : ColText
                });
            }

            if (c.HudShowGpuTemp || c.HudShowGpuLoad || c.HudShowGpuVram || c.HudShowGpuFan || c.HudShowGpuPower)
            {
                List<string> parts = new List<string>();
                if (c.HudShowGpuTemp) parts.Add(s.GpuTempC > 0f ? Loc.F("{0:0}°C", s.GpuTempC) : "--°C");
                if (c.HudShowGpuLoad && s.GpuLoadPct >= 0f) parts.Add(Loc.F("{0:0}%", s.GpuLoadPct));
                if (c.HudShowGpuVram && s.GpuVramUsedGb >= 0f) parts.Add(Loc.F("{0:0.0}GB", s.GpuVramUsedGb));
                if (c.HudShowGpuFan)
                {
                    if (s.GpuFanRpm > 0f) parts.Add(Loc.F("{0:0} RPM", s.GpuFanRpm));
                    else if (s.GpuFanPct >= 0f) parts.Add(Loc.F("FAN {0:0}%", s.GpuFanPct));
                }
                if (c.HudShowGpuPower && s.GpuPowerW > 1f) parts.Add(Loc.F("{0:0}W", s.GpuPowerW));

                if (parts.Count > 0)
                {
                    list.Add(new HudEntry
                    {
                        Label = "GPU",
                        Value = string.Join(" · ", parts.ToArray()),
                        Accent = ColEmerald,
                        ValueColor = c.HudShowGpuTemp && s.GpuTempC > 0f ? TempColor(s.GpuTempC) : ColText
                    });
                }
            }

            if (c.HudShowRam && s.RamTotalGb > 0f)
            {
                list.Add(new HudEntry
                {
                    Label = "RAM",
                    Value = Loc.F("{0:0.0}/{1:0}GB ({2:0}%)", s.RamUsedGb, s.RamTotalGb, s.RamLoadPct),
                    Accent = ColViolet,
                    ValueColor = s.RamLoadPct >= 88f ? ColAmber : ColText
                });
            }

            if (c.HudShowCaseFans)
            {
                string fVal = s.PrimaryCaseFanRpm > 0f
                    ? Loc.F("{0:0} RPM", s.PrimaryCaseFanRpm)
                    : (s.GpuFanRpm > 0f ? Loc.F("{0:0} RPM", s.GpuFanRpm) : "-- RPM");
                list.Add(new HudEntry { Label = "FAN", Value = fVal, Accent = ColCyan, ValueColor = ColText });
            }

            if (c.HudShowBoardDiskTemp)
            {
                List<string> pts = new List<string>();
                if (s.BoardTempC > 0f) pts.Add(Loc.F("MB {0:0}°C", s.BoardTempC));
                if (s.StorageTempC > 0f) pts.Add(Loc.F("SSD {0:0}°C", s.StorageTempC));
                if (pts.Count == 0) pts.Add("--°C");
                list.Add(new HudEntry { Label = "SYS", Value = string.Join(" · ", pts.ToArray()), Accent = ColAmber, ValueColor = ColText });
            }

            if (c.HudShowNetClock)
            {
                list.Add(new HudEntry
                {
                    Label = "NET",
                    Value = Loc.F("↓{0:0.0} ↑{1:0.0} · {2}", s.NetDownMbps, s.NetUpMbps, DateTime.Now.ToString("HH:mm")),
                    Accent = ColCyan,
                    ValueColor = ColText
                });
            }

            if (list.Count == 0)
                list.Add(new HudEntry { Label = "COREPULSE", Value = Loc.F("CPU {0:0}%", s.CpuLoadPct), Accent = ColCyan, ValueColor = ColText });

            return list;
        }

        public static Bitmap RenderHudBitmap(CorePulseSettings c, TelemetrySnapshot s, float dpiScale)
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

                    int bgAlpha = Math.Max(130, Math.Min(245, (int)Math.Round((c.OverlayOpacity / 100.0) * 238.0)));
                    int bdAlpha = Math.Max(160, Math.Min(255, bgAlpha + 20));
                    RectangleF rc = new RectangleF(1f, 1f, w - 2.5f, h - 2.5f);
                    float rad = (c.OverlayHorizontal ? 9f : 10f) * sf;

                    using (GraphicsPath gp = Rounded(rc, rad))
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(bgAlpha, ColHudBgBase)))
                    using (Pen bd = new Pen(Color.FromArgb(bdAlpha, ColHudBorder), 1.2f))
                    {
                        g.FillPath(bg, gp);
                        g.DrawPath(bd, gp);
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
