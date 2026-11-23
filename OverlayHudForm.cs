using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CorePulse
{
    // True Click-Through, Zero-Focus-Stealing, Always-On-Top Gaming HUD Overlay
    internal sealed class OverlayHudForm : Form
    {
        private sealed class HudEntry
        {
            public string Label;
            public string Value;
            public Color Accent;
            public Color ValueColor;
        }

        private static readonly Color ColHudBg = Color.FromArgb(14, 18, 25);
        private static readonly Color ColHudBorder = Color.FromArgb(52, 66, 86);
        private static readonly Color ColSep = Color.FromArgb(40, 50, 66);
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
        private readonly List<HudEntry> _entries = new List<HudEntry>();

        private Font _fLabel;
        private Font _fVal;

        public OverlayHudForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(1, 2, 3);
            TransparencyKey = Color.FromArgb(1, 2, 3);
            DoubleBuffered = true;

            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint,
                true);

            RebuildFonts();
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

        private float ScaleFactor()
        {
            float sizeMult = _cfg.OverlayScale == 0 ? 0.86f : (_cfg.OverlayScale == 2 ? 1.18f : 1.0f);
            return _dpiScale * sizeMult;
        }

        private float Sf(float v) { return v * ScaleFactor(); }
        private float PtToPx(float pt) { return pt * (96f / 72f) * ScaleFactor(); }

        private void RebuildFonts()
        {
            DisposeFonts();
            string fam = Loc.FontFamily(_cfg != null ? _cfg.Language : "en");
            _fLabel = new Font(fam, PtToPx(7.4f), FontStyle.Bold, GraphicsUnit.Pixel);
            _fVal = new Font("Segoe UI", PtToPx(9.4f), FontStyle.Bold, GraphicsUnit.Pixel);
        }

        private void DisposeFonts()
        {
            if (_fLabel != null) { _fLabel.Dispose(); _fLabel = null; }
            if (_fVal != null) { _fVal.Dispose(); _fVal = null; }
        }

        public static Color TempColor(float c)
        {
            if (c < 0f) return ColLabel;
            if (c >= 82f) return ColCoral;
            if (c >= 68f) return ColAmber;
            return ColEmerald;
        }

        public void UpdateHud(CorePulseSettings cfg, TelemetrySnapshot snap, float dpiScale)
        {
            bool fontDirty = (_cfg == null || _cfg.OverlayScale != cfg.OverlayScale || _cfg.Language != cfg.Language || Math.Abs(_dpiScale - dpiScale) > 0.01f);
            _cfg = cfg;
            _snap = snap ?? new TelemetrySnapshot();
            _dpiScale = dpiScale > 0.5f ? dpiScale : 1.0f;
            if (fontDirty) RebuildFonts();

            Opacity = Math.Max(0.55, Math.Min(1.0, cfg.OverlayOpacity / 100.0));
            BuildEntries();
            ResizeAndPosition();
            EnsureTopMost();
            Invalidate();
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

        private void BuildEntries()
        {
            _entries.Clear();
            TelemetrySnapshot s = _snap;
            CorePulseSettings c = _cfg;

            if (c.HudShowFps)
            {
                string val = s.Fps > 0.5f
                    ? string.Format("{0:0}  ({1:0.0}ms)", s.Fps, s.FrametimeMs)
                    : (s.FpsEtwActive ? "--" : "ADMIN");
                Color vc = s.Fps >= 60f ? ColEmerald : (s.Fps >= 30f ? ColAmber : ColLabel);
                _entries.Add(new HudEntry { Label = "FPS", Value = val, Accent = ColEmerald, ValueColor = vc });
            }

            if (c.HudShowFps1Low && s.Fps > 0.5f)
            {
                _entries.Add(new HudEntry { Label = "1% LOW", Value = string.Format("{0:0}", s.Fps1Low), Accent = ColEmerald, ValueColor = s.Fps1Low >= 45f ? ColText : ColAmber });
            }

            if (c.HudShowCpuTemp || c.HudShowCpuLoad || c.HudShowCpuClockPower)
            {
                List<string> parts = new List<string>();
                if (c.HudShowCpuTemp) parts.Add(s.CpuTempC > 0f ? string.Format("{0:0}°C", s.CpuTempC) : "--°C");
                if (c.HudShowCpuLoad) parts.Add(string.Format("{0:0}%", s.CpuLoadPct));
                if (c.HudShowCpuClockPower)
                {
                    if (s.CpuClockMhz > 100f) parts.Add(string.Format("{0:0.0}GHz", s.CpuClockMhz / 1000f));
                    if (s.CpuPowerW > 1f) parts.Add(string.Format("{0:0}W", s.CpuPowerW));
                }
                _entries.Add(new HudEntry
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
                if (c.HudShowGpuTemp) parts.Add(s.GpuTempC > 0f ? string.Format("{0:0}°C", s.GpuTempC) : "--°C");
                if (c.HudShowGpuLoad && s.GpuLoadPct >= 0f) parts.Add(string.Format("{0:0}%", s.GpuLoadPct));
                if (c.HudShowGpuVram && s.GpuVramUsedGb >= 0f) parts.Add(string.Format("{0:0.0}GB", s.GpuVramUsedGb));
                if (c.HudShowGpuFan)
                {
                    if (s.GpuFanRpm > 0f) parts.Add(string.Format("{0:0} RPM", s.GpuFanRpm));
                    else if (s.GpuFanPct >= 0f) parts.Add(string.Format("FAN {0:0}%", s.GpuFanPct));
                }
                if (c.HudShowGpuPower && s.GpuPowerW > 1f) parts.Add(string.Format("{0:0}W", s.GpuPowerW));

                if (parts.Count > 0)
                {
                    _entries.Add(new HudEntry
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
                _entries.Add(new HudEntry
                {
                    Label = "RAM",
                    Value = string.Format("{0:0.0}/{1:0}GB ({2:0}%)", s.RamUsedGb, s.RamTotalGb, s.RamLoadPct),
                    Accent = ColViolet,
                    ValueColor = s.RamLoadPct >= 88f ? ColAmber : ColText
                });
            }

            if (c.HudShowCaseFans)
            {
                string fVal = s.PrimaryCaseFanRpm > 0f
                    ? string.Format("{0:0} RPM", s.PrimaryCaseFanRpm)
                    : (s.GpuFanRpm > 0f ? string.Format("{0:0} RPM", s.GpuFanRpm) : "-- RPM");
                _entries.Add(new HudEntry { Label = "FAN", Value = fVal, Accent = ColCyan, ValueColor = ColText });
            }

            if (c.HudShowBoardDiskTemp)
            {
                List<string> pts = new List<string>();
                if (s.BoardTempC > 0f) pts.Add(string.Format("MB {0:0}°C", s.BoardTempC));
                if (s.StorageTempC > 0f) pts.Add(string.Format("SSD {0:0}°C", s.StorageTempC));
                if (pts.Count == 0) pts.Add("--°C");
                _entries.Add(new HudEntry { Label = "SYS", Value = string.Join(" · ", pts.ToArray()), Accent = ColAmber, ValueColor = ColText });
            }

            if (c.HudShowNetClock)
            {
                _entries.Add(new HudEntry
                {
                    Label = "NET",
                    Value = string.Format("↓{0:0.0} ↑{1:0.0} · {2}", s.NetDownMbps, s.NetUpMbps, DateTime.Now.ToString("HH:mm")),
                    Accent = ColCyan,
                    ValueColor = ColText
                });
            }

            // Guarantee at least 1 indicator if user unchecked everything
            if (_entries.Count == 0)
            {
                _entries.Add(new HudEntry { Label = "COREPULSE", Value = string.Format("CPU {0:0}%", s.CpuLoadPct), Accent = ColCyan, ValueColor = ColText });
            }
        }

        private void ResizeAndPosition()
        {
            int w, h;
            using (Bitmap tmp = new Bitmap(16, 16))
            using (Graphics g = Graphics.FromImage(tmp))
            {
                if (_cfg.OverlayHorizontal)
                {
                    float totalW = Sf(18f);
                    for (int i = 0; i < _entries.Count; i++)
                    {
                        float lw = MeasureTight(g, _entries[i].Label, _fLabel).Width;
                        float vw = MeasureTight(g, _entries[i].Value, _fVal).Width;
                        totalW += Sf(6f) + lw + Sf(6f) + vw + Sf(16f);
                    }
                    w = Math.Max((int)Math.Ceiling(Sf(130f)), (int)Math.Ceiling(totalW));
                    h = (int)Math.Ceiling(Sf(32f));
                }
                else
                {
                    float maxLbl = 0f, maxVal = 0f;
                    for (int i = 0; i < _entries.Count; i++)
                    {
                        maxLbl = Math.Max(maxLbl, MeasureTight(g, _entries[i].Label, _fLabel).Width);
                        maxVal = Math.Max(maxVal, MeasureTight(g, _entries[i].Value, _fVal).Width);
                    }
                    w = (int)Math.Ceiling(Sf(30f) + maxLbl + Sf(12f) + maxVal + Sf(14f));
                    h = (int)Math.Ceiling(Sf(14f) + _entries.Count * Sf(22f));
                }
            }

            Rectangle screen = Screen.PrimaryScreen != null ? Screen.PrimaryScreen.Bounds : new Rectangle(0, 0, 1920, 1080);
            int margin = (int)Math.Round(_cfg.OverlayMargin * _dpiScale);
            int x = screen.Left + margin;
            int y = screen.Top + margin;

            if (_cfg.OverlayPosition == OverlayAnchor.TopCenter || _cfg.OverlayPosition == OverlayAnchor.BottomCenter)
                x = screen.Left + (screen.Width - w) / 2;
            else if (_cfg.OverlayPosition == OverlayAnchor.TopRight || _cfg.OverlayPosition == OverlayAnchor.BottomRight)
                x = screen.Right - w - margin;

            if (_cfg.OverlayPosition == OverlayAnchor.BottomLeft || _cfg.OverlayPosition == OverlayAnchor.BottomCenter || _cfg.OverlayPosition == OverlayAnchor.BottomRight)
                y = screen.Bottom - h - margin - (int)Math.Round(42f * _dpiScale); // clear Windows taskbar

            SetBounds(x, y, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(TransparencyKey);

            RectangleF rc = new RectangleF(0.5f, 0.5f, ClientSize.Width - 1.5f, ClientSize.Height - 1.5f);
            float rad = _cfg.OverlayHorizontal ? Sf(9f) : Sf(10f);
            using (GraphicsPath gp = Rounded(rc, rad))
            using (SolidBrush bg = new SolidBrush(ColHudBg))
            using (Pen bd = new Pen(ColHudBorder, 1.2f))
            {
                g.FillPath(bg, gp);
                g.DrawPath(bd, gp);
            }

            if (_cfg.OverlayHorizontal)
            {
                float x = Sf(10f);
                for (int i = 0; i < _entries.Count; i++)
                {
                    HudEntry it = _entries[i];
                    if (i > 0)
                    {
                        using (Pen sep = new Pen(ColSep, 1f))
                            g.DrawLine(sep, x - Sf(7f), Sf(7f), x - Sf(7f), ClientSize.Height - Sf(7f));
                    }

                    using (SolidBrush ab = new SolidBrush(it.Accent))
                        g.FillRectangle(ab, x, (ClientSize.Height - Sf(12f)) * 0.5f, Sf(2.5f), Sf(12f));
                    x += Sf(6f);

                    float lw = MeasureTight(g, it.Label, _fLabel).Width + Sf(2f);
                    DrawTextTight(g, it.Label, _fLabel, ColLabel, new RectangleF(x, 0, lw, ClientSize.Height));
                    x += lw + Sf(5f);

                    float vw = MeasureTight(g, it.Value, _fVal).Width + Sf(2f);
                    DrawTextTight(g, it.Value, _fVal, it.ValueColor, new RectangleF(x, 0, vw, ClientSize.Height));
                    x += vw + Sf(15f);
                }
            }
            else
            {
                float maxLbl = 0f;
                for (int i = 0; i < _entries.Count; i++)
                    maxLbl = Math.Max(maxLbl, MeasureTight(g, _entries[i].Label, _fLabel).Width);

                float y = Sf(7f);
                float rowH = Sf(22f);
                for (int i = 0; i < _entries.Count; i++)
                {
                    HudEntry it = _entries[i];
                    using (SolidBrush ab = new SolidBrush(it.Accent))
                        g.FillRectangle(ab, Sf(10f), y + Sf(4f), Sf(2.5f), rowH - Sf(8f));

                    DrawTextTight(g, it.Label, _fLabel, ColLabel, new RectangleF(Sf(17f), y, maxLbl + Sf(4f), rowH));
                    DrawTextTight(g, it.Value, _fVal, it.ValueColor, new RectangleF(Sf(17f) + maxLbl + Sf(10f), y, ClientSize.Width - (Sf(28f) + maxLbl), rowH));
                    y += rowH;
                }
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) DisposeFonts();
            base.Dispose(disposing);
        }
    }
}
