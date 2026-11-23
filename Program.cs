using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("CorePulse — Hardware Monitor, Fan Controller & Gaming HUD")]
[assembly: AssemblyDescription("Lightweight native Windows 10/11 hardware telemetry, PWM fan curve controller with thermal safety override, zero-injection DXGI/D3D9 ETW FPS counter, System Tray support, and 32-bit per-pixel alpha Always-On-Top Gaming HUD (EN/TR/zh-CN).")]
[assembly: AssemblyProduct("CorePulse")]
[assembly: AssemblyVersion("1.2.0.0")]
[assembly: AssemblyFileVersion("1.2.0.0")]

namespace CorePulse
{
    internal sealed class LaunchConfig
    {
        public bool DemoMode;
        public string ForceLang;
        public int InitialTab;
        public bool ShowDemoOverlay;
        public bool StartMinimized;
        public bool Handoff;

        public static LaunchConfig Parse(string[] args)
        {
            LaunchConfig c = new LaunchConfig();
            if (args == null) return c;
            for (int i = 0; i < args.Length; i++)
            {
                string a = (args[i] ?? "").Trim().ToLowerInvariant();
                if (a == "--demo") c.DemoMode = true;
                else if (a == "--hud") c.ShowDemoOverlay = true;
                else if (a == "--minimized" || a == "--tray") c.StartMinimized = true;
                else if (a == "--handoff") c.Handoff = true;
                else if (a == "--lang" && i + 1 < args.Length)
                {
                    string l = (args[++i] ?? "").Trim().ToLowerInvariant();
                    if (l == "tr" || l == "en" || l == "zh") c.ForceLang = l;
                }
                else if (a == "--tab" && i + 1 < args.Length)
                {
                    int t;
                    if (int.TryParse(args[++i], out t) && t >= 0 && t <= 3) c.InitialTab = t;
                }
            }
            return c;
        }
    }

    internal sealed class HitZone
    {
        public RectangleF Rect;
        public string Key;
        public object Payload;

        public HitZone(RectangleF r, string key, object payload)
        {
            Rect = r; Key = key; Payload = payload;
        }
    }

    internal sealed class PulseContentPanel : Control
    {
        public static readonly Color ColBg = Color.FromArgb(11, 15, 22);
        public static readonly Color ColCard = Color.FromArgb(19, 24, 33);
        public static readonly Color ColCardHover = Color.FromArgb(26, 33, 44);
        public static readonly Color ColBorder = Color.FromArgb(38, 48, 63);
        public static readonly Color ColBorderHover = Color.FromArgb(66, 82, 106);
        public static readonly Color ColTextPrimary = Color.FromArgb(241, 245, 249);
        public static readonly Color ColTextBody = Color.FromArgb(203, 213, 225);
        public static readonly Color ColTextSec = Color.FromArgb(148, 163, 184);
        public static readonly Color ColTextMuted = Color.FromArgb(100, 116, 139);
        public static readonly Color ColCyan = Color.FromArgb(56, 189, 248);
        public static readonly Color ColCyanBg = Color.FromArgb(16, 40, 58);
        public static readonly Color ColCyanBd = Color.FromArgb(40, 120, 166);
        public static readonly Color ColEmerald = Color.FromArgb(52, 211, 153);
        public static readonly Color ColEmeraldBg = Color.FromArgb(16, 46, 38);
        public static readonly Color ColEmeraldBd = Color.FromArgb(36, 128, 96);
        public static readonly Color ColAmber = Color.FromArgb(251, 191, 36);
        public static readonly Color ColAmberBg = Color.FromArgb(48, 36, 16);
        public static readonly Color ColAmberBd = Color.FromArgb(152, 104, 24);
        public static readonly Color ColCoral = Color.FromArgb(248, 113, 113);
        public static readonly Color ColCoralBg = Color.FromArgb(52, 22, 26);
        public static readonly Color ColCoralBd = Color.FromArgb(164, 52, 60);
        public static readonly Color ColViolet = Color.FromArgb(167, 139, 250);
        public static readonly Color ColBtnBg = Color.FromArgb(28, 35, 47);
        public static readonly Color ColBtnHover = Color.FromArgb(40, 50, 66);
        public static readonly Color ColBtnBd = Color.FromArgb(56, 68, 86);
        public static readonly Color ColPrimaryBg = Color.FromArgb(16, 124, 88);
        public static readonly Color ColPrimaryHov = Color.FromArgb(20, 148, 106);

        private float _scale = 1.0f;
        private string _lang = "en";
        private int _activeTab;
        private int _scrollY;
        private int _contentH;
        private string _hoverKey = "";
        private object _hoverPayload;
        private bool _hoverScroll;
        private bool _dragScroll;
        private int _dragStartY;
        private int _dragStartScroll;

        private readonly List<HitZone> _zones = new List<HitZone>();

        public CorePulseSettings Settings = new CorePulseSettings();
        public HardwareEngine Engine;
        public TelemetrySnapshot Snap = new TelemetrySnapshot();
        public string SensorFilter = "ALL";
        public bool DemoMode;
        public bool HudDragUnlocked;

        public event Action<string, object> ActionInvoked;

        private Font _fSec;
        private Font _fTitle;
        private Font _fSub;
        private Font _fSmall;
        private Font _fBadge;
        private Font _fBtn;
        private Font _fHero;
        private Font _fMetric;

        public PulseContentPanel()
        {
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);
            BackColor = ColBg;
            RebuildFonts();
        }

        public void Configure(float scale, string lang, int tab)
        {
            bool changed = (_activeTab != tab);
            _scale = scale;
            _lang = lang ?? "en";
            _activeTab = tab;
            if (changed) _scrollY = 0;
            RebuildFonts();
            Invalidate();
        }

        private float Sf(float v) { return v * _scale; }
        private float PtToPx(float pt) { return pt * (96f / 72f) * _scale; }
        private int ScrollW() { return (int)Math.Round(10f * _scale); }
        private string T(string en, string tr, string zh) { return Loc.T(_lang, en, tr, zh); }

        private void RebuildFonts()
        {
            DisposeFonts();
            string fam = Loc.FontFamily(_lang);
            _fSec = new Font(fam, PtToPx(8.4f), FontStyle.Bold, GraphicsUnit.Pixel);
            _fTitle = new Font(fam, PtToPx(9.3f), FontStyle.Bold, GraphicsUnit.Pixel);
            _fSub = new Font(fam, PtToPx(8.0f), FontStyle.Regular, GraphicsUnit.Pixel);
            _fSmall = new Font("Segoe UI", PtToPx(7.6f), FontStyle.Regular, GraphicsUnit.Pixel);
            _fBadge = new Font(fam, PtToPx(6.9f), FontStyle.Bold, GraphicsUnit.Pixel);
            _fBtn = new Font(fam, PtToPx(8.2f), FontStyle.Bold, GraphicsUnit.Pixel);
            _fHero = new Font("Segoe UI", PtToPx(22.0f), FontStyle.Bold, GraphicsUnit.Pixel);
            _fMetric = new Font("Segoe UI", PtToPx(10.8f), FontStyle.Bold, GraphicsUnit.Pixel);
        }

        private void DisposeFonts()
        {
            if (_fSec != null) { _fSec.Dispose(); _fSec = null; }
            if (_fTitle != null) { _fTitle.Dispose(); _fTitle = null; }
            if (_fSub != null) { _fSub.Dispose(); _fSub = null; }
            if (_fSmall != null) { _fSmall.Dispose(); _fSmall = null; }
            if (_fBadge != null) { _fBadge.Dispose(); _fBadge = null; }
            if (_fBtn != null) { _fBtn.Dispose(); _fBtn = null; }
            if (_fHero != null) { _fHero.Dispose(); _fHero = null; }
            if (_fMetric != null) { _fMetric.Dispose(); _fMetric = null; }
        }

        private int MaxScroll() { return Math.Max(0, _contentH - ClientSize.Height); }
        private void ClampScroll() { int m = MaxScroll(); if (_scrollY > m) _scrollY = m; if (_scrollY < 0) _scrollY = 0; }

        private RectangleF GetThumb()
        {
            if (_contentH <= ClientSize.Height || ClientSize.Height <= 0) return RectangleF.Empty;
            float tx = ClientSize.Width - ScrollW();
            float th = Math.Max(Sf(32f), ClientSize.Height * ((float)ClientSize.Height / _contentH));
            float ratio = (float)_scrollY / Math.Max(1, MaxScroll());
            return new RectangleF(tx + Sf(2f), ratio * (ClientSize.Height - th), ScrollW() - Sf(4f), th);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); ClampScroll(); Invalidate(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (MaxScroll() <= 0) return;
            _scrollY -= (e.Delta / 120) * (int)Math.Round(52f * _scale);
            ClampScroll();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragScroll)
            {
                RectangleF tb = GetThumb();
                float avail = Math.Max(1f, ClientSize.Height - tb.Height);
                _scrollY = _dragStartScroll + (int)Math.Round(((e.Y - _dragStartY) / avail) * MaxScroll());
                ClampScroll();
                Invalidate();
                return;
            }
            RectangleF tr = GetThumb();
            bool onSc = !tr.IsEmpty && e.X >= ClientSize.Width - ScrollW() - Sf(2f);
            string nk = ""; object np = null;
            if (!onSc)
            {
                for (int i = _zones.Count - 1; i >= 0; i--)
                {
                    if (_zones[i].Rect.Contains(e.Location)) { nk = _zones[i].Key; np = _zones[i].Payload; break; }
                }
            }
            if (nk != _hoverKey || !object.Equals(np, _hoverPayload) || onSc != _hoverScroll)
            {
                _hoverKey = nk; _hoverPayload = np; _hoverScroll = onSc;
                Cursor = (!string.IsNullOrEmpty(nk) || onSc) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!string.IsNullOrEmpty(_hoverKey) || _hoverScroll)
            {
                _hoverKey = ""; _hoverPayload = null; _hoverScroll = false; Cursor = Cursors.Default; Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            RectangleF tr = GetThumb();
            if (!tr.IsEmpty && e.X >= ClientSize.Width - ScrollW() - Sf(2f))
            {
                _dragScroll = true; _dragStartY = e.Y; _dragStartScroll = _scrollY; Capture = true; return;
            }
            for (int i = _zones.Count - 1; i >= 0; i--)
            {
                if (_zones[i].Rect.Contains(e.Location))
                {
                    if (ActionInvoked != null) ActionInvoked(_zones[i].Key, _zones[i].Payload);
                    return;
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragScroll) { _dragScroll = false; Capture = false; }
        }

        private bool IsHov(string key, object payload) { return _hoverKey == key && object.Equals(_hoverPayload, payload); }
        private void AddZone(RectangleF r, string key, object payload) { if (r.Bottom >= 0 && r.Top <= ClientSize.Height) _zones.Add(new HitZone(r, key, payload)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(ColBg);
            _zones.Clear();

            float availW = ClientSize.Width - ScrollW() - Sf(8f);
            if (availW < Sf(200f)) availW = ClientSize.Width;

            if (_activeTab == 0) _contentH = PaintDashboard(g, availW);
            else if (_activeTab == 1) _contentH = PaintSensors(g, availW);
            else if (_activeTab == 2) _contentH = PaintFanControl(g, availW);
            else _contentH = PaintOverlayTab(g, availW);

            if (_scrollY > MaxScroll()) _scrollY = MaxScroll();
            if (_contentH > ClientSize.Height)
            {
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(16, 21, 29)))
                    g.FillRectangle(tb, ClientSize.Width - ScrollW(), 0, ScrollW(), ClientSize.Height);
                RectangleF thumb = GetThumb();
                using (GraphicsPath gp = Rounded(thumb, Sf(3f)))
                using (SolidBrush sb = new SolidBrush((_hoverScroll || _dragScroll) ? Color.FromArgb(84, 102, 126) : Color.FromArgb(54, 66, 84)))
                    g.FillPath(sb, gp);
            }
        }

        // ==================== TAB 0: DASHBOARD ====================
        private int PaintDashboard(Graphics g, float availW)
        {
            float y = -_scrollY;
            TelemetrySnapshot s = Snap ?? new TelemetrySnapshot();

            // Helpful banner if lib\ folder is missing, or Admin / PawnIO is needed for full CPU/Board/Fan sensors
            if (!s.LhmLoaded || !s.IsAdmin || !s.PawnIoInstalled)
            {
                RectangleF ban = new RectangleF(0, y, availW, Sf(40f));
                DrawBox(g, ban, Sf(9f), ColAmberBg, ColAmberBd);
                string msg = !s.LhmLoaded
                    ? T("Native Fallback Mode — keep the 'lib\\' folder next to CorePulse.exe to enable full CPU/GPU/Motherboard & Fan sensors.",
                        "Yerli Yedek Mod — tam CPU/GPU/Anakart ve Fan sensörleri için 'lib\\' klasörünü CorePulse.exe ile aynı klasörde tutun.",
                        "原生降级模式 — 请将 lib\\ 文件夹与 CorePulse.exe 放在同一目录，以启用完整 CPU/主板/风扇传感器。")
                    : (!s.PawnIoInstalled
                        ? T("Signed PawnIO driver not detected — install official PawnIO + Run as Admin to unlock CPU/Motherboard temps & PWM Fan Control.",
                            "İmzalı PawnIO sürücüsü bulunamadı — CPU/Anakart ısısı ve Fan Kontrolü için resmi PawnIO kurup Yönetici olarak açın.",
                            "未检测到签名 PawnIO 驱动 — 安装官方 PawnIO 并以管理员运行即可解锁 CPU/主板温度与 PWM 风扇控制。")
                        : T("Standard Mode — GPU & RAM active. Click 'Admin ↗' to unlock CPU Package Temp, Motherboard VRM/Fans & Zero-Injection ETW FPS.",
                            "Standart Mod — GPU ve RAM aktif. CPU Isısı, Anakart Fan Kontrolü ve ETW FPS için 'Yönetici ↗' tıklayın.",
                            "标准模式 — 已读取 GPU 与内存；点击「管理员 ↗」即可解锁 CPU/主板温度、风扇控制与无注入 ETW FPS。"));
                DrawText(g, msg, _fSub, ColAmber, new RectangleF(ban.X + Sf(12f), ban.Y, ban.Width - Sf(250f), ban.Height), StringAlignment.Near);

                float bx = ban.Right - Sf(116f);
                if (!s.IsAdmin)
                {
                    RectangleF bAdm = new RectangleF(bx, ban.Y + Sf(7f), Sf(106f), Sf(26f));
                    DrawPillBtn(g, bAdm, T("Admin ↗", "Yönetici ↗", "管理员 ↗"), IsHov("elevate", null), true, true);
                    AddZone(bAdm, "elevate", null);
                    bx -= Sf(124f);
                }
                if (s.LhmLoaded && !s.PawnIoInstalled)
                {
                    RectangleF bPwn = new RectangleF(bx, ban.Y + Sf(7f), Sf(118f), Sf(26f));
                    DrawPillBtn(g, bPwn, T("Get PawnIO ↗", "PawnIO İndir ↗", "下载 PawnIO ↗"), IsHov("open_pawnio", null), false, true);
                    AddZone(bPwn, "open_pawnio", null);
                }
                y += Sf(48f);
            }

            // 2x2 Hero Telemetry Cards
            float gap = Sf(10f);
            float cw = (availW - gap) * 0.5f;
            float ch = Sf(124f);

            // Card 1: CPU
            DrawHeroCard(g, new RectangleF(0, y, cw, ch), ColCyan,
                T("PROCESSOR (CPU)", "İŞLEMCİ (CPU)", "处理器 (CPU)"),
                s.CpuName,
                s.CpuTempC > 0f ? Loc.F("{0:0.0}°C", s.CpuTempC) : T("Admin", "Yönetici", "需管理员"),
                s.CpuTempC > 0f ? OverlayHudForm.TempColor(s.CpuTempC) : ColAmber,
                s.CpuLoadPct,
                Loc.F(T("Load {0:0}%  ·  Clock {1}  ·  Power {2}", "Yük %{0:0}  ·  Saat {1}  ·  Güç {2}", "负载 {0:0}%  ·  频率 {1}  ·  功耗 {2}"),
                    s.CpuLoadPct,
                    s.CpuClockMhz > 100f ? Loc.F("{0:0.00} GHz", s.CpuClockMhz / 1000f) : "--",
                    s.CpuPowerW > 1f ? Loc.F("{0:0.0} W", s.CpuPowerW) : "--"));

            // Card 2: GPU
            DrawHeroCard(g, new RectangleF(cw + gap, y, cw, ch), ColEmerald,
                T("GRAPHICS (GPU)", "EKRAN KARTI (GPU)", "显卡 (GPU)"),
                s.GpuName,
                s.GpuTempC > 0f ? Loc.F("{0:0.0}°C", s.GpuTempC) : "--°C",
                OverlayHudForm.TempColor(s.GpuTempC),
                s.GpuLoadPct >= 0f ? s.GpuLoadPct : 0f,
                Loc.F(T("Load {0:0}%  ·  VRAM {1}  ·  Fan {2}  ·  {3}", "Yük %{0:0}  ·  VRAM {1}  ·  Fan {2}  ·  {3}", "负载 {0:0}%  ·  显存 {1}  ·  风扇 {2}  ·  {3}"),
                    Math.Max(0f, s.GpuLoadPct),
                    s.GpuVramUsedGb >= 0f ? Loc.F("{0:0.0}/{1:0}G", s.GpuVramUsedGb, s.GpuVramTotalGb) : (s.GpuVramTotalGb > 0f ? Loc.F("{0:0}G", s.GpuVramTotalGb) : "--"),
                    s.GpuFanRpm > 0f ? Loc.F("{0:0} RPM", s.GpuFanRpm) : (s.GpuFanPct >= 0f ? Loc.F("{0:0}%", s.GpuFanPct) : "--"),
                    s.GpuPowerW > 1f ? Loc.F("{0:0} W", s.GpuPowerW) : "--"));

            y += ch + gap;

            // Card 3: RAM & Motherboard / Storage
            DrawHeroCard(g, new RectangleF(0, y, cw, ch), ColViolet,
                T("MEMORY, BOARD & SSD", "BELLEK, ANAKART VE DİSK", "内存、主板与硬盘"),
                !string.IsNullOrEmpty(s.RamSpecText) ? s.RamSpecText : Loc.F("{0:0} GB RAM", s.RamTotalGb),
                Loc.F("{0:0}%", s.RamLoadPct),
                s.RamLoadPct >= 88f ? ColAmber : ColTextPrimary,
                s.RamLoadPct,
                Loc.F(T("Used {0:0.0}/{1:0} GB  ·  Board {2}  ·  SSD {3}", "Kullanım {0:0.0}/{1:0} GB  ·  Anakart {2}  ·  SSD {3}", "已用 {0:0.0}/{1:0} GB  ·  主板 {2}  ·  硬盘 {3}"),
                    s.RamUsedGb, s.RamTotalGb,
                    s.BoardTempC > 0f ? Loc.F("{0:0}°C", s.BoardTempC) : "--",
                    s.StorageTempC > 0f ? Loc.F("{0:0}°C", s.StorageTempC) : "--"));

            // Card 4: Live Gaming FPS (ETW) & Power / Net
            string fpsHero = s.Fps > 0.5f
                ? Loc.F("{0:0} FPS", s.Fps)
                : (s.FpsEtwActive ? T("Idle", "Beklemede", "待机") : T("Admin", "Yönetici", "需管理员"));
            Color fpsCol = s.Fps >= 60f ? ColEmerald : (s.Fps > 0.5f ? ColAmber : ColTextSec);
            string fpsSubTitle = !string.IsNullOrEmpty(s.ActiveGameProcess)
                ? ("DXGI / D3D9 ETW · " + s.ActiveGameProcess)
                : T("Zero-Injection Anti-Cheat Safe DXGI / D3D9 ETW", "Anti-Cheat Güvenli Enjeksiyonsuz DXGI / D3D9 ETW", "零注入防封号 DXGI / D3D9 ETW 帧率监测");
            string fpsBottom = s.Fps > 0.5f
                ? Loc.F("1% Low {0:0}  ·  {1:0.0} ms  ·  {2} {3:0} W  ·  ↓{4:0.0} Mb", s.Fps1Low, s.FrametimeMs, s.PsuLabel, s.EstimatedTotalPowerW, s.NetDownMbps)
                : Loc.F("{0}: ~{1:0} W  ·  Net ↓{2:0.0} / ↑{3:0.0} Mbps", s.PsuLabel, Math.Max(0f, s.EstimatedTotalPowerW), s.NetDownMbps, s.NetUpMbps);
            DrawHeroCard(g, new RectangleF(cw + gap, y, cw, ch), ColAmber,
                T("GAMING FPS & SYSTEM POWER", "OYUN FPS VE SİSTEM GÜCÜ", "游戏帧率与系统功耗"),
                fpsSubTitle, fpsHero, fpsCol,
                s.Fps > 0.5f ? Math.Min(100f, (s.Fps / 240f) * 100f) : 0f,
                fpsBottom);

            y += ch + Sf(12f);

            // Real-Time Dual Thermal Sparkline Chart
            float chartH = Sf(158f);
            RectangleF chartCard = new RectangleF(0, y, availW, chartH);
            DrawBox(g, chartCard, Sf(10f), ColCard, ColBorder);
            DrawThermalChart(g, chartCard, s);
            y += chartH + Sf(10f);

            // Hardware Inventory Footer Strip
            RectangleF specCard = new RectangleF(0, y, availW, Sf(48f));
            DrawBox(g, specCard, Sf(9f), ColCard, ColBorder);
            string specLine = Loc.F(
                T("Motherboard: {0}   ·   Storage: {1}   ·   Fans Detected: {2}   ·   HUD Hotkey: Ctrl+Shift+O",
                  "Anakart: {0}   ·   Disk: {1}   ·   Algılanan Fan: {2}   ·   HUD Kısayol: Ctrl+Shift+O",
                  "主板：{0}   ·   硬盘：{1}   ·   已检测风扇：{2}   ·   悬浮窗热键：Ctrl+Shift+O"),
                s.MotherboardName,
                !string.IsNullOrEmpty(s.StorageName) ? s.StorageName : "NVMe / SATA",
                s.Fans != null ? s.Fans.Count : 0);
            DrawText(g, specLine, _fSub, ColTextSec, new RectangleF(specCard.X + Sf(14f), specCard.Y, specCard.Width - Sf(28f), specCard.Height), StringAlignment.Near);
            y += Sf(56f);

            return (int)Math.Ceiling(y + _scrollY + Sf(6f));
        }

        private void DrawHeroCard(Graphics g, RectangleF r, Color accent, string label, string subTitle, string bigVal, Color bigCol, float barPct, string footer)
        {
            DrawBox(g, r, Sf(10f), ColCard, ColBorder);
            using (SolidBrush ab = new SolidBrush(accent))
                g.FillRectangle(ab, r.X + Sf(12f), r.Y + Sf(12f), Sf(3f), Sf(14f));

            DrawText(g, label, _fSec, accent, new RectangleF(r.X + Sf(21f), r.Y + Sf(10f), r.Width - Sf(150f), Sf(18f)), StringAlignment.Near);
            DrawText(g, subTitle, _fSub, ColTextSec, new RectangleF(r.X + Sf(14f), r.Y + Sf(31f), r.Width - Sf(155f), Sf(17f)), StringAlignment.Near);

            // Big metric value on top-right
            DrawText(g, bigVal, _fHero, bigCol, new RectangleF(r.Right - Sf(154f), r.Y + Sf(10f), Sf(140f), Sf(42f)), StringAlignment.Far);

            // Sleek load progress bar
            RectangleF barTrack = new RectangleF(r.X + Sf(14f), r.Y + Sf(66f), r.Width - Sf(28f), Sf(7f));
            using (GraphicsPath tp = Rounded(barTrack, Sf(3.5f)))
            using (SolidBrush tb = new SolidBrush(Color.FromArgb(28, 36, 48)))
                g.FillPath(tb, tp);

            float fillW = Math.Max(barPct > 0.5f ? Sf(8f) : 0f, barTrack.Width * Math.Min(1f, Math.Max(0f, barPct / 100f)));
            if (fillW > 0f)
            {
                using (GraphicsPath fp = Rounded(new RectangleF(barTrack.X, barTrack.Y, fillW, barTrack.Height), Sf(3.5f)))
                using (SolidBrush fb = new SolidBrush(accent))
                    g.FillPath(fb, fp);
            }

            DrawText(g, footer, _fSmall, ColTextBody, new RectangleF(r.X + Sf(14f), r.Bottom - Sf(32f), r.Width - Sf(28f), Sf(20f)), StringAlignment.Near);
        }

        private void DrawThermalChart(Graphics g, RectangleF card, TelemetrySnapshot s)
        {
            DrawText(g, T("LIVE THERMAL TELEMETRY (60s HISTORY)", "CANLI SICAKLIK GRAFİĞİ (60 SN GEÇMİŞ)", "实时温度变化曲线（近 60 秒）"),
                _fSec, ColCyan, new RectangleF(card.X + Sf(14f), card.Y + Sf(10f), Sf(320f), Sf(18f)), StringAlignment.Near);

            // Legend on top right
            string legCpu = s.CpuTempC > 0f ? Loc.F("● CPU {0:0.0}°C", s.CpuTempC) : "● CPU --";
            string legGpu = s.GpuTempC > 0f ? Loc.F("● GPU {0:0.0}°C", s.GpuTempC) : "● GPU --";
            DrawText(g, legCpu, _fBtn, ColCyan, new RectangleF(card.Right - Sf(250f), card.Y + Sf(9f), Sf(116f), Sf(19f)), StringAlignment.Far);
            DrawText(g, legGpu, _fBtn, ColEmerald, new RectangleF(card.Right - Sf(126f), card.Y + Sf(9f), Sf(112f), Sf(19f)), StringAlignment.Far);

            RectangleF plot = new RectangleF(card.X + Sf(44f), card.Y + Sf(36f), card.Width - Sf(60f), card.Height - Sf(52f));
            using (Pen gp = new Pen(Color.FromArgb(30, 39, 52), 1f))
            {
                int[] ticks = new int[] { 20, 40, 60, 80, 100 };
                foreach (int t in ticks)
                {
                    float py = plot.Bottom - ((t - 20f) / 80f) * plot.Height;
                    g.DrawLine(gp, plot.X, py, plot.Right, py);
                    DrawText(g, t + "°", _fSmall, ColTextMuted, new RectangleF(card.X + Sf(6f), py - Sf(8f), Sf(34f), Sf(16f)), StringAlignment.Far);
                }
            }

            // Thermal safety line at 85C
            float warnY = plot.Bottom - ((85f - 20f) / 80f) * plot.Height;
            using (Pen wp = new Pen(Color.FromArgb(90, 248, 113, 113), 1f))
            {
                wp.DashStyle = DashStyle.Dash;
                g.DrawLine(wp, plot.X, warnY, plot.Right, warnY);
            }

            if (Engine == null) return;
            DrawSparkSeries(g, plot, Engine.HistCpuTemp, ColCyan);
            DrawSparkSeries(g, plot, Engine.HistGpuTemp, ColEmerald);
        }

        private static void DrawSparkSeries(Graphics g, RectangleF plot, float[] vals, Color col)
        {
            List<PointF> pts = new List<PointF>(60);
            for (int i = 0; i < vals.Length; i++)
            {
                if (vals[i] <= 1f) continue;
                float clamped = Math.Max(20f, Math.Min(100f, vals[i]));
                float px = plot.X + (i / 59f) * plot.Width;
                float py = plot.Bottom - ((clamped - 20f) / 80f) * plot.Height;
                pts.Add(new PointF(px, py));
            }
            if (pts.Count < 2) return;

            using (GraphicsPath area = new GraphicsPath())
            {
                area.AddLines(pts.ToArray());
                area.AddLine(pts[pts.Count - 1].X, plot.Bottom, pts[0].X, plot.Bottom);
                area.CloseFigure();
                using (LinearGradientBrush fill = new LinearGradientBrush(plot, Color.FromArgb(44, col), Color.FromArgb(2, col), 90f))
                    g.FillPath(fill, area);
            }
            using (Pen lp = new Pen(col, 2.0f))
            {
                lp.LineJoin = LineJoin.Round;
                g.DrawLines(lp, pts.ToArray());
            }
        }

        // ==================== TAB 1: ALL SENSORS TABLE ====================
        private int PaintSensors(Graphics g, float availW)
        {
            float y = -_scrollY;

            // Filter bar + Reset Min/Max
            string[] filters = new string[] { "ALL", "CPU", "GPU", "Motherboard", "Storage" };
            string[] flabels = new string[]
            {
                T("All", "Tümü", "全部"),
                "CPU", "GPU",
                T("Board / Fans", "Anakart / Fan", "主板/风扇"),
                T("Storage", "Disk", "硬盘")
            };
            float bx = 0f;
            float tbH = Sf(28f);
            for (int i = 0; i < filters.Length; i++)
            {
                float w = Sf(i == 3 ? 104f : 72f);
                RectangleF r = new RectangleF(bx, y, w, tbH);
                bool act = SensorFilter == filters[i];
                DrawPillBtn(g, r, flabels[i], IsHov("sensor_filter", filters[i]), act, true);
                AddZone(r, "sensor_filter", filters[i]);
                bx = r.Right + Sf(7f);
            }

            RectangleF rRes = new RectangleF(availW - Sf(134f), y, Sf(134f), tbH);
            DrawPillBtn(g, rRes, T("Reset Min / Max", "Min / Maks Sıfırla", "重置最小/最大"), IsHov("sensor_reset", null), false, true);
            AddZone(rRes, "sensor_reset", null);
            y += tbH + Sf(12f);

            // Table Header
            RectangleF hdr = new RectangleF(0, y, availW, Sf(28f));
            DrawBox(g, hdr, Sf(7f), Color.FromArgb(16, 21, 29), ColBorder);
            DrawText(g, T("SENSOR", "SENSÖR", "传感器名称"), _fSec, ColTextMuted, new RectangleF(Sf(12f), y, Sf(240f), Sf(28f)), StringAlignment.Near);
            DrawText(g, T("HARDWARE", "DONANIM", "所属硬件"), _fSec, ColTextMuted, new RectangleF(Sf(260f), y, Sf(230f), Sf(28f)), StringAlignment.Near);
            DrawText(g, T("CURRENT", "ANLIK", "当前值"), _fSec, ColTextMuted, new RectangleF(availW - Sf(275f), y, Sf(85f), Sf(28f)), StringAlignment.Far);
            DrawText(g, T("MIN", "MİN", "最小"), _fSec, ColTextMuted, new RectangleF(availW - Sf(185f), y, Sf(82f), Sf(28f)), StringAlignment.Far);
            DrawText(g, T("MAX", "MAKS", "最大"), _fSec, ColTextMuted, new RectangleF(availW - Sf(96f), y, Sf(82f), Sf(28f)), StringAlignment.Far);
            y += Sf(34f);

            List<SensorReading> rows = Snap != null ? Snap.AllSensors : new List<SensorReading>();
            int shown = 0;
            float rowH = Sf(32f);
            for (int i = 0; i < rows.Count; i++)
            {
                SensorReading sr = rows[i];
                if (SensorFilter != "ALL" && !string.Equals(sr.Group, SensorFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                shown++;
                RectangleF rr = new RectangleF(0, y, availW, rowH);
                if (rr.Bottom >= -4 && rr.Top <= ClientSize.Height + 4)
                {
                    DrawBox(g, rr, Sf(6f), (shown % 2 == 0) ? ColCard : Color.FromArgb(16, 21, 29), Color.FromArgb(30, 38, 50));
                    DrawText(g, sr.Name, _fTitle, ColTextPrimary, new RectangleF(Sf(12f), y, Sf(242f), rowH), StringAlignment.Near);
                    DrawText(g, sr.HardwareName, _fSub, ColTextSec, new RectangleF(Sf(260f), y, Sf(230f), rowH), StringAlignment.Near);

                    Color valCol = sr.TypeLabel == "Temp" ? OverlayHudForm.TempColor(sr.Value) : (sr.TypeLabel == "Fan" ? ColCyan : ColTextPrimary);
                    string fmt = sr.Unit == "RPM" || sr.Unit == "MHz" ? "{0:0} {1}" : "{0:0.0} {1}";
                    DrawText(g, Loc.F(fmt, sr.Value, sr.Unit), _fBtn, valCol, new RectangleF(availW - Sf(275f), y, Sf(85f), rowH), StringAlignment.Far);
                    DrawText(g, Loc.F(fmt, sr.Min, sr.Unit), _fSmall, ColTextSec, new RectangleF(availW - Sf(185f), y, Sf(82f), rowH), StringAlignment.Far);
                    DrawText(g, Loc.F(fmt, sr.Max, sr.Unit), _fSmall, ColTextBody, new RectangleF(availW - Sf(96f), y, Sf(82f), rowH), StringAlignment.Far);
                }
                y += rowH + Sf(4f);
            }

            if (shown == 0)
            {
                DrawText(g, T("No sensors in this filter. Run as Administrator with signed PawnIO for full Motherboard & CPU sensors.",
                    "Bu filtrede sensör bulunamadı. Tam CPU/Anakart sensörleri için Yönetici olarak çalıştırın.",
                    "当前分类暂无传感器数据，请以管理员身份运行并安装 PawnIO 以解锁完整传感器。"),
                    _fSub, ColTextSec, new RectangleF(Sf(10f), y + Sf(16f), availW - Sf(20f), Sf(26f)), StringAlignment.Near);
                y += Sf(60f);
            }

            return (int)Math.Ceiling(y + _scrollY + Sf(10f));
        }

        // ==================== TAB 2: FAN CONTROLLER & CURVE ====================
        private int PaintFanControl(Graphics g, float availW)
        {
            float y = -_scrollY;
            TelemetrySnapshot s = Snap ?? new TelemetrySnapshot();

            // Global Cooling Presets + Thermal Safety Shield
            float tbH = Sf(30f);
            float bx = 0f;
            FanProfileMode[] modes = new FanProfileMode[] { FanProfileMode.BiosAuto, FanProfileMode.Silent, FanProfileMode.Balanced, FanProfileMode.Turbo };
            string[] mLabels = new string[]
            {
                T("BIOS Auto", "BIOS Otomatik", "BIOS 自动"),
                T("Silent", "Sessiz", "静音模式"),
                T("Balanced", "Dengeli", "均衡模式"),
                T("Turbo", "Turbo", "强冷模式")
            };
            for (int i = 0; i < modes.Length; i++)
            {
                RectangleF mr = new RectangleF(bx, y, Sf(104f), tbH);
                bool act = Settings.FanProfile == modes[i];
                DrawPillBtn(g, mr, mLabels[i], IsHov("fan_profile", modes[i]), act, true);
                AddZone(mr, "fan_profile", modes[i]);
                bx = mr.Right + Sf(8f);
            }

            // Thermal Safety Shield badge on far right
            RectangleF safeBadge = new RectangleF(availW - Sf(254f), y, Sf(254f), tbH);
            DrawBox(g, safeBadge, Sf(7f), s.ThermalSafetyOverride ? ColCoralBg : ColEmeraldBg, s.ThermalSafetyOverride ? ColCoralBd : ColEmeraldBd);
            string safeTxt = s.ThermalSafetyOverride
                ? T("THERMAL OVERRIDE ACTIVE: 100%", "TERMAL KORUMA AKTİF: %100", "高温安全保护已触发：100%")
                : T("Thermal Safety Guard: ≥88°C → 100%", "Termal Koruma: ≥88°C → %100", "高温安全保护：≥88°C 自动满转");
            DrawText(g, safeTxt, _fBtn, s.ThermalSafetyOverride ? ColCoral : ColEmerald, safeBadge, StringAlignment.Center);
            y += tbH + Sf(12f);

            // Interactive Visual Fan Curve Card
            float curveH = Sf(186f);
            RectangleF cCard = new RectangleF(0, y, availW, curveH);
            DrawBox(g, cCard, Sf(10f), ColCard, ColBorder);
            DrawFanCurvePanel(g, cCard, s);
            y += curveH + Sf(12f);

            // Discovered Fan Channels
            DrawText(g, T("CONTROLLABLE & MONITORED FAN CHANNELS (AUTO-RESTORES BIOS AUTO ON EXIT)",
                "KONTROL EDİLEBİLİR VE İZLENEN FAN KANALLARI (ÇIKIŞTA BIOS OTOMATİĞE DÖNER)",
                "已检测风扇通道（退出软件时自动恢复 BIOS 默认控制，防止过热）"),
                _fSec, ColCyan, new RectangleF(Sf(2f), y, availW, Sf(18f)), StringAlignment.Near);
            y += Sf(24f);

            List<FanChannelInfo> fans = s.Fans ?? new List<FanChannelInfo>();
            if (fans.Count == 0)
            {
                RectangleF empty = new RectangleF(0, y, availW, Sf(56f));
                DrawBox(g, empty, Sf(9f), ColCard, ColAmberBd);
                DrawText(g, T("No controllable PWM fan headers detected yet. Run as Administrator with signed PawnIO to unlock Motherboard & GPU fan control.",
                    "Kontrol edilebilir PWM fan kanalı henüz algılanmadı. Anakart ve GPU fan kontrolü için Yönetici olarak açın.",
                    "暂未检测到可控 PWM 风扇通道；请以管理员身份运行并安装官方 PawnIO 驱动。"),
                    _fSub, ColAmber, new RectangleF(empty.X + Sf(14f), empty.Y, empty.Width - Sf(28f), empty.Height), StringAlignment.Near);
                y += Sf(64f);
            }
            else
            {
                foreach (FanChannelInfo f in fans)
                {
                    float fh = Sf(60f);
                    RectangleF fr = new RectangleF(0, y, availW, fh);
                    if (fr.Bottom >= -4 && fr.Top <= ClientSize.Height + 4)
                    {
                        DrawBox(g, fr, Sf(9f), ColCard, f.Mode > 0 ? ColCyanBd : ColBorder);
                        DrawText(g, f.DisplayName, _fTitle, ColTextPrimary, new RectangleF(fr.X + Sf(14f), fr.Y + Sf(8f), Sf(260f), Sf(19f)), StringAlignment.Near);
                        DrawText(g, f.HardwareName, _fSub, ColTextSec, new RectangleF(fr.X + Sf(14f), fr.Y + Sf(30f), Sf(260f), Sf(17f)), StringAlignment.Near);

                        // Live RPM & Duty readout
                        string rpmStr = f.CurrentRpm >= 0f ? Loc.F("{0:0} RPM", f.CurrentRpm) : "-- RPM";
                        string dutyStr = f.CurrentDutyPct >= 0f ? Loc.F("{0:0}%", f.CurrentDutyPct) : "--%";
                        DrawText(g, rpmStr, _fMetric, ColCyan, new RectangleF(Sf(290f), fr.Y + Sf(8f), Sf(110f), Sf(22f)), StringAlignment.Near);
                        DrawText(g, T("Duty: ", "Hız: ", "转速: ") + dutyStr, _fSub, ColTextBody, new RectangleF(Sf(290f), fr.Y + Sf(32f), Sf(110f), Sf(17f)), StringAlignment.Near);

                        if (f.HasControl)
                        {
                            float mx = fr.Right - Sf(368f);
                            float my = fr.Y + (fr.Height - Sf(26f)) * 0.5f;

                            RectangleF bAuto = new RectangleF(mx, my, Sf(76f), Sf(26f));
                            DrawPillBtn(g, bAuto, T("Auto", "Oto", "自动"), IsHov("fan_mode_0", f), f.Mode == 0, true);
                            AddZone(bAuto, "fan_mode_0", f);

                            RectangleF bCurv = new RectangleF(bAuto.Right + Sf(6f), my, Sf(76f), Sf(26f));
                            DrawPillBtn(g, bCurv, T("Curve", "Eğri", "曲线"), IsHov("fan_mode_2", f), f.Mode == 2, true);
                            AddZone(bCurv, "fan_mode_2", f);

                            RectangleF bMan = new RectangleF(bCurv.Right + Sf(6f), my, Sf(76f), Sf(26f));
                            DrawPillBtn(g, bMan, T("Manual", "Manuel", "手动"), IsHov("fan_mode_1", f), f.Mode == 1, true);
                            AddZone(bMan, "fan_mode_1", f);

                            // Manual duty stepper (-5% / 55% / +5%)
                            RectangleF bMinus = new RectangleF(bMan.Right + Sf(10f), my, Sf(28f), Sf(26f));
                            DrawPillBtn(g, bMinus, "-", IsHov("fan_dec", f), false, true);
                            AddZone(bMinus, "fan_dec", f);

                            DrawText(g, f.ManualDutyPct + "%", _fBtn, f.Mode == 1 ? ColEmerald : ColTextSec,
                                new RectangleF(bMinus.Right + Sf(2f), my, Sf(44f), Sf(26f)), StringAlignment.Center);

                            RectangleF bPlus = new RectangleF(bMinus.Right + Sf(48f), my, Sf(28f), Sf(26f));
                            DrawPillBtn(g, bPlus, "+", IsHov("fan_inc", f), false, true);
                            AddZone(bPlus, "fan_inc", f);
                        }
                        else
                        {
                            DrawTag(g, fr.Right - Sf(108f), fr.Y + Sf(21f), T("READ ONLY", "SALT OKUNUR", "仅监测"), ColBtnBg, ColBorder, ColTextSec, fr.Right - Sf(10f));
                        }
                    }
                    y += fh + Sf(8f);
                }
            }

            return (int)Math.Ceiling(y + _scrollY + Sf(8f));
        }

        private void DrawFanCurvePanel(Graphics g, RectangleF card, TelemetrySnapshot s)
        {
            DrawText(g, T("CUSTOM TEMPERATURE → FAN SPEED CURVE", "ÖZEL SICAKLIK → FAN HIZI EĞRİSİ", "自定义温度 → 风扇转速曲线"),
                _fSec, ColCyan, new RectangleF(card.X + Sf(14f), card.Y + Sf(9f), Sf(360f), Sf(18f)), StringAlignment.Near);

            float liveTemp = s.CpuTempC > 1f ? Math.Max(s.CpuTempC, s.GpuTempC > 1f ? s.GpuTempC * 0.9f : 0f) : (s.GpuTempC > 1f ? s.GpuTempC : 55f);
            int targetDuty = FanController.EvaluateCurve(liveTemp, Settings.CurveTemps, Settings.CurveDuties);
            DrawText(g, Loc.F(T("Live Temp: {0:0}°C  →  Target Curve: {1}%", "Canlı Isı: {0:0}°C  →  Hedef Hız: %{1}", "当前温度：{0:0}°C  →  目标转速：{1}%"), liveTemp, targetDuty),
                _fBtn, ColEmerald, new RectangleF(card.Right - Sf(300f), card.Y + Sf(8f), Sf(286f), Sf(19f)), StringAlignment.Far);

            RectangleF plot = new RectangleF(card.X + Sf(44f), card.Y + Sf(34f), card.Width - Sf(60f), Sf(104f));
            using (Pen gp = new Pen(Color.FromArgb(30, 39, 52), 1f))
            {
                for (int pct = 25; pct <= 100; pct += 25)
                {
                    float py = plot.Bottom - (pct / 100f) * plot.Height;
                    g.DrawLine(gp, plot.X, py, plot.Right, py);
                    DrawText(g, pct + "%", _fSmall, ColTextMuted, new RectangleF(card.X + Sf(4f), py - Sf(8f), Sf(36f), Sf(16f)), StringAlignment.Far);
                }
            }

            // Thermal safety shaded region (>= 88C)
            float x88 = plot.X + ((88f - 20f) / 80f) * plot.Width;
            using (SolidBrush sb = new SolidBrush(Color.FromArgb(32, 248, 113, 113)))
                g.FillRectangle(sb, x88, plot.Y, plot.Right - x88, plot.Height);

            PointF[] pts = new PointF[5];
            for (int i = 0; i < 5; i++)
            {
                float px = plot.X + ((Settings.CurveTemps[i] - 20f) / 80f) * plot.Width;
                float py = plot.Bottom - (Settings.CurveDuties[i] / 100f) * plot.Height;
                pts[i] = new PointF(px, py);
            }

            using (GraphicsPath area = new GraphicsPath())
            {
                area.AddLines(pts);
                area.AddLine(pts[4].X, plot.Bottom, pts[0].X, plot.Bottom);
                area.CloseFigure();
                using (LinearGradientBrush fb = new LinearGradientBrush(plot, Color.FromArgb(42, ColCyan), Color.FromArgb(4, ColCyan), 90f))
                    g.FillPath(fb, area);
            }
            using (Pen cp = new Pen(ColCyan, 2.2f))
                g.DrawLines(cp, pts);

            // Live operating dot
            float lx = plot.X + ((Math.Max(20f, Math.Min(100f, liveTemp)) - 20f) / 80f) * plot.Width;
            float ly = plot.Bottom - (targetDuty / 100f) * plot.Height;
            using (Pen lp = new Pen(Color.FromArgb(110, ColEmerald), 1f)) { lp.DashStyle = DashStyle.Dot; g.DrawLine(lp, lx, plot.Y, lx, plot.Bottom); }
            using (SolidBrush eb = new SolidBrush(ColEmerald)) g.FillEllipse(eb, lx - Sf(5f), ly - Sf(5f), Sf(10f), Sf(10f));

            // 5 editable curve nodes + quick -/+ buttons below plot
            float stepW = (card.Width - Sf(28f)) / 5f;
            float ctrlY = card.Bottom - Sf(34f);
            for (int i = 0; i < 5; i++)
            {
                using (SolidBrush nb = new SolidBrush(ColTextPrimary))
                using (Pen np = new Pen(ColCyan, 2f))
                {
                    g.FillEllipse(nb, pts[i].X - Sf(4f), pts[i].Y - Sf(4f), Sf(8f), Sf(8f));
                    g.DrawEllipse(np, pts[i].X - Sf(4f), pts[i].Y - Sf(4f), Sf(8f), Sf(8f));
                }

                float sx = card.X + Sf(14f) + i * stepW;
                RectangleF bM = new RectangleF(sx, ctrlY, Sf(24f), Sf(23f));
                DrawPillBtn(g, bM, "-", IsHov("curve_dec", i), false, true);
                AddZone(bM, "curve_dec", i);

                DrawText(g, Loc.F("{0}°→{1}%", Settings.CurveTemps[i], Settings.CurveDuties[i]), _fSmall, ColTextBody,
                    new RectangleF(bM.Right + Sf(2f), ctrlY, stepW - Sf(60f), Sf(23f)), StringAlignment.Center);

                RectangleF bP = new RectangleF(sx + stepW - Sf(32f), ctrlY, Sf(24f), Sf(23f));
                DrawPillBtn(g, bP, "+", IsHov("curve_inc", i), false, true);
                AddZone(bP, "curve_inc", i);
            }
        }

        // ==================== TAB 3: ALWAYS-ON-TOP OVERLAY (HUD) ====================
        private int PaintOverlayTab(Graphics g, float availW)
        {
            float y = -_scrollY;

            // Hero Master Always-On-Top Control Bar
            RectangleF top = new RectangleF(0, y, availW, Sf(62f));
            DrawBox(g, top, Sf(10f), ColCard, Settings.OverlayEnabled ? ColEmeraldBd : ColBorder);

            DrawText(g, T("ALWAYS ON TOP — GAMING HUD OVERLAY", "HER ZAMAN ÜSTTE (ALWAYS ON TOP) — OYUNCU HUD", "游戏置顶悬浮窗（Always On Top）"),
                _fTitle, Settings.OverlayEnabled ? ColEmerald : ColTextPrimary,
                new RectangleF(top.X + Sf(16f), top.Y + Sf(10f), Sf(360f), Sf(20f)), StringAlignment.Near);
            DrawText(g, T("Click-through, zero-focus-stealing HUD. Stays on top over games & desktop · Global Hotkey: Ctrl+Shift+O",
                "Tıklama geçiren, oyun akışını asla bozmayan şeffaf HUD · Küresel Kısayol: Ctrl+Shift+O",
                "鼠标穿透、不抢焦点、不影响操作；游戏与桌面全局置顶显示 · 快捷键：Ctrl+Shift+O"),
                _fSub, ColTextSec, new RectangleF(top.X + Sf(16f), top.Y + Sf(33f), availW - Sf(250f), Sf(18f)), StringAlignment.Near);

            // Big ON / OFF Master Switch Button
            RectangleF bTog = new RectangleF(top.Right - Sf(192f), top.Y + Sf(15f), Sf(176f), Sf(32f));
            DrawPillBtn(g, bTog,
                Settings.OverlayEnabled ? T("● Always On Top: ON", "● Her Zaman Üstte: AÇIK", "● 置顶显示：已开启") : T("○ Always On Top: OFF", "○ Her Zaman Üstte: KAPALI", "○ 置顶显示：已关闭"),
                IsHov("hud_toggle", null), Settings.OverlayEnabled, true);
            AddZone(bTog, "hud_toggle", null);
            y += Sf(72f);

            // Middle Split: Left = 6-Corner Monitor Preview + Target Display + Edge Margin + Free Drag | Right = Style, Presets & System Behavior
            float gap = Sf(12f);
            float leftW = availW * 0.46f;
            float rightW = availW - leftW - gap;
            float midH = Sf(238f);

            // Left: Visual Monitor 6-Position Picker + Multi-Monitor Target Selector + Margin + Free Drag
            RectangleF posCard = new RectangleF(0, y, leftW, midH);
            DrawBox(g, posCard, Sf(10f), ColCard, ColBorder);
            DrawText(g, T("SCREEN POSITION, EDGE MARGIN & FREE DRAG", "EKRAN KONUMU, KENAR BOŞLUĞU VE SERBEST SÜRÜKLE", "屏幕位置、贴边间距与自由拖动"),
                _fSec, ColCyan, new RectangleF(posCard.X + Sf(14f), posCard.Y + Sf(10f), posCard.Width - Sf(28f), Sf(18f)), StringAlignment.Near);

            RectangleF mon = new RectangleF(posCard.X + Sf(14f), posCard.Y + Sf(32f), posCard.Width - Sf(28f), Sf(96f));
            DrawBox(g, mon, Sf(8f), Color.FromArgb(12, 16, 23), Color.FromArgb(52, 64, 82));

            string[] posNames = new string[]
            {
                T("Top-Left", "Üst-Sol", "左上角"),
                T("Top-Center", "Üst-Orta", "顶部居中"),
                T("Top-Right", "Üst-Sağ", "右上角"),
                T("Bottom-Left", "Alt-Sol", "左下角"),
                T("Bottom-Center", "Alt-Orta", "底部居中"),
                T("Bottom-Right", "Alt-Sağ", "右下角")
            };
            float cellW = (mon.Width - Sf(24f)) / 3f;
            float cellH = (mon.Height - Sf(20f)) / 2f;
            for (int i = 0; i < 6; i++)
            {
                int col = i % 3;
                int row = i / 3;
                RectangleF zr = new RectangleF(mon.X + Sf(8f) + col * (cellW + Sf(4f)), mon.Y + Sf(7f) + row * (cellH + Sf(6f)), cellW, cellH);
                OverlayAnchor anc = (OverlayAnchor)i;
                bool act = !Settings.OverlayCustomPos && Settings.OverlayPosition == anc;
                bool hov = IsHov("hud_pos", anc);
                DrawBox(g, zr, Sf(6f), act ? ColEmeraldBg : (hov ? ColBtnHover : Color.FromArgb(20, 26, 36)), act ? ColEmerald : (hov ? ColCyan : ColBorder));
                DrawText(g, posNames[i], _fBtn, act ? ColEmerald : ColTextBody, zr, StringAlignment.Center);
                AddZone(zr, "hud_pos", anc);
            }

            // Multi-Monitor Target Display Selector Row
            int scrCount = DemoMode ? 2 : Math.Max(1, Screen.AllScreens.Length);
            int maxMonBtns = Math.Min(3, scrCount); // Primary + up to 3 displays
            float monY = posCard.Y + Sf(136f);
            float monH = Sf(24f);
            if (scrCount <= 1)
            {
                Screen prim = Screen.PrimaryScreen;
                string primLbl = prim != null
                    ? Loc.F(T("Monitor: Primary ({0}×{1})", "Monitör: Ana Ekran ({0}×{1})", "目标屏幕：主显示器 ({0}×{1})"), prim.Bounds.Width, prim.Bounds.Height)
                    : T("Monitor: Primary", "Monitör: Ana Ekran", "目标屏幕：主显示器");
                RectangleF mr = new RectangleF(posCard.X + Sf(14f), monY, posCard.Width - Sf(28f), monH);
                DrawPillBtn(g, mr, primLbl, IsHov("hud_monitor", 0), !Settings.OverlayCustomPos, true);
                AddZone(mr, "hud_monitor", 0);
            }
            else
            {
                int totalBtns = 1 + maxMonBtns;
                float mGap2 = Sf(6f);
                float mw2 = (posCard.Width - Sf(28f) - mGap2 * (totalBtns - 1)) / totalBtns;
                for (int i = 0; i < totalBtns; i++)
                {
                    RectangleF mr = new RectangleF(posCard.X + Sf(14f) + i * (mw2 + mGap2), monY, mw2, monH);
                    string lbl = i == 0
                        ? T("Primary", "Ana Ekran", "主屏")
                        : Loc.F(T("Display {0}", "Ekran {0}", "屏幕 {0}"), i);
                    bool act = !Settings.OverlayCustomPos && Settings.OverlayMonitorIndex == i;
                    DrawPillBtn(g, mr, lbl, IsHov("hud_monitor", i), act, true);
                    AddZone(mr, "hud_monitor", i);
                }
            }

            // Edge Margin Quick Pills (0px Flush / 6px / 14px / 24px) + Fine -/+ Stepper
            float mgY = posCard.Y + Sf(167f);
            float mgH = Sf(24f);
            float innerX = posCard.X + Sf(14f);
            float innerW = posCard.Width - Sf(28f);
            float mgLblW = Sf(46f);
            float mgStepW = Sf(24f);
            float mgValW = Sf(36f);
            float mgGap = Sf(4f);
            float mgPresetW = Math.Max(Sf(32f), (innerW - mgLblW - mgStepW * 2f - mgValW - mgGap * 6f) / 4f);

            DrawText(g, T("Margin", "Kenar", "边距"), _fSmall, ColTextSec, new RectangleF(innerX, mgY, mgLblW, mgH), StringAlignment.Near);
            float mxCur = innerX + mgLblW + mgGap;
            int[] mgPresets = new int[] { 0, 6, 14, 24 };
            for (int i = 0; i < mgPresets.Length; i++)
            {
                int mv = mgPresets[i];
                RectangleF mpr = new RectangleF(mxCur, mgY, mgPresetW, mgH);
                string mLbl = mv == 0 ? T("0px", "0px", "0贴边") : (mv + "px");
                bool mAct = !Settings.OverlayCustomPos && Settings.OverlayMargin == mv;
                DrawPillBtn(g, mpr, mLbl, IsHov("hud_margin", mv), mAct, true);
                AddZone(mpr, "hud_margin", mv);
                mxCur += mgPresetW + mgGap;
            }
            RectangleF bMgDec = new RectangleF(mxCur, mgY, mgStepW, mgH);
            DrawPillBtn(g, bMgDec, "-", IsHov("hud_margin_dec", null), false, Settings.OverlayMargin > 0);
            if (Settings.OverlayMargin > 0) AddZone(bMgDec, "hud_margin_dec", null);
            mxCur += mgStepW + mgGap;

            DrawText(g, Settings.OverlayMargin + "px", _fSmall, ColTextPrimary, new RectangleF(mxCur, mgY, mgValW, mgH), StringAlignment.Center);
            mxCur += mgValW + mgGap;

            RectangleF bMgInc = new RectangleF(mxCur, mgY, mgStepW, mgH);
            DrawPillBtn(g, bMgInc, "+", IsHov("hud_margin_inc", null), false, Settings.OverlayMargin < 120);
            if (Settings.OverlayMargin < 120) AddZone(bMgInc, "hud_margin_inc", null);

            // Unlock & Drag HUD Anywhere Button
            float drY = posCard.Y + Sf(199f);
            RectangleF bDrag = new RectangleF(innerX, drY, innerW, Sf(26f));
            string dragLbl;
            if (HudDragUnlocked)
            {
                dragLbl = T("✓ Drag HUD with Mouse (Right-Click / Click Here to Lock)", "✓ HUD'u Fareyle Sürükle (Kilitlemek için Tıkla)", "✓ 请直接鼠标拖动悬浮窗（右键或点此锁定穿透）");
            }
            else if (Settings.OverlayCustomPos)
            {
                dragLbl = Loc.F(T("Unlock & Drag HUD  ·  Custom ({0}, {1})", "HUD'u Serbest Sürükle  ·  Özel ({0}, {1})", "解锁自由拖动  ·  自定义坐标 ({0}, {1})"), Settings.OverlayCustomX, Settings.OverlayCustomY);
            }
            else
            {
                dragLbl = T("Unlock & Drag HUD Anywhere (Custom Position)", "HUD'u Fareyle Serbest Sürükle (Özel Konum)", "解锁鼠标自由拖动（可完全贴顶 / 任意位置）");
            }
            DrawPillBtn(g, bDrag, dragLbl, IsHov("hud_drag_toggle", null), HudDragUnlocked, true);
            AddZone(bDrag, "hud_drag_toggle", null);

            // Right: Style, Opacity, Size, Presets & System Tray / Startup Behavior
            RectangleF styCard = new RectangleF(leftW + gap, y, rightW, midH);
            DrawBox(g, styCard, Sf(10f), ColCard, ColBorder);
            float sx = styCard.X + Sf(14f);
            float sy = styCard.Y + Sf(10f);

            DrawText(g, T("STYLE, OPACITY, PRESETS & TRAY BEHAVIOR", "TASARIM, SAYDAMLIK, PROFİLLER VE SİSTEM TEPSİSİ", "样式、透明度、快捷方案与托盘行为"),
                _fSec, ColCyan, new RectangleF(sx, sy, rightW - Sf(28f), Sf(18f)), StringAlignment.Near);
            sy += Sf(26f);

            // Layout Mode: Horizontal Bar vs Compact Card
            float bw2 = (rightW - Sf(36f)) * 0.5f;
            RectangleF bHor = new RectangleF(sx, sy, bw2, Sf(28f));
            DrawPillBtn(g, bHor, T("Horizontal Bar", "Yatay Şerit Bar", "横向极简条"), IsHov("hud_layout", true), Settings.OverlayHorizontal, true);
            AddZone(bHor, "hud_layout", true);
            RectangleF bVer = new RectangleF(bHor.Right + Sf(8f), sy, bw2, Sf(28f));
            DrawPillBtn(g, bVer, T("Compact Card", "Dikey Kart", "竖向紧凑卡片"), IsHov("hud_layout", false), !Settings.OverlayHorizontal, true);
            AddZone(bVer, "hud_layout", false);
            sy += Sf(39f);

            // Opacity pills: 65%, 80%, 90%, 100%
            int[] opac = new int[] { 65, 80, 90, 100 };
            float opW = (rightW - Sf(52f)) / 4f;
            for (int i = 0; i < 4; i++)
            {
                RectangleF or = new RectangleF(sx + i * (opW + Sf(8f)), sy, opW, Sf(26f));
                DrawPillBtn(g, or, opac[i] + "%", IsHov("hud_opacity", opac[i]), Math.Abs(Settings.OverlayOpacity - opac[i]) <= 5, true);
                AddZone(or, "hud_opacity", opac[i]);
            }
            sy += Sf(37f);

            // Size pills: Compact / Normal / Large
            string[] szLbl = new string[] { T("Compact", "Küçük", "紧凑"), T("Normal", "Normal", "标准"), T("Large", "Büyük", "大号") };
            float szW = (rightW - Sf(44f)) / 3f;
            for (int i = 0; i < 3; i++)
            {
                RectangleF sr = new RectangleF(sx + i * (szW + Sf(8f)), sy, szW, Sf(26f));
                DrawPillBtn(g, sr, szLbl[i], IsHov("hud_scale", i), Settings.OverlayScale == i, true);
                AddZone(sr, "hud_scale", i);
            }
            sy += Sf(37f);

            // Quick Gamer Presets
            string[] pIds = new string[] { "minimal", "gamer", "thermal" };
            string[] pLbl = new string[] { T("Minimal", "Minimal", "极简模式"), T("Pro Gamer", "Oyuncu", "玩家全显"), T("Thermals", "Isı & Fan", "温控专精") };
            for (int i = 0; i < 3; i++)
            {
                RectangleF pr = new RectangleF(sx + i * (szW + Sf(8f)), sy, szW, Sf(26f));
                DrawPillBtn(g, pr, pLbl[i], IsHov("hud_preset", pIds[i]), false, true);
                AddZone(pr, "hud_preset", pIds[i]);
            }
            sy += Sf(37f);

            // System Behavior Toggles: Minimize to Tray & Start with Windows
            RectangleF bTray = new RectangleF(sx, sy, bw2, Sf(26f));
            DrawPillBtn(g, bTray,
                (Settings.MinimizeToTray ? "✓ " : "○ ") + T("Close to Tray", "Tepsiye Küçült", "关闭时驻留托盘"),
                IsHov("toggle_tray", null), Settings.MinimizeToTray, true);
            AddZone(bTray, "toggle_tray", null);

            RectangleF bAutoStart = new RectangleF(bTray.Right + Sf(8f), sy, bw2, Sf(26f));
            DrawPillBtn(g, bAutoStart,
                (Settings.StartWithWindows ? "✓ " : "○ ") + T("Start with Windows", "Windows ile Başlat", "开机自启动"),
                IsHov("toggle_startup", null), Settings.StartWithWindows, true);
            AddZone(bAutoStart, "toggle_startup", null);

            y += midH + Sf(14f);

            // 14 Individual Metric Checkboxes
            DrawText(g, T("CHOOSE EXACT DATA TO DISPLAY ON OVERLAY (LIVE UPDATE)",
                "ÜSTTE GÖSTERİLECEK VERİLERİ SEÇ (ANINDA GÜNCELLENİR)",
                "自定义悬浮窗显示项（勾选即刻实时生效）"),
                _fSec, ColCyan, new RectangleF(Sf(2f), y, availW, Sf(18f)), StringAlignment.Near);
            y += Sf(24f);

            string[] keys = new string[]
            {
                "HudShowFps", "HudShowFps1Low", "HudShowCpuTemp", "HudShowCpuLoad",
                "HudShowCpuClockPower", "HudShowGpuTemp", "HudShowGpuLoad", "HudShowGpuVram",
                "HudShowGpuFan", "HudShowGpuPower", "HudShowRam", "HudShowCaseFans",
                "HudShowBoardDiskTemp", "HudShowNetClock"
            };
            string[] titles = new string[]
            {
                T("FPS & Frametime (ms)", "FPS ve Kare Süresi (ms)", "实时帧率 FPS 与帧时间"),
                T("1% Low FPS", "%1 Low FPS", "1% Low 最低帧率"),
                T("CPU Temperature (°C)", "CPU Sıcaklığı (°C)", "CPU 温度 (°C)"),
                T("CPU Usage (%)", "CPU Kullanımı (%)", "CPU 占用率 (%)"),
                T("CPU Clock (GHz) & Power (W)", "CPU Frekans (GHz) ve Güç (W)", "CPU 频率与功耗 (W)"),
                T("GPU Temperature (°C)", "GPU Sıcaklığı (°C)", "GPU 温度 (°C)"),
                T("GPU Usage (%)", "GPU Kullanımı (%)", "GPU 占用率 (%)"),
                T("GPU VRAM Usage (GB)", "GPU VRAM Kullanımı (GB)", "GPU 显存占用 (GB)"),
                T("GPU Fan Speed (RPM / %)", "GPU Fan Hızı (RPM / %)", "GPU 风扇转速 (RPM / %)"),
                T("GPU Power Draw (W)", "GPU Güç Tüketimi (W)", "GPU 实时功耗 (W)"),
                T("RAM Usage (GB & %)", "RAM Kullanımı (GB & %)", "内存占用 (GB / %)"),
                T("CPU / Case Fan RPM", "CPU / Kasa Fan Hızı (RPM)", "CPU / 机箱风扇转速"),
                T("Motherboard & SSD Temp", "Anakart ve SSD Isısı", "主板与硬盘温度"),
                T("Network Up/Down & Clock", "Ağ Hızı ve Saat", "实时网速与时间")
            };
            bool[] vals = new bool[]
            {
                Settings.HudShowFps, Settings.HudShowFps1Low, Settings.HudShowCpuTemp, Settings.HudShowCpuLoad,
                Settings.HudShowCpuClockPower, Settings.HudShowGpuTemp, Settings.HudShowGpuLoad, Settings.HudShowGpuVram,
                Settings.HudShowGpuFan, Settings.HudShowGpuPower, Settings.HudShowRam, Settings.HudShowCaseFans,
                Settings.HudShowBoardDiskTemp, Settings.HudShowNetClock
            };

            float mGap = Sf(8f);
            float mw = (availW - mGap) * 0.5f;
            float mh = Sf(36f);
            for (int i = 0; i < keys.Length; i++)
            {
                int col = i % 2;
                float mx = col * (mw + mGap);
                RectangleF mr = new RectangleF(mx, y, mw, mh);
                if (mr.Bottom >= -4 && mr.Top <= ClientSize.Height + 4)
                {
                    bool hov = IsHov("hud_metric", keys[i]);
                    DrawBox(g, mr, Sf(8f), hov ? ColCardHover : ColCard, vals[i] ? ColEmeraldBd : (hov ? ColBorderHover : ColBorder));
                    AddZone(mr, "hud_metric", keys[i]);
                    float cb = Sf(16f);
                    DrawCheckbox(g, new RectangleF(mr.X + Sf(12f), mr.Y + (mr.Height - cb) * 0.5f, cb, cb), vals[i], ColEmerald);
                    DrawText(g, titles[i], _fTitle, vals[i] ? ColTextPrimary : ColTextSec,
                        new RectangleF(mr.X + Sf(36f), mr.Y, mr.Width - Sf(46f), mr.Height), StringAlignment.Near);
                }
                if (col == 1 || i == keys.Length - 1) y += mh + Sf(7f);
            }

            return (int)Math.Ceiling(y + _scrollY + Sf(8f));
        }

        // ==================== SHARED GDI+ HELPERS ====================
        private void DrawCheckbox(Graphics g, RectangleF r, bool chk, Color col)
        {
            if (chk)
            {
                DrawBox(g, r, Sf(4f), col, col);
                using (Pen p = new Pen(ColBg, Math.Max(1.6f, Sf(1.8f))))
                {
                    p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; p.LineJoin = LineJoin.Round;
                    g.DrawLines(p, new PointF[]
                    {
                        new PointF(r.X + r.Width * 0.22f, r.Y + r.Height * 0.52f),
                        new PointF(r.X + r.Width * 0.43f, r.Y + r.Height * 0.74f),
                        new PointF(r.X + r.Width * 0.78f, r.Y + r.Height * 0.28f)
                    });
                }
            }
            else
            {
                DrawBox(g, r, Sf(4f), ColBg, Color.FromArgb(72, 86, 106));
            }
        }

        private void DrawPillBtn(Graphics g, RectangleF r, string text, bool hov, bool primary, bool enabled)
        {
            Color bg, bd, fg;
            if (!enabled) { bg = ColCard; bd = Color.FromArgb(32, 40, 52); fg = ColTextMuted; }
            else if (primary) { bg = hov ? ColPrimaryHov : ColPrimaryBg; bd = ColEmerald; fg = Color.White; }
            else { bg = hov ? ColBtnHover : ColBtnBg; bd = hov ? Color.FromArgb(78, 94, 118) : ColBtnBd; fg = ColTextBody; }
            DrawBox(g, r, Sf(7f), bg, bd);
            DrawText(g, text, _fBtn, fg, r, StringAlignment.Center);
        }

        private float DrawTag(Graphics g, float x, float y, string text, Color bg, Color bd, Color fg, float maxX)
        {
            SizeF sz = MeasureTight(g, text, _fBadge);
            float w = sz.Width + Sf(12f), h = Sf(16.5f);
            if (x + w > maxX) return maxX;
            RectangleF r = new RectangleF(x, y, w, h);
            DrawBox(g, r, Sf(4.5f), bg, bd);
            DrawText(g, text, _fBadge, fg, r, StringAlignment.Center);
            return x + w + Sf(6f);
        }

        private static SizeF MeasureTight(Graphics g, string text, Font font)
        {
            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.NoWrap;
                return g.MeasureString(text, font, PointF.Empty, sf);
            }
        }

        private static void DrawText(Graphics g, string text, Font font, Color col, RectangleF r, StringAlignment align)
        {
            using (SolidBrush b = new SolidBrush(col))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = align; sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags = StringFormatFlags.NoWrap; sf.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(text, font, b, r, sf);
            }
        }

        private static void DrawBox(Graphics g, RectangleF r, float rad, Color fill, Color border)
        {
            using (GraphicsPath p = Rounded(r, rad))
            using (SolidBrush fb = new SolidBrush(fill))
            using (Pen bp = new Pen(border, 1f)) { g.FillPath(fb, p); g.DrawPath(bp, p); }
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

    internal sealed class MainForm : Form
    {
        private const int LogicalW = 960;
        private const int LogicalH = 690;

        private static readonly Color ColBg = Color.FromArgb(11, 15, 22);
        private static readonly Color ColCard = Color.FromArgb(19, 24, 33);
        private static readonly Color ColBorder = Color.FromArgb(38, 48, 63);
        private static readonly Color ColDivider = Color.FromArgb(28, 36, 49);
        private static readonly Color ColTextPrimary = Color.FromArgb(241, 245, 249);
        private static readonly Color ColTextSec = Color.FromArgb(148, 163, 184);
        private static readonly Color ColTextMuted = Color.FromArgb(100, 116, 139);
        private static readonly Color ColCyan = Color.FromArgb(56, 189, 248);
        private static readonly Color ColEmerald = Color.FromArgb(52, 211, 153);
        private static readonly Color ColAmber = Color.FromArgb(251, 191, 36);
        private static readonly Color ColBtnHover = Color.FromArgb(36, 45, 60);

        private readonly LaunchConfig _cfg;
        private readonly CorePulseSettings _settings;
        private readonly HardwareEngine _engine;
        private readonly FanController _fanCtrl;
        private readonly PulseContentPanel _content;
        private readonly OverlayHudForm _hud;
        private readonly System.Windows.Forms.Timer _pollTimer;
        private readonly Icon _appIcon;
        private readonly List<HitZone> _chromeZones = new List<HitZone>();
        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;

        private float _scale = 1.0f;
        private int _activeTab;
        private string _hoverKey = "";
        private volatile bool _polling;
        private volatile bool _shuttingDown;
        private readonly ManualResetEvent _pollIdle = new ManualResetEvent(true);
        private bool _hotkeyRegistered;
        private bool _forceExit;
        private bool _initialHideDone;

        private Font _fBrand;
        private Font _fSmallPill;
        private Font _fLangZh;
        private Font _fTab;
        private Font _fStatus;

        public MainForm(LaunchConfig cfg)
        {
            _cfg = cfg ?? new LaunchConfig();
            _settings = _cfg.DemoMode ? new CorePulseSettings() : CorePulseSettings.Load();
            if (!string.IsNullOrEmpty(_cfg.ForceLang)) _settings.Language = _cfg.ForceLang;
            if (_cfg.DemoMode)
            {
                _settings.OverlayEnabled = _cfg.ShowDemoOverlay;
                _settings.FanProfile = FanProfileMode.Balanced;
                FanController.ApplyPresetToSettings(_settings, FanProfileMode.Balanced);
                _settings.MinimizeToTray = true;
                _settings.StartWithWindows = false;
            }
            else
            {
                _settings.StartWithWindows = WinNative.IsStartupEnabled();
            }
            _activeTab = _cfg.InitialTab;

            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = ColBg;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

            try { _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); if (_appIcon != null) Icon = _appIcon; } catch { }

            _engine = new HardwareEngine();
            _engine.Initialize(_cfg.DemoMode);

            _fanCtrl = new FanController();
            _fanCtrl.RestoreSavedSettingsOnStartup(_settings, _engine.Latest.Fans, _cfg.DemoMode);
            _fanCtrl.Tick(_settings, _engine.Latest, _cfg.DemoMode);

            _hud = new OverlayHudForm();
            _hud.CustomPositionChanged += delegate(int cx, int cy)
            {
                _settings.OverlayCustomPos = true;
                _settings.OverlayCustomX = cx;
                _settings.OverlayCustomY = cy;
                if (!_cfg.DemoMode) _settings.Save();
                _content.Invalidate();
            };
            _hud.LockRequested += delegate
            {
                SetHudDragUnlocked(false);
            };

            _content = new PulseContentPanel();
            _content.DemoMode = _cfg.DemoMode;
            _content.Settings = _settings;
            _content.Engine = _engine;
            _content.Snap = _engine.Latest;
            _content.ActionInvoked += OnContentAction;
            Controls.Add(_content);

            if (!_cfg.DemoMode)
            {
                InitTrayIcon();
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            }

            UpdateWindowTitle();
            UpdateDpiScale(GetInitialDpi());
            ClientSize = new Size(S(LogicalW), S(LogicalH));
            MinimumSize = new Size(S(820), S(560));

            _pollTimer = new System.Windows.Forms.Timer();
            _pollTimer.Interval = 1000;
            _pollTimer.Tick += OnPollTick;
            if (!_cfg.DemoMode) _pollTimer.Start();

            if (_settings.OverlayEnabled)
                ShowOrHideHud(true);
        }

        private void InitTrayIcon()
        {
            try
            {
                _trayMenu = new ContextMenuStrip();
                _trayIcon = new NotifyIcon();
                _trayIcon.Icon = _appIcon ?? SystemIcons.Application;
                _trayIcon.Text = "CorePulse v1.2";
                _trayIcon.ContextMenuStrip = _trayMenu;
                _trayIcon.DoubleClick += delegate { RestoreFromTray(); };
                RebuildTrayMenu();
                UpdateTrayTooltip(_engine.Latest);
                _trayIcon.Visible = true;
            }
            catch { }
        }

        private void RebuildTrayMenu()
        {
            if (_trayMenu == null) return;
            while (_trayMenu.Items.Count > 0)
            {
                ToolStripItem old = _trayMenu.Items[0];
                _trayMenu.Items.RemoveAt(0);
                old.Dispose();
            }

            ToolStripMenuItem mShow = new ToolStripMenuItem(T("Show CorePulse", "CorePulse'ı Göster", "显示 CorePulse 主界面"));
            mShow.Click += delegate { RestoreFromTray(); };
            _trayMenu.Items.Add(mShow);

            ToolStripMenuItem mHud = new ToolStripMenuItem(T("Always On Top HUD (Ctrl+Shift+O)", "Her Zaman Üstte HUD (Ctrl+Shift+O)", "置顶悬浮窗 HUD (Ctrl+Shift+O)"));
            mHud.Checked = _settings.OverlayEnabled;
            mHud.Click += delegate { ToggleOverlay(); };
            _trayMenu.Items.Add(mHud);

            ToolStripMenuItem mDrag = new ToolStripMenuItem(T("Unlock & Drag HUD", "HUD'u Serbest Sürükle", "解锁自由拖动悬浮窗"));
            mDrag.Checked = _hud.DragUnlocked;
            mDrag.Click += delegate { SetHudDragUnlocked(!_hud.DragUnlocked); };
            _trayMenu.Items.Add(mDrag);

            _trayMenu.Items.Add(new ToolStripSeparator());

            FanProfileMode[] modes = new FanProfileMode[] { FanProfileMode.BiosAuto, FanProfileMode.Silent, FanProfileMode.Balanced, FanProfileMode.Turbo };
            string[] labels = new string[]
            {
                T("Fan: BIOS Auto", "Fan: BIOS Otomatik", "风扇：BIOS 自动"),
                T("Fan: Silent", "Fan: Sessiz", "风扇：静音模式"),
                T("Fan: Balanced", "Fan: Dengeli", "风扇：均衡模式"),
                T("Fan: Turbo", "Fan: Turbo", "风扇：强冷模式")
            };
            for (int i = 0; i < modes.Length; i++)
            {
                FanProfileMode targetMode = modes[i];
                ToolStripMenuItem mf = new ToolStripMenuItem(labels[i]);
                mf.Checked = (_settings.FanProfile == targetMode);
                mf.Click += delegate
                {
                    _fanCtrl.ApplyGlobalProfile(_settings, _engine.Latest.Fans, targetMode, _cfg.DemoMode);
                    _fanCtrl.Tick(_settings, _engine.Latest, _cfg.DemoMode);
                    if (!_cfg.DemoMode) _settings.Save();
                    RebuildTrayMenu();
                    _content.Invalidate();
                };
                _trayMenu.Items.Add(mf);
            }

            _trayMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem mExit = new ToolStripMenuItem(T("Exit & Restore BIOS Fans", "Çıkış (Fanları BIOS'a Döndür)", "退出并恢复 BIOS 风扇"));
            mExit.Click += delegate { _forceExit = true; Close(); };
            _trayMenu.Items.Add(mExit);
        }

        private void UpdateTrayTooltip(TelemetrySnapshot s)
        {
            if (_trayIcon == null || s == null) return;
            try
            {
                string cpu = s.CpuTempC > 0f ? Loc.F("CPU {0:0}°C", s.CpuTempC) : Loc.F("CPU {0:0}%", s.CpuLoadPct);
                string gpu = s.GpuTempC > 0f ? Loc.F("GPU {0:0}°C", s.GpuTempC) : "GPU --";
                string tip = s.Fps > 0.5f
                    ? Loc.F("CorePulse · {0} · {1} · {2:0} FPS", cpu, gpu, s.Fps)
                    : Loc.F("CorePulse · {0} · {1}", cpu, gpu);
                if (tip.Length > 63) tip = tip.Substring(0, 63);
                _trayIcon.Text = tip;
            }
            catch { }
        }

        private void RestoreFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            bool wasTop = TopMost;
            TopMost = true;
            TopMost = wasTop;
            Activate();
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume || IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    // BIOS resets SuperIO/GPU PWM registers on Sleep/Resume -> re-apply active curves immediately!
                    _fanCtrl.InvalidateCache();
                    _fanCtrl.Tick(_settings, _engine.Latest, _cfg.DemoMode);
                    if (_settings.OverlayEnabled) _hud.UpdateHud(_settings, _engine.Latest, _scale);
                });
            }
            catch { }
        }

        private void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (_settings.OverlayEnabled) _hud.UpdateHud(_settings, _engine.Latest, _scale);
                    if (Visible) { _content.Invalidate(); Invalidate(); }
                });
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_cfg.StartMinimized && !_cfg.DemoMode && !_initialHideDone)
            {
                _initialHideDone = true;
                Hide();
            }
        }

        private string Lang { get { return _settings.Language ?? "en"; } }
        private string T(string en, string tr, string zh) { return Loc.T(Lang, en, tr, zh); }
        private int S(float v) { return (int)Math.Round(v * _scale); }
        private float Sf(float v) { return v * _scale; }
        private float PtToPx(float pt) { return pt * (96f / 72f) * _scale; }

        private float GetInitialDpi()
        {
            using (Graphics g = CreateGraphics()) return g.DpiX > 0 ? g.DpiX : 96f;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WinNative.ApplyDarkTitleBar(Handle, ColBg, ColTextPrimary);
            try
            {
                uint d = WinNative.GetDpiForWindow(Handle);
                if (d >= 72 && Math.Abs((d / 96f) - _scale) > 0.01f)
                {
                    UpdateDpiScale(d);
                    ClientSize = new Size(S(LogicalW), S(LogicalH));
                    MinimumSize = new Size(S(820), S(560));
                }
            }
            catch { }

            if (!_cfg.DemoMode)
            {
                WinNative.AllowWakeMessageFromLowerIntegrity(Handle);
                _hotkeyRegistered = WinNative.RegisterHotKey(Handle, WinNative.HOTKEY_TOGGLE_HUD, WinNative.MOD_CONTROL | WinNative.MOD_SHIFT, WinNative.VK_O);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (WinNative.WM_COREPULSE_SHOW != 0 && (uint)m.Msg == WinNative.WM_COREPULSE_SHOW)
            {
                RestoreFromTray();
                return;
            }
            if (m.Msg == WinNative.WM_DPICHANGED)
            {
                int nd = (int)(m.WParam.ToInt64() & 0xFFFF);
                if (nd >= 72)
                {
                    UpdateDpiScale(nd);
                    if (_settings.OverlayEnabled && _hud != null && !_hud.IsDisposed)
                        _hud.UpdateHud(_settings, _engine.Latest, _scale);
                    Invalidate();
                }
            }
            else if (m.Msg == WinNative.WM_HOTKEY && m.WParam.ToInt32() == WinNative.HOTKEY_TOGGLE_HUD)
            {
                ToggleOverlay();
            }
            base.WndProc(ref m);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutAll(); Invalidate(); }

        private void UpdateDpiScale(float dpi)
        {
            _scale = dpi / 96f;
            RebuildFonts();
            _content.Configure(_scale, Lang, _activeTab);
            LayoutAll();
        }

        private void RebuildFonts()
        {
            DisposeFonts();
            string fam = Loc.FontFamily(Lang);
            _fBrand = new Font("Segoe UI", PtToPx(11.5f), FontStyle.Bold, GraphicsUnit.Pixel);
            _fSmallPill = new Font(fam, PtToPx(7.6f), FontStyle.Bold, GraphicsUnit.Pixel);
            _fLangZh = new Font("Microsoft YaHei UI", PtToPx(7.5f), FontStyle.Bold, GraphicsUnit.Pixel);
            _fTab = new Font(fam, PtToPx(8.9f), FontStyle.Bold, GraphicsUnit.Pixel);
            _fStatus = new Font(fam, PtToPx(8.1f), FontStyle.Regular, GraphicsUnit.Pixel);
        }

        private void DisposeFonts()
        {
            if (_fBrand != null) { _fBrand.Dispose(); _fBrand = null; }
            if (_fSmallPill != null) { _fSmallPill.Dispose(); _fSmallPill = null; }
            if (_fLangZh != null) { _fLangZh.Dispose(); _fLangZh = null; }
            if (_fTab != null) { _fTab.Dispose(); _fTab = null; }
            if (_fStatus != null) { _fStatus.Dispose(); _fStatus = null; }
        }

        private void UpdateWindowTitle()
        {
            Text = T("CorePulse — Hardware Monitor, Fan Controller & Gaming HUD",
                "CorePulse — Donanım Sensör, Fan Kontrol & Oyuncu HUD",
                "CorePulse — 专业硬件监控、风扇曲线控制与游戏置顶悬浮窗");
        }

        private void LayoutAll()
        {
            int pad = S(20f);
            int top = S(100f);
            int bot = S(36f);
            _content.SetBounds(pad, top, Math.Max(S(300f), ClientSize.Width - pad * 2), Math.Max(S(160f), ClientSize.Height - top - bot));
        }

        private void OnPollTick(object sender, EventArgs e)
        {
            if (_polling || _shuttingDown || IsDisposed) return;
            _polling = true;
            _pollIdle.Reset();
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (_shuttingDown) return;
                    TelemetrySnapshot snap = _engine.PollOnce();
                    if (_shuttingDown) return;
                    _fanCtrl.Tick(_settings, snap, false);
                    if (!_shuttingDown && !IsDisposed && IsHandleCreated)
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (_shuttingDown || IsDisposed) return;
                            _content.Snap = snap;
                            if (Visible) _content.Invalidate();
                            if (_settings.OverlayEnabled)
                                _hud.UpdateHud(_settings, snap, _scale);
                            UpdateTrayTooltip(snap);
                            if (Visible) Invalidate();
                        });
                    }
                }
                catch { }
                finally
                {
                    _polling = false;
                    try { _pollIdle.Set(); } catch { }
                }
            });
        }

        private void ShowOrHideHud(bool show)
        {
            _settings.OverlayEnabled = show;
            if (!_cfg.DemoMode) _settings.Save();
            if (show)
            {
                _hud.UpdateHud(_settings, _engine.Latest, _scale);
                if (!_hud.Visible) _hud.Show();
                _hud.EnsureTopMost();
            }
            else
            {
                if (_hud.DragUnlocked) { _hud.SetDragUnlocked(false); _content.HudDragUnlocked = false; }
                if (_hud.Visible) _hud.Hide();
            }
            RebuildTrayMenu();
            _content.Invalidate();
            Invalidate();
        }

        private void SetHudDragUnlocked(bool unlocked)
        {
            if (unlocked && !_settings.OverlayEnabled)
                ShowOrHideHud(true);
            _hud.SetDragUnlocked(unlocked);
            _content.HudDragUnlocked = _hud.DragUnlocked;
            RebuildTrayMenu();
            _content.Invalidate();
            Invalidate();
        }

        private void ToggleOverlay() { ShowOrHideHud(!_settings.OverlayEnabled); }

        private void RefreshHudIfVisible()
        {
            if (!_cfg.DemoMode) _settings.Save();
            if (_settings.OverlayEnabled)
                _hud.UpdateHud(_settings, _engine.Latest, _scale);
            _content.Invalidate();
            Invalidate();
        }

        private void SetLanguage(string lang)
        {
            if (_settings.Language == lang) return;
            _settings.Language = lang;
            if (!_cfg.DemoMode) _settings.Save();
            RebuildFonts();
            UpdateWindowTitle();
            RebuildTrayMenu();
            _content.Configure(_scale, Lang, _activeTab);
            RefreshHudIfVisible();
        }

        private void SwitchTab(int t)
        {
            if (_activeTab == t) return;
            _activeTab = t;
            _content.Configure(_scale, Lang, _activeTab);
            Invalidate();
        }

        private void OnContentAction(string key, object payload)
        {
            if (key == "elevate")
            {
                if (!_cfg.DemoMode && WinNative.RestartAsAdministrator("--handoff --tab " + _activeTab)) { _forceExit = true; Close(); }
            }
            else if (key == "open_pawnio")
            {
                WinNative.OpenUrl("https://github.com/namazso/PawnIO.Setup/releases/latest");
            }
            else if (key == "sensor_filter")
            {
                _content.SensorFilter = payload as string ?? "ALL";
                _content.Invalidate();
            }
            else if (key == "sensor_reset")
            {
                _engine.ResetMinMax();
                _content.Invalidate();
            }
            else if (key == "fan_profile" && payload is FanProfileMode)
            {
                _fanCtrl.ApplyGlobalProfile(_settings, _engine.Latest.Fans, (FanProfileMode)payload, _cfg.DemoMode);
                _fanCtrl.Tick(_settings, _engine.Latest, _cfg.DemoMode);
                if (!_cfg.DemoMode) _settings.Save();
                RebuildTrayMenu();
                _content.Invalidate();
            }
            else if (key == "curve_inc" && payload is int)
            {
                int idx = (int)payload;
                _settings.CurveDuties[idx] = FanController.ClampDuty(_settings.CurveDuties[idx] + 5);
                _settings.FanProfile = FanProfileMode.Custom;
                _fanCtrl.InvalidateCache();
                _fanCtrl.Tick(_settings, _engine.Latest, _cfg.DemoMode);
                if (!_cfg.DemoMode) _settings.Save();
                RebuildTrayMenu();
                _content.Invalidate();
            }
            else if (key == "curve_dec" && payload is int)
            {
                int idx = (int)payload;
                _settings.CurveDuties[idx] = FanController.ClampDuty(_settings.CurveDuties[idx] - 5);
                _settings.FanProfile = FanProfileMode.Custom;
                _fanCtrl.InvalidateCache();
                _fanCtrl.Tick(_settings, _engine.Latest, _cfg.DemoMode);
                if (!_cfg.DemoMode) _settings.Save();
                RebuildTrayMenu();
                _content.Invalidate();
            }
            else if (key == "fan_mode_0" || key == "fan_mode_1" || key == "fan_mode_2")
            {
                FanChannelInfo fc = payload as FanChannelInfo;
                if (fc != null)
                {
                    fc.Mode = key == "fan_mode_0" ? 0 : (key == "fan_mode_1" ? 1 : 2);
                    _settings.SavedFanModes[fc.Id] = fc.Mode;
                    _fanCtrl.Tick(_settings, _engine.Latest, _cfg.DemoMode);
                    if (!_cfg.DemoMode) _settings.Save();
                    _content.Invalidate();
                }
            }
            else if (key == "fan_inc" || key == "fan_dec")
            {
                FanChannelInfo fc = payload as FanChannelInfo;
                if (fc != null)
                {
                    fc.ManualDutyPct = FanController.ClampDuty(fc.ManualDutyPct + (key == "fan_inc" ? 5 : -5));
                    fc.Mode = 1;
                    _settings.SavedFanModes[fc.Id] = 1;
                    _settings.SavedFanManuals[fc.Id] = fc.ManualDutyPct;
                    _fanCtrl.Tick(_settings, _engine.Latest, _cfg.DemoMode);
                    if (!_cfg.DemoMode) _settings.Save();
                    _content.Invalidate();
                }
            }
            else if (key == "hud_toggle") ToggleOverlay();
            else if (key == "hud_pos" && payload is OverlayAnchor) { _settings.OverlayPosition = (OverlayAnchor)payload; _settings.OverlayCustomPos = false; RefreshHudIfVisible(); }
            else if (key == "hud_monitor" && payload is int) { _settings.OverlayMonitorIndex = (int)payload; _settings.OverlayCustomPos = false; RefreshHudIfVisible(); }
            else if (key == "hud_margin" && payload is int) { _settings.OverlayMargin = Math.Max(0, Math.Min(120, (int)payload)); _settings.OverlayCustomPos = false; RefreshHudIfVisible(); }
            else if (key == "hud_margin_dec") { _settings.OverlayMargin = Math.Max(0, _settings.OverlayMargin - 2); _settings.OverlayCustomPos = false; RefreshHudIfVisible(); }
            else if (key == "hud_margin_inc") { _settings.OverlayMargin = Math.Min(120, _settings.OverlayMargin + 2); _settings.OverlayCustomPos = false; RefreshHudIfVisible(); }
            else if (key == "hud_drag_toggle") SetHudDragUnlocked(!_hud.DragUnlocked);
            else if (key == "hud_layout" && payload is bool) { _settings.OverlayHorizontal = (bool)payload; RefreshHudIfVisible(); }
            else if (key == "hud_opacity" && payload is int) { _settings.OverlayOpacity = (int)payload; RefreshHudIfVisible(); }
            else if (key == "hud_scale" && payload is int) { _settings.OverlayScale = (int)payload; RefreshHudIfVisible(); }
            else if (key == "hud_preset" && payload is string) { _settings.ApplyHudPreset((string)payload); RefreshHudIfVisible(); }
            else if (key == "toggle_tray")
            {
                _settings.MinimizeToTray = !_settings.MinimizeToTray;
                if (!_cfg.DemoMode) _settings.Save();
                _content.Invalidate();
            }
            else if (key == "toggle_startup")
            {
                if (_cfg.DemoMode) _settings.StartWithWindows = !_settings.StartWithWindows;
                else
                {
                    WinNative.SetStartupEnabled(!_settings.StartWithWindows);
                    _settings.StartWithWindows = WinNative.IsStartupEnabled();
                }
                _content.Invalidate();
            }
            else if (key == "hud_metric" && payload is string)
            {
                string mk = (string)payload;
                if (mk == "HudShowFps") _settings.HudShowFps = !_settings.HudShowFps;
                else if (mk == "HudShowFps1Low") _settings.HudShowFps1Low = !_settings.HudShowFps1Low;
                else if (mk == "HudShowCpuTemp") _settings.HudShowCpuTemp = !_settings.HudShowCpuTemp;
                else if (mk == "HudShowCpuLoad") _settings.HudShowCpuLoad = !_settings.HudShowCpuLoad;
                else if (mk == "HudShowCpuClockPower") _settings.HudShowCpuClockPower = !_settings.HudShowCpuClockPower;
                else if (mk == "HudShowGpuTemp") _settings.HudShowGpuTemp = !_settings.HudShowGpuTemp;
                else if (mk == "HudShowGpuLoad") _settings.HudShowGpuLoad = !_settings.HudShowGpuLoad;
                else if (mk == "HudShowGpuVram") _settings.HudShowGpuVram = !_settings.HudShowGpuVram;
                else if (mk == "HudShowGpuFan") _settings.HudShowGpuFan = !_settings.HudShowGpuFan;
                else if (mk == "HudShowGpuPower") _settings.HudShowGpuPower = !_settings.HudShowGpuPower;
                else if (mk == "HudShowRam") _settings.HudShowRam = !_settings.HudShowRam;
                else if (mk == "HudShowCaseFans") _settings.HudShowCaseFans = !_settings.HudShowCaseFans;
                else if (mk == "HudShowBoardDiskTemp") _settings.HudShowBoardDiskTemp = !_settings.HudShowBoardDiskTemp;
                else if (mk == "HudShowNetClock") _settings.HudShowNetClock = !_settings.HudShowNetClock;
                RefreshHudIfVisible();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string found = "";
            for (int i = _chromeZones.Count - 1; i >= 0; i--)
                if (_chromeZones[i].Rect.Contains(e.Location)) { found = _chromeZones[i].Key; break; }
            if (found != _hoverKey)
            {
                _hoverKey = found;
                Cursor = !string.IsNullOrEmpty(found) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (!string.IsNullOrEmpty(_hoverKey)) { _hoverKey = ""; Cursor = Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            for (int i = _chromeZones.Count - 1; i >= 0; i--)
            {
                if (_chromeZones[i].Rect.Contains(e.Location))
                {
                    string k = _chromeZones[i].Key;
                    if (k == "lang_tr") SetLanguage("tr");
                    else if (k == "lang_en") SetLanguage("en");
                    else if (k == "lang_zh") SetLanguage("zh");
                    else if (k == "tab_0") SwitchTab(0);
                    else if (k == "tab_1") SwitchTab(1);
                    else if (k == "tab_2") SwitchTab(2);
                    else if (k == "tab_3") SwitchTab(3);
                    else if (k == "top_hud") ToggleOverlay();
                    else if (k == "elevate") { if (!_engine.Latest.IsAdmin && !_cfg.DemoMode && WinNative.RestartAsAdministrator("--handoff --tab " + _activeTab)) { _forceExit = true; Close(); } }
                    break;
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(ColBg);
            _chromeZones.Clear();

            float pad = Sf(20f);
            float w = ClientSize.Width;

            // Top Header: Pulse Emblem + Brand + Sensor Engine Pill
            DrawPulseEmblem(g, new RectangleF(pad, Sf(14f), Sf(22f), Sf(22f)));
            DrawStr(g, "COREPULSE", _fBrand, ColTextPrimary, new RectangleF(pad + Sf(29f), Sf(13f), Sf(116f), Sf(24f)), StringAlignment.Near);

            string engBadge = _engine.Latest.LhmLoaded
                ? (_engine.Latest.IsAdmin && _engine.Latest.PawnIoInstalled ? T("Full Sensors · PawnIO", "Tam Sensör · PawnIO", "完整传感器 · PawnIO") : T("Standard Sensors", "Standart Sensör", "基础传感器"))
                : T("Native Mode", "Yerli Mod", "原生模式");
            RectangleF engRc = new RectangleF(pad + Sf(148f), Sf(15f), Sf(154f), Sf(22f));
            DrawBox(g, engRc, Sf(6f), ColCard, ColBorder);
            DrawStr(g, engBadge, _fSmallPill, _engine.Latest.IsAdmin && _engine.Latest.PawnIoInstalled ? ColEmerald : ColTextSec, engRc, StringAlignment.Center);

            // Right: Language switcher + Admin badge + Quick Always-On-Top HUD ON/OFF pill
            RectangleF langPill = new RectangleF(w - pad - Sf(96f), Sf(14f), Sf(96f), Sf(23f));
            float wTr = Sf(29f), wEn = Sf(29f), wZh = langPill.Width - wTr - wEn;
            RectangleF rTr = new RectangleF(langPill.X, langPill.Y, wTr, langPill.Height);
            RectangleF rEn = new RectangleF(rTr.Right, langPill.Y, wEn, langPill.Height);
            RectangleF rZh = new RectangleF(rEn.Right, langPill.Y, wZh, langPill.Height);
            DrawBox(g, langPill, Sf(6f), ColCard, ColBorder);
            DrawStr(g, "TR", _fSmallPill, Lang == "tr" ? ColEmerald : ColTextMuted, rTr, StringAlignment.Center);
            DrawStr(g, "EN", _fSmallPill, Lang == "en" ? ColEmerald : ColTextMuted, rEn, StringAlignment.Center);
            DrawStr(g, "中文", _fLangZh, Lang == "zh" ? ColEmerald : ColTextMuted, rZh, StringAlignment.Center);
            using (Pen sep = new Pen(ColDivider, 1f))
            {
                g.DrawLine(sep, rTr.Right, langPill.Y + Sf(4f), rTr.Right, langPill.Bottom - Sf(4f));
                g.DrawLine(sep, rEn.Right, langPill.Y + Sf(4f), rEn.Right, langPill.Bottom - Sf(4f));
            }
            _chromeZones.Add(new HitZone(rTr, "lang_tr", null));
            _chromeZones.Add(new HitZone(rEn, "lang_en", null));
            _chromeZones.Add(new HitZone(rZh, "lang_zh", null));

            bool isAdm = _engine.Latest.IsAdmin;
            RectangleF admRc = new RectangleF(langPill.X - Sf(86f), Sf(14f), Sf(78f), Sf(23f));
            DrawBox(g, admRc, Sf(6f), isAdm ? Color.FromArgb(16, 46, 38) : ColCard, isAdm ? Color.FromArgb(36, 128, 96) : ColBorder);
            DrawStr(g, isAdm ? T("Admin", "Yönetici", "管理员") : T("Admin ↗", "Yönetici ↗", "管理员 ↗"), _fSmallPill, isAdm ? ColEmerald : ColAmber, admRc, StringAlignment.Center);
            if (!isAdm) _chromeZones.Add(new HitZone(admRc, "elevate", null));

            RectangleF hudPill = new RectangleF(admRc.X - Sf(158f), Sf(14f), Sf(150f), Sf(23f));
            bool hOn = _settings.OverlayEnabled;
            bool hHov = _hoverKey == "top_hud";
            DrawBox(g, hudPill, Sf(6f), hOn ? Color.FromArgb(16, 54, 42) : (hHov ? ColBtnHover : ColCard), hOn ? ColEmerald : (hHov ? ColCyan : ColBorder));
            DrawStr(g, hOn ? T("● Always On Top: ON", "● Üstte Göster: AÇIK", "● 悬浮窗：开启") : T("○ Always On Top: OFF", "○ Üstte Göster: KAPALI", "○ 悬浮窗：关闭"),
                _fSmallPill, hOn ? ColEmerald : ColTextSec, hudPill, StringAlignment.Center);
            _chromeZones.Add(new HitZone(hudPill, "top_hud", null));

            // 4-Tab Strip
            float ty = Sf(50f);
            float th = Sf(36f);
            RectangleF track = new RectangleF(pad, ty, Sf(596f), th);
            DrawBox(g, track, Sf(9f), Color.FromArgb(15, 20, 28), ColDivider);

            string[] tabs = new string[]
            {
                T("Dashboard", "Özet", "硬件总览"),
                T("Sensors", "Sensörler", "完整传感器"),
                T("Fan Control", "Fan Kontrol", "风扇控制"),
                T("Overlay (HUD)", "Üstte Göster (HUD)", "置顶悬浮窗")
            };
            float ip = Sf(3f);
            float tw = (track.Width - ip * 5f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                RectangleF tr = new RectangleF(track.X + ip + i * (tw + ip), track.Y + ip, tw, th - ip * 2f);
                bool act = (_activeTab == i);
                bool hov = _hoverKey == ("tab_" + i);
                if (act) DrawBox(g, tr, Sf(7f), Color.FromArgb(18, 46, 44), Color.FromArgb(40, 148, 120));
                else if (hov) DrawBox(g, tr, Sf(7f), Color.FromArgb(24, 31, 42), ColDivider);
                DrawStr(g, tabs[i], _fTab, act ? ColEmerald : (hov ? ColTextPrimary : ColTextSec), tr, StringAlignment.Center);
                _chromeZones.Add(new HitZone(tr, "tab_" + i, null));
            }

            // Footer status bar
            float fy = ClientSize.Height - Sf(30f);
            using (Pen dp = new Pen(ColDivider, 1f)) g.DrawLine(dp, 0, fy, w, fy);
            string foot = T(
                "CorePulse v1.2 · Anti-Cheat Safe DXGI/D3D9 ETW FPS · Thermal Guard ≥88°C · Tray & BIOS Auto-Restore on Exit.",
                "CorePulse v1.2 · Anti-Cheat Güvenli DXGI/D3D9 ETW FPS · Termal Koruma ≥88°C · Sistem Tepsisi ve Çıkışta BIOS Modu.",
                "CorePulse v1.2 · 零注入防封号 DXGI/D3D9 ETW 帧率 · ≥88°C 高温保护 · 系统托盘驻留与退出自动恢复 BIOS 温控。");
            DrawStr(g, foot, _fStatus, ColTextMuted, new RectangleF(pad, fy + Sf(4f), w - pad * 2f, Sf(22f)), StringAlignment.Near);
        }

        private static void DrawPulseEmblem(Graphics g, RectangleF r)
        {
            using (GraphicsPath bg = Rounded(r, r.Width * 0.28f))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(36, ColCyan)))
            using (Pen bd = new Pen(ColCyan, 1.5f)) { g.FillPath(fill, bg); g.DrawPath(bd, bg); }

            // Heartbeat / pulse line inside emblem
            PointF[] pts = new PointF[]
            {
                new PointF(r.X + r.Width * 0.14f, r.Y + r.Height * 0.56f),
                new PointF(r.X + r.Width * 0.34f, r.Y + r.Height * 0.56f),
                new PointF(r.X + r.Width * 0.44f, r.Y + r.Height * 0.24f),
                new PointF(r.X + r.Width * 0.56f, r.Y + r.Height * 0.78f),
                new PointF(r.X + r.Width * 0.68f, r.Y + r.Height * 0.52f),
                new PointF(r.X + r.Width * 0.86f, r.Y + r.Height * 0.52f)
            };
            using (Pen p = new Pen(ColEmerald, 2.0f)) { p.LineJoin = LineJoin.Round; p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLines(p, pts); }
        }

        private static void DrawStr(Graphics g, string text, Font f, Color col, RectangleF r, StringAlignment align)
        {
            using (SolidBrush b = new SolidBrush(col))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = align; sf.LineAlignment = StringAlignment.Center;
                sf.FormatFlags = StringFormatFlags.NoWrap; sf.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(text, f, b, r, sf);
            }
        }

        private static void DrawBox(Graphics g, RectangleF r, float rad, Color fill, Color bd)
        {
            using (GraphicsPath p = Rounded(r, rad))
            using (SolidBrush fb = new SolidBrush(fill))
            using (Pen bp = new Pen(bd, 1f)) { g.FillPath(fb, p); g.DrawPath(bp, p); }
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

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_forceExit && !_cfg.DemoMode && _settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            _shuttingDown = true;
            if (_pollTimer != null) { _pollTimer.Stop(); _pollTimer.Dispose(); }
            try { _pollIdle.WaitOne(1200); } catch { }
            try { _pollIdle.Close(); } catch { }
            if (!_cfg.DemoMode)
            {
                try { SystemEvents.PowerModeChanged -= OnPowerModeChanged; } catch { }
                try { SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; } catch { }
            }
            if (_trayIcon != null) { try { _trayIcon.Visible = false; _trayIcon.Dispose(); } catch { } _trayIcon = null; }
            if (_trayMenu != null) { try { _trayMenu.Dispose(); } catch { } _trayMenu = null; }
            if (_hotkeyRegistered && IsHandleCreated) WinNative.UnregisterHotKey(Handle, WinNative.HOTKEY_TOGGLE_HUD);
            try { _fanCtrl.RestoreAllToBiosAuto(_engine.Latest != null ? _engine.Latest.Fans : null, _cfg.DemoMode); } catch { }
            try { if (_hud != null && !_hud.IsDisposed) _hud.Close(); } catch { }
            try { _engine.Dispose(); } catch { }
            if (_appIcon != null) _appIcon.Dispose();
            DisposeFonts();
            base.OnFormClosing(e);
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            LibResolver.Install();
            LaunchConfig cfg = LaunchConfig.Parse(args);

            Mutex singleMutex = null;
            bool ownsMutex = false;
            if (!cfg.DemoMode)
            {
                bool createdNew;
                singleMutex = new Mutex(true, "Local\\CorePulse_SingleInstance_v1", out createdNew);
                ownsMutex = createdNew;
                if (!createdNew)
                {
                    if (cfg.Handoff)
                    {
                        try { ownsMutex = singleMutex.WaitOne(4000); }
                        catch (AbandonedMutexException) { ownsMutex = true; }
                    }
                    if (!ownsMutex)
                    {
                        // Wake existing running instance (even if minimized in tray or running as Admin!)
                        if (WinNative.WM_COREPULSE_SHOW != 0)
                            WinNative.PostMessage(WinNative.HWND_BROADCAST, WinNative.WM_COREPULSE_SHOW, IntPtr.Zero, IntPtr.Zero);
                        singleMutex.Dispose();
                        return;
                    }
                }
            }

            try
            {
                WinNative.EnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(cfg));
            }
            finally
            {
                if (singleMutex != null)
                {
                    if (ownsMutex) { try { singleMutex.ReleaseMutex(); } catch { } }
                    singleMutex.Dispose();
                }
            }
        }
    }
}
