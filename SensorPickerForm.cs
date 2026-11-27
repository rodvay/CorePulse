using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CorePulse
{
    internal sealed class HudActiveSensorDetail
    {
        public string RoleLabel;
        public string SensorName;
        public string HardwareName;
        public string ValueText;
        public Color ValueColor;
    }

    internal sealed class HudSensorOption
    {
        public string Key;
        public string Group;
        public string TypeLabel;
        public string Name;
        public string ShortName;
        public string HardwareName;
        public string CompactValue;
        public string DetailedValue;
        public string MinMaxText;
        public Color ValueColor;
        public float NumericValue;
        public bool HasNumeric;
    }

    internal static class HudSensorResolver
    {
        public const string SysFps = "__SYS__FPS";
        public const string SysFrametime = "__SYS__FRAMETIME";
        public const string SysFps1Low = "__SYS__FPS_1LOW";
        public const string SysCpuLoad = "__SYS__CPU_LOAD";
        public const string SysGpuVramUsed = "__SYS__GPU_VRAM_USED";
        public const string SysGpuVramPct = "__SYS__GPU_VRAM_PCT";
        public const string SysGpuFanPct = "__SYS__GPU_FAN_PCT";
        public const string SysRamUsed = "__SYS__RAM_USED";
        public const string SysRamPct = "__SYS__RAM_PCT";
        public const string SysNetDown = "__SYS__NET_DOWN";
        public const string SysNetUp = "__SYS__NET_UP";
        public const string SysClock = "__SYS__CLOCK";

        public static List<string> GetSelectedKeys(CorePulseSettings cfg, string metricKey)
        {
            List<string> list;
            if (cfg != null && cfg.HudCustomSensors != null && !string.IsNullOrEmpty(metricKey) &&
                cfg.HudCustomSensors.TryGetValue(metricKey, out list) && list != null && list.Count > 0)
            {
                return list;
            }
            return null;
        }

        public static bool HasCustomSensor(CorePulseSettings cfg, string metricKey)
        {
            List<string> list = GetSelectedKeys(cfg, metricKey);
            return list != null && list.Count > 0;
        }

        public static SensorReading FindByKey(TelemetrySnapshot snap, string key)
        {
            if (snap == null || snap.AllSensors == null || string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < snap.AllSensors.Count; i++)
            {
                SensorReading sr = snap.AllSensors[i];
                if (sr != null && string.Equals(sr.Key, key, StringComparison.OrdinalIgnoreCase))
                    return sr;
            }
            return null;
        }

        public static string ShortenName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            string s = name.Trim();
            if (s.Equals("Core (Tctl/Tdie)", StringComparison.OrdinalIgnoreCase)) return "Tctl/Tdie";
            s = s.Replace("GPU ", "").Replace("CPU ", "").Replace(" Drive Temp", "").Replace("Clocks ", "").Replace("Memory ", "Mem ");
            if (s.Length > 18) s = s.Substring(0, 18).TrimEnd();
            return s;
        }

        public static string FormatCompactValue(SensorReading sr)
        {
            if (sr == null) return "--";
            if (sr.TypeLabel == "Temp") return Loc.F("{0:0}°C", sr.Value);
            if (sr.TypeLabel == "Load" || sr.TypeLabel == "PWM") return Loc.F("{0:0}%", sr.Value);
            if (sr.TypeLabel == "Clock")
                return sr.Value >= 1000f ? Loc.F("{0:0.0}GHz", sr.Value / 1000f) : Loc.F("{0:0}MHz", sr.Value);
            if (sr.TypeLabel == "Fan") return Loc.F("{0:0} RPM", sr.Value);
            if (sr.TypeLabel == "Power") return Loc.F("{0:0}W", sr.Value);
            if (sr.TypeLabel == "Volt") return Loc.F("{0:0.00}V", sr.Value);
            if (sr.TypeLabel == "VRAM") return Loc.F("{0:0.0}GB", sr.Value);
            return Loc.F("{0:0.0}{1}", sr.Value, sr.Unit ?? "");
        }

        public static string FormatDetailedValue(SensorReading sr)
        {
            if (sr == null) return "--";
            if (sr.TypeLabel == "Fan" || sr.TypeLabel == "Clock")
                return Loc.F("{0:0} {1}", sr.Value, sr.Unit ?? "");
            if (sr.TypeLabel == "Volt")
                return Loc.F("{0:0.000} {1}", sr.Value, sr.Unit ?? "");
            return Loc.F("{0:0.0} {1}", sr.Value, sr.Unit ?? "");
        }

        public static string FormatMinMax(SensorReading sr)
        {
            if (sr == null) return "";
            if (sr.TypeLabel == "Fan" || sr.TypeLabel == "Clock")
                return Loc.F("Min {0:0} · Max {1:0}", sr.Min, sr.Max);
            if (sr.TypeLabel == "Volt")
                return Loc.F("Min {0:0.00} · Max {1:0.00}", sr.Min, sr.Max);
            return Loc.F("Min {0:0.0} · Max {1:0.0}", sr.Min, sr.Max);
        }

        public static Color GetValueColor(SensorReading sr)
        {
            if (sr == null) return PulseContentPanel.ColTextSec;
            if (sr.TypeLabel == "Temp") return OverlayHudForm.TempColor(sr.Value);
            if (sr.TypeLabel == "Fan" || sr.TypeLabel == "PWM" || sr.TypeLabel == "VRAM") return PulseContentPanel.ColCyan;
            if (sr.TypeLabel == "Power") return PulseContentPanel.ColAmber;
            if (sr.TypeLabel == "Clock") return PulseContentPanel.ColEmerald;
            return PulseContentPanel.ColTextPrimary;
        }

        private static HudSensorOption FromSensorReading(SensorReading sr)
        {
            return new HudSensorOption
            {
                Key = sr.Key,
                Group = sr.Group ?? "",
                TypeLabel = sr.TypeLabel ?? "",
                Name = sr.Name ?? "",
                ShortName = ShortenName(sr.Name),
                HardwareName = sr.HardwareName ?? "",
                CompactValue = FormatCompactValue(sr),
                DetailedValue = FormatDetailedValue(sr),
                MinMaxText = FormatMinMax(sr),
                ValueColor = GetValueColor(sr),
                NumericValue = sr.Value,
                HasNumeric = true
            };
        }

        private static HudSensorOption BuildVirtualOption(string key, TelemetrySnapshot s, string lang)
        {
            s = s ?? new TelemetrySnapshot();
            if (key == SysFps)
            {
                string val = s.Fps > 0.5f ? Loc.F("{0:0}", s.Fps) : (s.FpsEtwActive ? "--" : "ADMIN");
                string det = s.Fps > 0.5f ? Loc.F("{0:0} FPS", s.Fps) : (s.FpsEtwActive ? "--" : Loc.T(lang, "Requires Admin", "Yönetici Gerekli", "需管理员权限"));
                return new HudSensorOption
                {
                    Key = SysFps, Group = "ETW", TypeLabel = "FPS",
                    Name = Loc.T(lang, "Real-Time FPS (DXGI / D3D9)", "Gerçek Zamanlı FPS (DXGI / D3D9)", "实时帧率 (DXGI / D3D9 FPS)"),
                    ShortName = "FPS",
                    HardwareName = !string.IsNullOrEmpty(s.ActiveGameProcess) ? s.ActiveGameProcess : "Windows ETW Engine",
                    CompactValue = val, DetailedValue = det, MinMaxText = "QPC Present Events",
                    ValueColor = s.Fps >= 60f ? PulseContentPanel.ColEmerald : (s.Fps >= 30f ? PulseContentPanel.ColAmber : PulseContentPanel.ColTextSec),
                    NumericValue = s.Fps, HasNumeric = s.Fps > 0.5f
                };
            }
            if (key == SysFrametime)
            {
                string cVal = s.Fps > 0.5f ? Loc.F("{0:0.0}ms", s.FrametimeMs) : "--ms";
                string dVal = s.Fps > 0.5f ? Loc.F("{0:0.0} ms", s.FrametimeMs) : "-- ms";
                return new HudSensorOption
                {
                    Key = SysFrametime, Group = "ETW", TypeLabel = "Frametime",
                    Name = Loc.T(lang, "Frame Time (ms)", "Kare Süresi (ms)", "帧时间 (Frametime ms)"),
                    ShortName = "FT",
                    HardwareName = !string.IsNullOrEmpty(s.ActiveGameProcess) ? s.ActiveGameProcess : "Windows ETW Engine",
                    CompactValue = cVal, DetailedValue = dVal, MinMaxText = "QPC Frame Interval",
                    ValueColor = PulseContentPanel.ColTextPrimary,
                    NumericValue = s.FrametimeMs, HasNumeric = s.Fps > 0.5f
                };
            }
            if (key == SysFps1Low)
            {
                string cVal = s.Fps > 0.5f ? Loc.F("{0:0}", s.Fps1Low) : "--";
                string dVal = s.Fps > 0.5f ? Loc.F("{0:0} FPS", s.Fps1Low) : "--";
                return new HudSensorOption
                {
                    Key = SysFps1Low, Group = "ETW", TypeLabel = "1% Low",
                    Name = Loc.T(lang, "1% Low FPS", "%1 Low FPS", "1% Low 最低帧率"),
                    ShortName = "1% Low",
                    HardwareName = !string.IsNullOrEmpty(s.ActiveGameProcess) ? s.ActiveGameProcess : "Windows ETW Engine",
                    CompactValue = cVal, DetailedValue = dVal, MinMaxText = "99th Percentile Frametime",
                    ValueColor = s.Fps1Low >= 45f ? PulseContentPanel.ColEmerald : PulseContentPanel.ColAmber,
                    NumericValue = s.Fps1Low, HasNumeric = s.Fps > 0.5f
                };
            }
            if (key == SysCpuLoad)
            {
                return new HudSensorOption
                {
                    Key = SysCpuLoad, Group = "CPU", TypeLabel = "Load",
                    Name = Loc.T(lang, "CPU Total Usage", "CPU Toplam Kullanım", "CPU 总占用率"),
                    ShortName = "Total",
                    HardwareName = s.CpuName,
                    CompactValue = Loc.F("{0:0}%", s.CpuLoadPct),
                    DetailedValue = Loc.F("{0:0.0} %", s.CpuLoadPct),
                    MinMaxText = "System Performance Counter",
                    ValueColor = PulseContentPanel.ColCyan,
                    NumericValue = s.CpuLoadPct, HasNumeric = true
                };
            }
            if (key == SysGpuVramUsed)
            {
                string cVal = s.GpuVramUsedGb >= 0f ? Loc.F("{0:0.0}GB", s.GpuVramUsedGb) : "--GB";
                string dVal = s.GpuVramUsedGb >= 0f
                    ? (s.GpuVramTotalGb > 0f ? Loc.F("{0:0.0} / {1:0.0} GB", s.GpuVramUsedGb, s.GpuVramTotalGb) : Loc.F("{0:0.0} GB", s.GpuVramUsedGb))
                    : "-- GB";
                return new HudSensorOption
                {
                    Key = SysGpuVramUsed, Group = "GPU", TypeLabel = "VRAM",
                    Name = Loc.T(lang, "Dedicated VRAM Used (GB)", "Adanmış VRAM Kullanımı (GB)", "专用显存已用容量 (GB)"),
                    ShortName = "VRAM",
                    HardwareName = s.GpuName,
                    CompactValue = cVal, DetailedValue = dVal,
                    MinMaxText = s.GpuVramTotalGb > 0f ? Loc.F("Total {0:0.0} GB", s.GpuVramTotalGb) : "Dedicated Memory",
                    ValueColor = PulseContentPanel.ColCyan,
                    NumericValue = s.GpuVramUsedGb, HasNumeric = s.GpuVramUsedGb >= 0f
                };
            }
            if (key == SysGpuVramPct)
            {
                bool ok = s.GpuVramUsedGb >= 0f && s.GpuVramTotalGb > 0.1f;
                float pct = ok ? Math.Max(0f, Math.Min(100f, (s.GpuVramUsedGb / s.GpuVramTotalGb) * 100f)) : 0f;
                return new HudSensorOption
                {
                    Key = SysGpuVramPct, Group = "GPU", TypeLabel = "Load",
                    Name = Loc.T(lang, "VRAM Usage (%)", "VRAM Kullanım Oranı (%)", "显存占用百分比 (%)"),
                    ShortName = "VRAM%",
                    HardwareName = s.GpuName,
                    CompactValue = ok ? Loc.F("{0:0}%", pct) : "--%",
                    DetailedValue = ok ? Loc.F("{0:0.0} %", pct) : "-- %",
                    MinMaxText = ok ? Loc.F("{0:0.0} / {1:0.0} GB", s.GpuVramUsedGb, s.GpuVramTotalGb) : "Dedicated Memory Ratio",
                    ValueColor = pct >= 88f ? PulseContentPanel.ColAmber : PulseContentPanel.ColCyan,
                    NumericValue = pct, HasNumeric = ok
                };
            }
            if (key == SysGpuFanPct)
            {
                bool ok = s.GpuFanPct >= 0f;
                return new HudSensorOption
                {
                    Key = SysGpuFanPct, Group = "GPU", TypeLabel = "PWM",
                    Name = Loc.T(lang, "GPU Fan Duty (%)", "GPU Fan Yüzdesi (%)", "GPU 风扇占空比 (%)"),
                    ShortName = "FAN%",
                    HardwareName = s.GpuName,
                    CompactValue = ok ? Loc.F("FAN {0:0}%", s.GpuFanPct) : "--%",
                    DetailedValue = ok ? Loc.F("{0:0} %", s.GpuFanPct) : "-- %",
                    MinMaxText = "GPU Fan PWM Duty",
                    ValueColor = PulseContentPanel.ColCyan,
                    NumericValue = s.GpuFanPct, HasNumeric = ok
                };
            }
            if (key == SysRamUsed)
            {
                bool ok = s.RamTotalGb > 0f;
                return new HudSensorOption
                {
                    Key = SysRamUsed, Group = "Memory", TypeLabel = "RAM",
                    Name = Loc.T(lang, "Physical RAM Used (GB)", "Kullanılan RAM (GB)", "物理内存已用/总容量 (GB)"),
                    ShortName = "Used",
                    HardwareName = !string.IsNullOrEmpty(s.RamSpecText) ? s.RamSpecText : "System Memory",
                    CompactValue = ok ? Loc.F("{0:0.0}/{1:0}GB", s.RamUsedGb, s.RamTotalGb) : "--GB",
                    DetailedValue = ok ? Loc.F("{0:0.0} / {1:0.0} GB", s.RamUsedGb, s.RamTotalGb) : "-- GB",
                    MinMaxText = "Win32 GlobalMemoryStatusEx",
                    ValueColor = PulseContentPanel.ColViolet,
                    NumericValue = s.RamUsedGb, HasNumeric = ok
                };
            }
            if (key == SysRamPct)
            {
                bool ok = s.RamTotalGb > 0f;
                return new HudSensorOption
                {
                    Key = SysRamPct, Group = "Memory", TypeLabel = "Load",
                    Name = Loc.T(lang, "RAM Usage (%)", "RAM Kullanım Oranı (%)", "内存占用率 (%)"),
                    ShortName = "Load",
                    HardwareName = !string.IsNullOrEmpty(s.RamSpecText) ? s.RamSpecText : "System Memory",
                    CompactValue = ok ? Loc.F("{0:0}%", s.RamLoadPct) : "--%",
                    DetailedValue = ok ? Loc.F("{0:0.0} %", s.RamLoadPct) : "-- %",
                    MinMaxText = "Memory Load",
                    ValueColor = s.RamLoadPct >= 88f ? PulseContentPanel.ColAmber : PulseContentPanel.ColViolet,
                    NumericValue = s.RamLoadPct, HasNumeric = ok
                };
            }
            if (key == SysNetDown)
            {
                return new HudSensorOption
                {
                    Key = SysNetDown, Group = "Network", TypeLabel = "NetDown",
                    Name = Loc.T(lang, "Download Speed (↓ Mbps)", "İndirme Hızı (↓ Mbps)", "网络下行速率 (↓ Mbps)"),
                    ShortName = "↓",
                    HardwareName = "IPv4 Network Interfaces",
                    CompactValue = Loc.F("↓{0:0.0}", s.NetDownMbps),
                    DetailedValue = Loc.F("↓ {0:0.0} Mbps", s.NetDownMbps),
                    MinMaxText = "Active Interfaces",
                    ValueColor = PulseContentPanel.ColCyan,
                    NumericValue = s.NetDownMbps, HasNumeric = true
                };
            }
            if (key == SysNetUp)
            {
                return new HudSensorOption
                {
                    Key = SysNetUp, Group = "Network", TypeLabel = "NetUp",
                    Name = Loc.T(lang, "Upload Speed (↑ Mbps)", "Yükleme Hızı (↑ Mbps)", "网络上行速率 (↑ Mbps)"),
                    ShortName = "↑",
                    HardwareName = "IPv4 Network Interfaces",
                    CompactValue = Loc.F("↑{0:0.0}", s.NetUpMbps),
                    DetailedValue = Loc.F("↑ {0:0.0} Mbps", s.NetUpMbps),
                    MinMaxText = "Active Interfaces",
                    ValueColor = PulseContentPanel.ColCyan,
                    NumericValue = s.NetUpMbps, HasNumeric = true
                };
            }
            if (key == SysClock)
            {
                string tm = DateTime.Now.ToString("HH:mm");
                return new HudSensorOption
                {
                    Key = SysClock, Group = "Clock", TypeLabel = "Time",
                    Name = Loc.T(lang, "System Clock (HH:mm)", "Sistem Saati (HH:mm)", "系统时间 (HH:mm)"),
                    ShortName = "Time",
                    HardwareName = "Local System Time",
                    CompactValue = tm, DetailedValue = tm,
                    MinMaxText = DateTime.Now.ToString("yyyy-MM-dd"),
                    ValueColor = PulseContentPanel.ColTextPrimary,
                    NumericValue = 0f, HasNumeric = false
                };
            }
            return null;
        }

        public static HudSensorOption ResolveOption(TelemetrySnapshot snap, string sensorKey, string lang)
        {
            if (string.IsNullOrEmpty(sensorKey)) return null;
            if (sensorKey.StartsWith("__SYS__", StringComparison.Ordinal))
                return BuildVirtualOption(sensorKey, snap, lang);
            SensorReading sr = FindByKey(snap, sensorKey);
            return sr != null ? FromSensorReading(sr) : null;
        }

        public static List<HudSensorOption> GetZoneOptions(string metricKey, TelemetrySnapshot snap, string lang)
        {
            List<HudSensorOption> res = new List<HudSensorOption>();
            TelemetrySnapshot s = snap ?? new TelemetrySnapshot();
            List<SensorReading> all = s.AllSensors ?? new List<SensorReading>();

            if (metricKey == "HudShowFps")
            {
                res.Add(BuildVirtualOption(SysFps, s, lang));
                res.Add(BuildVirtualOption(SysFrametime, s, lang));
            }
            else if (metricKey == "HudShowFps1Low")
            {
                res.Add(BuildVirtualOption(SysFps1Low, s, lang));
            }
            else if (metricKey == "HudShowCpuTemp")
            {
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Group == "CPU" && all[i].TypeLabel == "Temp") res.Add(FromSensorReading(all[i]));
            }
            else if (metricKey == "HudShowCpuLoad")
            {
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Group == "CPU" && all[i].TypeLabel == "Load") res.Add(FromSensorReading(all[i]));
                if (res.Count == 0) res.Add(BuildVirtualOption(SysCpuLoad, s, lang));
            }
            else if (metricKey == "HudShowCpuClockPower")
            {
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Group == "CPU" && (all[i].TypeLabel == "Clock" || all[i].TypeLabel == "Power"))
                        res.Add(FromSensorReading(all[i]));
            }
            else if (metricKey == "HudShowGpuTemp")
            {
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Group == "GPU" && all[i].TypeLabel == "Temp") res.Add(FromSensorReading(all[i]));
            }
            else if (metricKey == "HudShowGpuLoad")
            {
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Group == "GPU" && all[i].TypeLabel == "Load") res.Add(FromSensorReading(all[i]));
            }
            else if (metricKey == "HudShowGpuVram")
            {
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Group == "GPU" && all[i].TypeLabel == "VRAM") res.Add(FromSensorReading(all[i]));
                if (res.Count == 0) res.Add(BuildVirtualOption(SysGpuVramUsed, s, lang));
                res.Add(BuildVirtualOption(SysGpuVramPct, s, lang));
            }
            else if (metricKey == "HudShowGpuFan")
            {
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Group == "GPU" && (all[i].TypeLabel == "Fan" || all[i].TypeLabel == "PWM"))
                        res.Add(FromSensorReading(all[i]));
                if (res.Count == 0 && s.GpuFanPct >= 0f) res.Add(BuildVirtualOption(SysGpuFanPct, s, lang));
            }
            else if (metricKey == "HudShowGpuPower")
            {
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Group == "GPU" && all[i].TypeLabel == "Power") res.Add(FromSensorReading(all[i]));
            }
            else if (metricKey == "HudShowRam")
            {
                res.Add(BuildVirtualOption(SysRamUsed, s, lang));
                res.Add(BuildVirtualOption(SysRamPct, s, lang));
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Group == "Memory") res.Add(FromSensorReading(all[i]));
            }
            else if (metricKey == "HudShowCaseFans")
            {
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Group == "Motherboard" && (all[i].TypeLabel == "Fan" || all[i].TypeLabel == "PWM"))
                        res.Add(FromSensorReading(all[i]));
            }
            else if (metricKey == "HudShowBoardDiskTemp")
            {
                for (int i = 0; i < all.Count; i++)
                    if ((all[i].Group == "Motherboard" || all[i].Group == "Storage") && all[i].TypeLabel == "Temp")
                        res.Add(FromSensorReading(all[i]));
            }
            else if (metricKey == "HudShowNetClock")
            {
                res.Add(BuildVirtualOption(SysNetDown, s, lang));
                res.Add(BuildVirtualOption(SysNetUp, s, lang));
                res.Add(BuildVirtualOption(SysClock, s, lang));
                for (int i = 0; i < all.Count; i++)
                    if (all[i].Group == "Network") res.Add(FromSensorReading(all[i]));
            }

            return res;
        }

        public static List<HudSensorOption> ResolveSelectedOptions(CorePulseSettings cfg, TelemetrySnapshot snap, string metricKey)
        {
            List<string> keys = GetSelectedKeys(cfg, metricKey);
            if (keys == null || keys.Count == 0) return null;
            List<HudSensorOption> list = new List<HudSensorOption>();
            string lang = cfg != null ? cfg.Language : "en";
            for (int i = 0; i < keys.Count; i++)
            {
                HudSensorOption opt = ResolveOption(snap, keys[i], lang);
                if (opt != null) list.Add(opt);
            }
            return list;
        }

        public static string FormatSelectedForHud(List<HudSensorOption> opts, out Color suggestedColor)
        {
            suggestedColor = PulseContentPanel.ColTextPrimary;
            if (opts == null || opts.Count == 0)
            {
                suggestedColor = PulseContentPanel.ColTextSec;
                return "--";
            }

            bool hasTemp = false;
            float maxTemp = -999f;
            Dictionary<string, int> typeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < opts.Count; i++)
            {
                string tp = opts[i].TypeLabel ?? "";
                int c;
                typeCounts.TryGetValue(tp, out c);
                typeCounts[tp] = c + 1;
                if (tp == "Temp" && opts[i].HasNumeric && opts[i].NumericValue > maxTemp)
                {
                    hasTemp = true;
                    maxTemp = opts[i].NumericValue;
                }
            }

            if (hasTemp) suggestedColor = OverlayHudForm.TempColor(maxTemp);
            else if (opts.Count == 1) suggestedColor = opts[0].ValueColor;

            if (opts.Count == 1) return opts[0].CompactValue;

            List<string> parts = new List<string>(opts.Count);
            for (int i = 0; i < opts.Count; i++)
            {
                HudSensorOption o = opts[i];
                int cnt;
                typeCounts.TryGetValue(o.TypeLabel ?? "", out cnt);
                if (cnt > 1 && !string.IsNullOrEmpty(o.ShortName))
                    parts.Add(o.ShortName + " " + o.CompactValue);
                else
                    parts.Add(o.CompactValue);
            }
            return string.Join(" · ", parts.ToArray());
        }

        private static SensorReading FindAutoSensorByValue(TelemetrySnapshot snap, string grp, string tp, float targetVal)
        {
            if (snap != null && snap.AllSensors != null && targetVal > 0f)
            {
                for (int i = 0; i < snap.AllSensors.Count; i++)
                {
                    SensorReading sr = snap.AllSensors[i];
                    if (sr.Group == grp && sr.TypeLabel == tp && Math.Abs(sr.Value - targetVal) < 0.05f)
                        return sr;
                }
            }
            return FindAutoSensor(snap, grp, tp, null);
        }

        private static SensorReading FindAutoSensor(TelemetrySnapshot snap, string grp, string tp, string[] preferredSubstrings)
        {
            if (snap == null || snap.AllSensors == null) return null;
            SensorReading fallback = null;
            if (preferredSubstrings != null)
            {
                for (int p = 0; p < preferredSubstrings.Length; p++)
                {
                    string kw = preferredSubstrings[p];
                    for (int i = 0; i < snap.AllSensors.Count; i++)
                    {
                        SensorReading sr = snap.AllSensors[i];
                        if (sr.Group == grp && sr.TypeLabel == tp &&
                            sr.Name != null && sr.Name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0)
                            return sr;
                    }
                }
            }
            for (int i = 0; i < snap.AllSensors.Count; i++)
            {
                SensorReading sr = snap.AllSensors[i];
                if (sr.Group == grp && sr.TypeLabel == tp)
                {
                    if (fallback == null) fallback = sr;
                }
            }
            return fallback;
        }

        public static List<HudActiveSensorDetail> DescribeActiveSensors(string metricKey, CorePulseSettings cfg, TelemetrySnapshot snap, string lang)
        {
            List<HudActiveSensorDetail> list = new List<HudActiveSensorDetail>();
            TelemetrySnapshot s = snap ?? new TelemetrySnapshot();

            List<string> selectedKeys = GetSelectedKeys(cfg, metricKey);
            if (selectedKeys != null && selectedKeys.Count > 0)
            {
                for (int i = 0; i < selectedKeys.Count; i++)
                {
                    string sk = selectedKeys[i];
                    HudSensorOption opt = ResolveOption(s, sk, lang);
                    if (opt != null)
                    {
                        list.Add(new HudActiveSensorDetail
                        {
                            RoleLabel = !string.IsNullOrEmpty(opt.TypeLabel) ? opt.TypeLabel : Loc.T(lang, "Custom", "Özel", "已选"),
                            SensorName = opt.Name,
                            HardwareName = opt.HardwareName,
                            ValueText = opt.DetailedValue,
                            ValueColor = opt.ValueColor
                        });
                    }
                    else
                    {
                        string[] parts = sk.Split('|');
                        string sName = parts.Length >= 4 ? parts[3] : sk;
                        string hwName = parts.Length >= 2 ? parts[1] : "";
                        list.Add(new HudActiveSensorDetail
                        {
                            RoleLabel = Loc.T(lang, "Waiting", "Bekleniyor", "等待读数"),
                            SensorName = sName,
                            HardwareName = hwName,
                            ValueText = "--",
                            ValueColor = PulseContentPanel.ColTextSec
                        });
                    }
                }
                return list;
            }

            if (metricKey == "HudShowFps")
            {
                string val = s.Fps > 0.5f
                    ? Loc.F("{0:0} FPS · {1:0.0} ms", s.Fps, s.FrametimeMs)
                    : (s.FpsEtwActive ? Loc.T(lang, "Waiting for 3D game...", "3D oyun bekleniyor...", "等待 3D 游戏呈现...") : Loc.T(lang, "Requires Admin", "Yönetici Gerekli", "需管理员权限"));
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = "ETW",
                    SensorName = "DXGI + D3D9 Present QPC",
                    HardwareName = !string.IsNullOrEmpty(s.ActiveGameProcess) ? s.ActiveGameProcess : "Windows ETW Engine",
                    ValueText = val,
                    ValueColor = s.Fps > 0.5f ? PulseContentPanel.ColEmerald : PulseContentPanel.ColTextSec
                });
            }
            else if (metricKey == "HudShowFps1Low")
            {
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = "ETW",
                    SensorName = "1% Low Frametime Percentile",
                    HardwareName = !string.IsNullOrEmpty(s.ActiveGameProcess) ? s.ActiveGameProcess : "Windows ETW Engine",
                    ValueText = s.Fps > 0.5f ? Loc.F("{0:0} FPS", s.Fps1Low) : "--",
                    ValueColor = s.Fps1Low >= 45f ? PulseContentPanel.ColEmerald : PulseContentPanel.ColAmber
                });
            }
            else if (metricKey == "HudShowCpuTemp")
            {
                SensorReading sr = FindAutoSensor(s, "CPU", "Temp", new string[] { "Package", "Tctl", "Tdie" });
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = Loc.T(lang, "Auto", "Oto", "默认"),
                    SensorName = sr != null ? sr.Name : "CPU Temperature",
                    HardwareName = sr != null ? sr.HardwareName : s.CpuName,
                    ValueText = sr != null ? FormatDetailedValue(sr) : (s.CpuTempC > 0f ? Loc.F("{0:0.0} °C", s.CpuTempC) : "-- °C"),
                    ValueColor = s.CpuTempC > 0f ? OverlayHudForm.TempColor(s.CpuTempC) : PulseContentPanel.ColTextSec
                });
            }
            else if (metricKey == "HudShowCpuLoad")
            {
                SensorReading sr = FindAutoSensor(s, "CPU", "Load", new string[] { "CPU Total" });
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = Loc.T(lang, "Auto", "Oto", "默认"),
                    SensorName = sr != null ? sr.Name : "CPU Total",
                    HardwareName = sr != null ? sr.HardwareName : s.CpuName,
                    ValueText = sr != null ? FormatDetailedValue(sr) : Loc.F("{0:0.0} %", s.CpuLoadPct),
                    ValueColor = PulseContentPanel.ColCyan
                });
            }
            else if (metricKey == "HudShowCpuClockPower")
            {
                SensorReading clk = FindAutoSensor(s, "CPU", "Clock", new string[] { "Average", "Avg", "Core #1" });
                SensorReading pwr = FindAutoSensor(s, "CPU", "Power", new string[] { "Package" });
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = Loc.T(lang, "Clock", "Saat", "频率"),
                    SensorName = clk != null ? clk.Name : "Core Clock",
                    HardwareName = clk != null ? clk.HardwareName : s.CpuName,
                    ValueText = s.CpuClockMhz > 100f ? Loc.F("{0:0} MHz", s.CpuClockMhz) : "-- MHz",
                    ValueColor = PulseContentPanel.ColEmerald
                });
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = Loc.T(lang, "Power", "Güç", "功耗"),
                    SensorName = pwr != null ? pwr.Name : "CPU Package Power",
                    HardwareName = pwr != null ? pwr.HardwareName : s.CpuName,
                    ValueText = s.CpuPowerW > 1f ? Loc.F("{0:0.0} W", s.CpuPowerW) : "-- W",
                    ValueColor = PulseContentPanel.ColAmber
                });
            }
            else if (metricKey == "HudShowGpuTemp")
            {
                SensorReading sr = FindAutoSensor(s, "GPU", "Temp", new string[] { "GPU Core", "Core" });
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = Loc.T(lang, "Auto", "Oto", "默认"),
                    SensorName = sr != null ? sr.Name : "GPU Core",
                    HardwareName = sr != null ? sr.HardwareName : s.GpuName,
                    ValueText = sr != null ? FormatDetailedValue(sr) : (s.GpuTempC > 0f ? Loc.F("{0:0.0} °C", s.GpuTempC) : "-- °C"),
                    ValueColor = s.GpuTempC > 0f ? OverlayHudForm.TempColor(s.GpuTempC) : PulseContentPanel.ColTextSec
                });
            }
            else if (metricKey == "HudShowGpuLoad")
            {
                SensorReading sr = FindAutoSensor(s, "GPU", "Load", new string[] { "GPU Core", "D3D 3D" });
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = Loc.T(lang, "Auto", "Oto", "默认"),
                    SensorName = sr != null ? sr.Name : "GPU Core",
                    HardwareName = sr != null ? sr.HardwareName : s.GpuName,
                    ValueText = s.GpuLoadPct >= 0f ? Loc.F("{0:0.0} %", s.GpuLoadPct) : "-- %",
                    ValueColor = PulseContentPanel.ColEmerald
                });
            }
            else if (metricKey == "HudShowGpuVram")
            {
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = "VRAM",
                    SensorName = "Dedicated GPU Memory Used",
                    HardwareName = s.GpuName,
                    ValueText = s.GpuVramUsedGb >= 0f
                        ? (s.GpuVramTotalGb > 0f ? Loc.F("{0:0.0} / {1:0.0} GB", s.GpuVramUsedGb, s.GpuVramTotalGb) : Loc.F("{0:0.0} GB", s.GpuVramUsedGb))
                        : "-- GB",
                    ValueColor = PulseContentPanel.ColCyan
                });
            }
            else if (metricKey == "HudShowGpuFan")
            {
                SensorReading fan = FindAutoSensor(s, "GPU", "Fan", new string[] { "GPU Fan", "Fan" });
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = Loc.T(lang, "Auto", "Oto", "默认"),
                    SensorName = fan != null ? fan.Name : "GPU Fan",
                    HardwareName = fan != null ? fan.HardwareName : s.GpuName,
                    ValueText = s.GpuFanRpm > 0f ? Loc.F("{0:0} RPM", s.GpuFanRpm) : (s.GpuFanPct >= 0f ? Loc.F("{0:0} %", s.GpuFanPct) : "-- RPM"),
                    ValueColor = PulseContentPanel.ColCyan
                });
            }
            else if (metricKey == "HudShowGpuPower")
            {
                SensorReading pwr = FindAutoSensor(s, "GPU", "Power", new string[] { "Board", "Package", "Total" });
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = Loc.T(lang, "Auto", "Oto", "默认"),
                    SensorName = pwr != null ? pwr.Name : "GPU Power",
                    HardwareName = pwr != null ? pwr.HardwareName : s.GpuName,
                    ValueText = s.GpuPowerW > 1f ? Loc.F("{0:0.0} W", s.GpuPowerW) : "-- W",
                    ValueColor = PulseContentPanel.ColAmber
                });
            }
            else if (metricKey == "HudShowRam")
            {
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = "Win32",
                    SensorName = "Physical Memory (GlobalMemoryStatusEx)",
                    HardwareName = !string.IsNullOrEmpty(s.RamSpecText) ? s.RamSpecText : "System RAM",
                    ValueText = s.RamTotalGb > 0f ? Loc.F("{0:0.0}/{1:0} GB ({2:0}%)", s.RamUsedGb, s.RamTotalGb, s.RamLoadPct) : "--",
                    ValueColor = PulseContentPanel.ColViolet
                });
            }
            else if (metricKey == "HudShowCaseFans")
            {
                SensorReading fan = FindAutoSensor(s, "Motherboard", "Fan", new string[] { "Chassis", "CPU Fan", "Fan" });
                if (fan == null) fan = FindAutoSensor(s, "GPU", "Fan", null);
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = Loc.T(lang, "Auto", "Oto", "默认"),
                    SensorName = fan != null ? fan.Name : "Primary Case / CPU Fan",
                    HardwareName = fan != null ? fan.HardwareName : s.MotherboardName,
                    ValueText = s.PrimaryCaseFanRpm > 0f ? Loc.F("{0:0} RPM", s.PrimaryCaseFanRpm) : (s.GpuFanRpm > 0f ? Loc.F("{0:0} RPM", s.GpuFanRpm) : "-- RPM"),
                    ValueColor = PulseContentPanel.ColCyan
                });
            }
            else if (metricKey == "HudShowBoardDiskTemp")
            {
                SensorReading mb = FindAutoSensorByValue(s, "Motherboard", "Temp", s.BoardTempC);
                SensorReading ssd = FindAutoSensorByValue(s, "Storage", "Temp", s.StorageTempC);
                float mbVal = mb != null ? mb.Value : s.BoardTempC;
                float ssdVal = ssd != null ? ssd.Value : s.StorageTempC;
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = Loc.T(lang, "Board", "Anakart", "主板"),
                    SensorName = mb != null ? mb.Name : "Motherboard Temp",
                    HardwareName = mb != null ? mb.HardwareName : s.MotherboardName,
                    ValueText = mbVal > 0f ? Loc.F("{0:0.0} °C", mbVal) : "-- °C",
                    ValueColor = mbVal > 0f ? OverlayHudForm.TempColor(mbVal) : PulseContentPanel.ColTextSec
                });
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = "SSD",
                    SensorName = ssd != null ? ssd.Name : "Storage Temp",
                    HardwareName = ssd != null ? ssd.HardwareName : (!string.IsNullOrEmpty(s.StorageName) ? s.StorageName : "Storage"),
                    ValueText = ssdVal > 0f ? Loc.F("{0:0.0} °C", ssdVal) : "-- °C",
                    ValueColor = ssdVal > 0f ? OverlayHudForm.TempColor(ssdVal) : PulseContentPanel.ColTextSec
                });
            }
            else if (metricKey == "HudShowNetClock")
            {
                list.Add(new HudActiveSensorDetail
                {
                    RoleLabel = "NET",
                    SensorName = "Active Network Interfaces + System Clock",
                    HardwareName = "IPv4 Interface Statistics",
                    ValueText = Loc.F("↓{0:0.0} ↑{1:0.0} Mbps · {2}", s.NetDownMbps, s.NetUpMbps, DateTime.Now.ToString("HH:mm")),
                    ValueColor = PulseContentPanel.ColCyan
                });
            }

            return list;
        }
    }

    internal sealed class SensorPickerForm : Form
    {
        private sealed class PickerZone
        {
            public RectangleF Rect;
            public string Key;
            public object Payload;

            public PickerZone(RectangleF r, string k, object p)
            {
                Rect = r; Key = k; Payload = p;
            }
        }

        private readonly CorePulseSettings _settings;
        private readonly HardwareEngine _engine;
        private readonly string _metricKey;
        private readonly string _metricTitle;
        private readonly string _lang;
        private readonly float _scale;
        private readonly Action _onChanged;
        private readonly Timer _refreshTimer;
        private readonly List<PickerZone> _zones = new List<PickerZone>();
        private readonly TextBox _searchBox;

        private int _scrollY;
        private int _contentH;
        private int _listTopY;
        private int _listBotY;
        private string _hoverKey = "";
        private object _hoverPayload;
        private bool _hoverScroll;
        private bool _dragScroll;
        private int _dragStartY;
        private int _dragStartScroll;

        private Font _fTitle;
        private Font _fSec;
        private Font _fSub;
        private Font _fSmall;
        private Font _fBtn;
        private Font _fBadge;

        public SensorPickerForm(
            CorePulseSettings settings,
            HardwareEngine engine,
            TelemetrySnapshot initialSnap,
            string metricKey,
            string metricTitle,
            string lang,
            float scale,
            Action onChanged)
        {
            _settings = settings;
            _engine = engine;
            _metricKey = metricKey;
            _metricTitle = metricTitle;
            _lang = lang ?? "en";
            _scale = scale > 0.5f ? scale : 1.0f;
            _onChanged = onChanged;

            Text = T("Sensors — ", "Sensörler — ", "分区传感器 — ") + metricTitle;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = PulseContentPanel.ColBg;
            DoubleBuffered = true;
            KeyPreview = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);

            RebuildFonts();

            ClientSize = new Size(S(560f), S(520f));
            MinimumSize = new Size(S(460f), S(400f));

            _searchBox = new TextBox();
            _searchBox.BorderStyle = BorderStyle.None;
            _searchBox.BackColor = PulseContentPanel.ColCard;
            _searchBox.ForeColor = PulseContentPanel.ColTextPrimary;
            _searchBox.Font = _fSub;
            _searchBox.Visible = false;
            _searchBox.TextChanged += delegate { _scrollY = 0; Invalidate(); };
            _searchBox.GotFocus += delegate { Invalidate(); };
            _searchBox.LostFocus += delegate
            {
                if (string.IsNullOrEmpty(_searchBox.Text)) _searchBox.Visible = false;
                Invalidate();
            };
            Controls.Add(_searchBox);
            LayoutSearchBox();

            _refreshTimer = new Timer();
            _refreshTimer.Interval = 1000;
            _refreshTimer.Tick += delegate { if (!IsDisposed && Visible) { LayoutSearchBox(); Invalidate(); } };
            _refreshTimer.Start();
        }

        private TelemetrySnapshot CurrentSnap(TelemetrySnapshot fallback)
        {
            if (_engine != null && _engine.Latest != null) return _engine.Latest;
            return fallback ?? new TelemetrySnapshot();
        }

        private string T(string en, string tr, string zh) { return Loc.T(_lang, en, tr, zh); }
        private int S(float v) { return (int)Math.Round(v * _scale); }
        private float Sf(float v) { return v * _scale; }
        private float PtToPx(float pt) { return pt * (96f / 72f) * _scale; }
        private int ScrollW() { return (int)Math.Round(10f * _scale); }

        private void RebuildFonts()
        {
            DisposeFonts();
            string fam = Loc.FontFamily(_lang);
            _fTitle = new Font(fam, PtToPx(9.8f), FontStyle.Bold, GraphicsUnit.Pixel);
            _fSec = new Font(fam, PtToPx(8.2f), FontStyle.Bold, GraphicsUnit.Pixel);
            _fSub = new Font(fam, PtToPx(8.2f), FontStyle.Regular, GraphicsUnit.Pixel);
            _fSmall = new Font("Segoe UI", PtToPx(7.6f), FontStyle.Regular, GraphicsUnit.Pixel);
            _fBtn = new Font(fam, PtToPx(8.3f), FontStyle.Bold, GraphicsUnit.Pixel);
            _fBadge = new Font(fam, PtToPx(6.9f), FontStyle.Bold, GraphicsUnit.Pixel);
        }

        private void DisposeFonts()
        {
            if (_fTitle != null) { _fTitle.Dispose(); _fTitle = null; }
            if (_fSec != null) { _fSec.Dispose(); _fSec = null; }
            if (_fSub != null) { _fSub.Dispose(); _fSub = null; }
            if (_fSmall != null) { _fSmall.Dispose(); _fSmall = null; }
            if (_fBtn != null) { _fBtn.Dispose(); _fBtn = null; }
            if (_fBadge != null) { _fBadge.Dispose(); _fBadge = null; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            WinNative.ApplyDarkTitleBar(Handle, PulseContentPanel.ColBg, PulseContentPanel.ColTextPrimary);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = null;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                Close();
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutSearchBox();
            ClampScroll();
            Invalidate();
        }

        private void ComputeLayoutBands(TelemetrySnapshot snap, out float headerBottom, out float toolbarY, out float listTop, out float listBot)
        {
            List<HudActiveSensorDetail> details = HudSensorResolver.DescribeActiveSensors(_metricKey, _settings, snap, _lang);
            int shownDetails = Math.Min(4, Math.Max(1, details.Count));
            float pad = Sf(16f);
            float detailH = Sf(28f) + shownDetails * Sf(22f);
            headerBottom = pad + Sf(40f) + detailH + Sf(10f);
            toolbarY = headerBottom;
            listTop = toolbarY + Sf(34f);
            listBot = ClientSize.Height - Sf(50f);
        }

        private void LayoutSearchBox()
        {
            if (_searchBox == null) return;
            TelemetrySnapshot snap = CurrentSnap(null);
            float headerBottom, toolbarY, listTop, listBot;
            ComputeLayoutBands(snap, out headerBottom, out toolbarY, out listTop, out listBot);
            float pad = Sf(16f);
            float innerW = ClientSize.Width - pad * 2f;
            float searchW = Math.Max(Sf(110f), innerW - Sf(272f));
            float sx = ClientSize.Width - pad - searchW;
            _searchBox.SetBounds(
                (int)Math.Round(sx + Sf(10f)),
                (int)Math.Round(toolbarY + Sf(5f)),
                (int)Math.Max(60, Math.Round(searchW - Sf(20f))),
                (int)Math.Max(16, Math.Round(Sf(18f))));
        }

        private List<HudSensorOption> GetFilteredOptions(TelemetrySnapshot snap)
        {
            List<HudSensorOption> zoneItems = HudSensorResolver.GetZoneOptions(_metricKey, snap, _lang);
            string q = (_searchBox != null ? (_searchBox.Text ?? "") : "").Trim();
            if (string.IsNullOrEmpty(q)) return zoneItems;

            List<HudSensorOption> filtered = new List<HudSensorOption>();
            for (int i = 0; i < zoneItems.Count; i++)
            {
                HudSensorOption o = zoneItems[i];
                bool hit = (o.Name != null && o.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                           (o.HardwareName != null && o.HardwareName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                           (o.TypeLabel != null && o.TypeLabel.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                if (hit) filtered.Add(o);
            }
            return filtered;
        }

        private int ListHeight() { return Math.Max(1, _listBotY - _listTopY); }
        private int MaxScroll() { return Math.Max(0, _contentH - ListHeight()); }
        private void ClampScroll() { int m = MaxScroll(); if (_scrollY > m) _scrollY = m; if (_scrollY < 0) _scrollY = 0; }

        private RectangleF GetThumb()
        {
            int lh = ListHeight();
            if (_contentH <= lh || lh <= 0) return RectangleF.Empty;
            float tx = ClientSize.Width - Sf(16f) - ScrollW();
            float th = Math.Max(Sf(28f), lh * ((float)lh / _contentH));
            float ratio = (float)_scrollY / Math.Max(1, MaxScroll());
            return new RectangleF(tx + Sf(2f), _listTopY + ratio * (lh - th), ScrollW() - Sf(4f), th);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (MaxScroll() <= 0) return;
            _scrollY -= (e.Delta / 120) * (int)Math.Round(48f * _scale);
            ClampScroll();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragScroll)
            {
                RectangleF tb = GetThumb();
                float avail = Math.Max(1f, ListHeight() - tb.Height);
                _scrollY = _dragStartScroll + (int)Math.Round(((e.Y - _dragStartY) / avail) * MaxScroll());
                ClampScroll();
                Invalidate();
                return;
            }
            RectangleF tr = GetThumb();
            bool onSc = !tr.IsEmpty && e.Y >= _listTopY && e.Y <= _listBotY && e.X >= tr.X - Sf(4f) && e.X <= tr.Right + Sf(6f);
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
            if (!_dragScroll && (!string.IsNullOrEmpty(_hoverKey) || _hoverScroll))
            {
                _hoverKey = ""; _hoverPayload = null; _hoverScroll = false; Cursor = Cursors.Default; Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            RectangleF tr = GetThumb();
            if (!tr.IsEmpty && e.Y >= _listTopY && e.Y <= _listBotY && e.X >= tr.X - Sf(4f) && e.X <= tr.Right + Sf(6f))
            {
                _dragScroll = true; _dragStartY = e.Y; _dragStartScroll = _scrollY; Capture = true; return;
            }
            for (int i = _zones.Count - 1; i >= 0; i--)
            {
                if (_zones[i].Rect.Contains(e.Location))
                {
                    HandleClick(_zones[i].Key, _zones[i].Payload);
                    return;
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragScroll) { _dragScroll = false; Capture = false; }
        }

        private void HandleClick(string key, object payload)
        {
            if (key == "reset_auto" || key == "pick_auto")
            {
                if (_settings.HudCustomSensors.ContainsKey(_metricKey))
                {
                    _settings.HudCustomSensors.Remove(_metricKey);
                    if (_onChanged != null) _onChanged();
                    LayoutSearchBox();
                    Invalidate();
                }
            }
            else if (key == "select_all")
            {
                TelemetrySnapshot snap = CurrentSnap(null);
                List<HudSensorOption> zoneOpts = HudSensorResolver.GetZoneOptions(_metricKey, snap, _lang);
                if (zoneOpts.Count > 0)
                {
                    List<string> allKeys = new List<string>(zoneOpts.Count);
                    for (int i = 0; i < zoneOpts.Count; i++)
                        allKeys.Add(zoneOpts[i].Key);
                    _settings.HudCustomSensors[_metricKey] = allKeys;
                    if (_onChanged != null) _onChanged();
                    LayoutSearchBox();
                    Invalidate();
                }
            }
            else if (key == "toggle_sensor" && payload is string)
            {
                string sk = (string)payload;
                List<string> cur;
                if (!_settings.HudCustomSensors.TryGetValue(_metricKey, out cur) || cur == null)
                {
                    cur = new List<string>();
                    _settings.HudCustomSensors[_metricKey] = cur;
                }
                int idx = -1;
                for (int i = 0; i < cur.Count; i++)
                {
                    if (string.Equals(cur[i], sk, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
                }
                if (idx >= 0) cur.RemoveAt(idx);
                else cur.Add(sk);

                if (cur.Count == 0) _settings.HudCustomSensors.Remove(_metricKey);
                if (_onChanged != null) _onChanged();
                LayoutSearchBox();
                Invalidate();
            }
            else if (key == "close_dlg")
            {
                Close();
            }
            else if (key == "search_focus")
            {
                if (_searchBox != null)
                {
                    _searchBox.Visible = true;
                    _searchBox.Focus();
                    Invalidate();
                }
            }
        }

        private bool IsHov(string key, object payload) { return _hoverKey == key && object.Equals(_hoverPayload, payload); }

        private static bool ContainsKey(List<string> list, string key)
        {
            if (list == null || string.IsNullOrEmpty(key)) return false;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(PulseContentPanel.ColBg);
            _zones.Clear();

            TelemetrySnapshot snap = CurrentSnap(null);
            float pad = Sf(16f);
            float w = ClientSize.Width;
            float innerW = w - pad * 2f;

            // Header
            DrawText(g, _metricTitle, _fTitle, PulseContentPanel.ColTextPrimary,
                new RectangleF(pad, pad, innerW - Sf(110f), Sf(20f)), StringAlignment.Near);
            DrawText(g, T("Showing sensors for this slot only. Check one or more sensors to display.",
                "Yalnızca bu bölümün sensörleri gösterilir. Birden fazla seçebilirsiniz.",
                "仅展示当前分区对应的传感器，支持勾选一项或多项组合显示。"),
                _fSub, PulseContentPanel.ColTextSec, new RectangleF(pad, pad + Sf(20f), innerW, Sf(16f)), StringAlignment.Near);

            List<string> selectedKeys = HudSensorResolver.GetSelectedKeys(_settings, _metricKey);
            bool hasCustom = selectedKeys != null && selectedKeys.Count > 0;
            RectangleF bResetTop = new RectangleF(w - pad - Sf(102f), pad, Sf(102f), Sf(24f));
            DrawPillBtn(g, bResetTop, T("Reset Auto", "Varsayılan", "恢复默认"), IsHov("reset_auto", null), false, hasCustom);
            if (hasCustom) _zones.Add(new PickerZone(bResetTop, "reset_auto", null));

            // Active Sensor Details Box (max 4 preview rows to keep list spacious)
            List<HudActiveSensorDetail> details = HudSensorResolver.DescribeActiveSensors(_metricKey, _settings, snap, _lang);
            int shownDetails = Math.Min(4, Math.Max(1, details.Count));
            float detailY = pad + Sf(40f);
            float detailH = Sf(28f) + shownDetails * Sf(22f);
            RectangleF detBox = new RectangleF(pad, detailY, innerW, detailH);
            DrawBox(g, detBox, Sf(8f), PulseContentPanel.ColCard, hasCustom ? PulseContentPanel.ColCyanBd : PulseContentPanel.ColBorder);

            string activeHdr = hasCustom
                ? Loc.F(T("ACTIVE SENSORS · {0} SELECTED", "ETKİN SENSÖRLER · {0} SEÇİLİ", "当前生效传感器 · 已多选 {0} 项"), selectedKeys.Count)
                : T("ACTIVE SOURCE · SMART AUTO", "ETKİN KAYNAK · AKILLI OTOMATİK", "当前生效数据源 · 智能默认");
            DrawText(g, activeHdr, _fSec, hasCustom ? PulseContentPanel.ColCyan : PulseContentPanel.ColEmerald,
                new RectangleF(detBox.X + Sf(12f), detBox.Y + Sf(6f), detBox.Width - Sf(24f), Sf(16f)), StringAlignment.Near);

            float dy = detBox.Y + Sf(25f);
            for (int i = 0; i < shownDetails; i++)
            {
                HudActiveSensorDetail d = details[i];
                RectangleF badgeRc = new RectangleF(detBox.X + Sf(12f), dy + Sf(2f), Sf(62f), Sf(17f));
                DrawBox(g, badgeRc, Sf(4f), Color.FromArgb(24, 34, 48), PulseContentPanel.ColBorder);
                DrawText(g, d.RoleLabel, _fBadge, PulseContentPanel.ColCyan, badgeRc, StringAlignment.Center);

                string labelText = d.SensorName + (string.IsNullOrEmpty(d.HardwareName) ? "" : ("  ·  " + d.HardwareName));
                if (i == shownDetails - 1 && details.Count > shownDetails)
                    labelText += Loc.F(T(" (+{0} more)", " (+{0} daha)", "（另有 {0} 项）"), details.Count - shownDetails);

                float valW = Sf(130f);
                DrawText(g, labelText, _fSub, PulseContentPanel.ColTextPrimary,
                    new RectangleF(badgeRc.Right + Sf(8f), dy, detBox.Right - badgeRc.Right - valW - Sf(22f), Sf(20f)), StringAlignment.Near);
                DrawText(g, d.ValueText, _fBtn, d.ValueColor,
                    new RectangleF(detBox.Right - valW - Sf(12f), dy, valW, Sf(20f)), StringAlignment.Far);
                dy += Sf(22f);
            }

            // Toolbar: Zone Count Pill + Select All + Search Box
            float headerBottom, toolbarY, listTop, listBot;
            ComputeLayoutBands(snap, out headerBottom, out toolbarY, out listTop, out listBot);
            _listTopY = (int)Math.Round(listTop);
            _listBotY = (int)Math.Round(listBot);

            List<HudSensorOption> zoneAll = HudSensorResolver.GetZoneOptions(_metricKey, snap, _lang);
            RectangleF rCount = new RectangleF(pad, toolbarY, Sf(166f), Sf(26f));
            DrawBox(g, rCount, Sf(6f), Color.FromArgb(16, 22, 32), PulseContentPanel.ColBorder);
            DrawText(g, Loc.F(T("Zone Sensors: {0}", "Bölüm Sensörü: {0}", "本分区可用传感器：{0}"), zoneAll.Count),
                _fSmall, PulseContentPanel.ColTextSec, rCount, StringAlignment.Center);

            RectangleF rSelAll = new RectangleF(rCount.Right + Sf(6f), toolbarY, Sf(90f), Sf(26f));
            bool canSelAll = zoneAll.Count > 0;
            DrawPillBtn(g, rSelAll, T("Select All", "Tümünü Seç", "全选本区"), IsHov("select_all", null), false, canSelAll);
            if (canSelAll) _zones.Add(new PickerZone(rSelAll, "select_all", null));

            float searchW = Math.Max(Sf(110f), innerW - Sf(272f));
            RectangleF sBoxRc = new RectangleF(w - pad - searchW, toolbarY, searchW, Sf(26f));
            DrawBox(g, sBoxRc, Sf(6f), PulseContentPanel.ColCard, _searchBox.Focused ? PulseContentPanel.ColCyanBd : PulseContentPanel.ColBorder);
            _zones.Add(new PickerZone(sBoxRc, "search_focus", null));
            if (string.IsNullOrEmpty(_searchBox.Text) && !_searchBox.Focused)
            {
                DrawText(g, T("Filter this zone...", "Bölümde ara...", "筛选本分区传感器..."), _fSmall, PulseContentPanel.ColTextMuted,
                    new RectangleF(sBoxRc.X + Sf(10f), sBoxRc.Y, sBoxRc.Width - Sf(16f), sBoxRc.Height), StringAlignment.Near);
            }

            // Scrollable Sensor List
            RectangleF listClip = new RectangleF(pad, listTop, innerW, Math.Max(10f, listBot - listTop));
            Region oldClip = g.Clip;
            g.SetClip(listClip);

            List<HudSensorOption> items = GetFilteredOptions(snap);
            bool hasScroll = _contentH > ListHeight();
            float rowW = innerW - (hasScroll ? ScrollW() + Sf(4f) : 0f);
            float y = listTop - _scrollY;
            float rowH = Sf(46f);
            float gap = Sf(5f);

            // Auto Default Row
            if (string.IsNullOrEmpty(_searchBox.Text))
            {
                bool autoSel = !hasCustom;
                RectangleF ar = new RectangleF(pad, y, rowW, Sf(42f));
                if (ar.Bottom >= listTop - 2 && ar.Top <= listBot + 2)
                {
                    bool hov = IsHov("pick_auto", null);
                    DrawBox(g, ar, Sf(7f),
                        autoSel ? PulseContentPanel.ColEmeraldBg : (hov ? PulseContentPanel.ColCardHover : PulseContentPanel.ColCard),
                        autoSel ? PulseContentPanel.ColEmerald : (hov ? PulseContentPanel.ColBorderHover : PulseContentPanel.ColBorder));
                    DrawCheckbox(g, new RectangleF(ar.X + Sf(12f), ar.Y + (ar.Height - Sf(15f)) * 0.5f, Sf(15f), Sf(15f)), autoSel, PulseContentPanel.ColEmerald);
                    DrawText(g, T("Auto (Smart Default)", "Otomatik (Akıllı Varsayılan)", "Auto（智能默认策略）"),
                        _fBtn, autoSel ? PulseContentPanel.ColEmerald : PulseContentPanel.ColTextPrimary,
                        new RectangleF(ar.X + Sf(36f), ar.Y + Sf(4f), ar.Width - Sf(50f), Sf(18f)), StringAlignment.Near);
                    DrawText(g, T("Uses CorePulse's built-in selection for this slot (uncheck all custom items to revert)",
                        "Bu yuva için CorePulse varsayılanını kullanır",
                        "使用内置默认策略；勾选下方传感器将进入多选组合模式"),
                        _fSmall, PulseContentPanel.ColTextSec,
                        new RectangleF(ar.X + Sf(36f), ar.Y + Sf(22f), ar.Width - Sf(50f), Sf(15f)), StringAlignment.Near);

                    RectangleF hitAr = RectangleF.Intersect(ar, listClip);
                    if (!hitAr.IsEmpty) _zones.Add(new PickerZone(hitAr, "pick_auto", null));
                }
                y += Sf(42f) + gap;
            }

            for (int i = 0; i < items.Count; i++)
            {
                HudSensorOption opt = items[i];
                bool sel = ContainsKey(selectedKeys, opt.Key);
                RectangleF rr = new RectangleF(pad, y, rowW, rowH);
                if (rr.Bottom >= listTop - 2 && rr.Top <= listBot + 2)
                {
                    bool hov = IsHov("toggle_sensor", opt.Key);
                    DrawBox(g, rr, Sf(7f),
                        sel ? Color.FromArgb(16, 40, 58) : (hov ? PulseContentPanel.ColCardHover : PulseContentPanel.ColCard),
                        sel ? PulseContentPanel.ColCyan : (hov ? PulseContentPanel.ColBorderHover : PulseContentPanel.ColBorder));

                    DrawCheckbox(g, new RectangleF(rr.X + Sf(12f), rr.Y + (rr.Height - Sf(15f)) * 0.5f, Sf(15f), Sf(15f)), sel, PulseContentPanel.ColCyan);

                    float valColW = Sf(124f);
                    float textX = rr.X + Sf(36f);
                    float textW = Math.Max(Sf(80f), rr.Width - Sf(46f) - valColW);

                    DrawText(g, opt.Name, _fTitle, sel ? PulseContentPanel.ColCyan : PulseContentPanel.ColTextPrimary,
                        new RectangleF(textX, rr.Y + Sf(5f), textW, Sf(18f)), StringAlignment.Near);

                    string subInfo = Loc.F("[{0}] {1} · {2}", opt.Group, opt.HardwareName, opt.TypeLabel);
                    DrawText(g, subInfo, _fSmall, PulseContentPanel.ColTextSec,
                        new RectangleF(textX, rr.Y + Sf(24f), textW, Sf(16f)), StringAlignment.Near);

                    DrawText(g, opt.DetailedValue, _fBtn, opt.ValueColor,
                        new RectangleF(rr.Right - valColW - Sf(10f), rr.Y + Sf(5f), valColW, Sf(18f)), StringAlignment.Far);
                    DrawText(g, opt.MinMaxText, _fSmall, PulseContentPanel.ColTextMuted,
                        new RectangleF(rr.Right - valColW - Sf(10f), rr.Y + Sf(24f), valColW, Sf(16f)), StringAlignment.Far);

                    RectangleF hitRr = RectangleF.Intersect(rr, listClip);
                    if (!hitRr.IsEmpty) _zones.Add(new PickerZone(hitRr, "toggle_sensor", opt.Key));
                }
                y += rowH + gap;
            }

            if (items.Count == 0)
            {
                DrawText(g, T("No sensors detected for this zone. Run as Administrator with PawnIO for full hardware sensors.",
                    "Bu bölüm için sensör algılanmadı. Tam sensörler için Yönetici olarak çalıştırın.",
                    "当前分区暂未检测到硬件传感器（请以管理员身份运行并安装 PawnIO）。"),
                    _fSub, PulseContentPanel.ColTextSec, new RectangleF(pad + Sf(6f), y + Sf(10f), rowW - Sf(12f), Sf(24f)), StringAlignment.Near);
                y += Sf(46f);
            }

            _contentH = (int)Math.Ceiling((y + _scrollY) - listTop);
            if (_scrollY > MaxScroll()) _scrollY = MaxScroll();

            g.Clip = oldClip;

            if (_contentH > ListHeight())
            {
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(16, 21, 29)))
                    g.FillRectangle(tb, w - pad - ScrollW(), listTop, ScrollW(), ListHeight());
                RectangleF thumb = GetThumb();
                using (GraphicsPath gp = Rounded(thumb, Sf(3f)))
                using (SolidBrush sb = new SolidBrush((_hoverScroll || _dragScroll) ? Color.FromArgb(84, 102, 126) : Color.FromArgb(54, 66, 84)))
                    g.FillPath(sb, gp);
            }

            // Footer
            float footY = listBot + Sf(8f);
            using (Pen dp = new Pen(Color.FromArgb(28, 36, 49), 1f))
                g.DrawLine(dp, 0, footY - Sf(2f), w, footY - Sf(2f));
            DrawText(g, T("Multi-select supported · Checked sensors apply live to the HUD.",
                "Çoklu seçim desteklenir · İşaretlenenler HUD'a anında yansır.",
                "支持多选组合 · 勾选后即刻实时更新至置顶悬浮窗。"),
                _fSmall, PulseContentPanel.ColTextMuted, new RectangleF(pad, footY + Sf(6f), innerW - Sf(100f), Sf(22f)), StringAlignment.Near);

            RectangleF bDone = new RectangleF(w - pad - Sf(88f), footY + Sf(4f), Sf(88f), Sf(26f));
            DrawPillBtn(g, bDone, T("Done", "Tamam", "完成"), IsHov("close_dlg", null), true, true);
            _zones.Add(new PickerZone(bDone, "close_dlg", null));
        }

        private void DrawCheckbox(Graphics g, RectangleF r, bool chk, Color col)
        {
            if (chk)
            {
                DrawBox(g, r, Sf(4f), col, col);
                using (Pen p = new Pen(PulseContentPanel.ColBg, Math.Max(1.6f, Sf(1.8f))))
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
                DrawBox(g, r, Sf(4f), PulseContentPanel.ColBg, Color.FromArgb(72, 86, 106));
            }
        }

        private void DrawPillBtn(Graphics g, RectangleF r, string text, bool hov, bool primary, bool enabled)
        {
            Color bg, bd, fg;
            if (!enabled) { bg = PulseContentPanel.ColCard; bd = Color.FromArgb(32, 40, 52); fg = PulseContentPanel.ColTextMuted; }
            else if (primary) { bg = hov ? PulseContentPanel.ColPrimaryHov : PulseContentPanel.ColPrimaryBg; bd = PulseContentPanel.ColEmerald; fg = Color.White; }
            else { bg = hov ? PulseContentPanel.ColBtnHover : PulseContentPanel.ColBtnBg; bd = hov ? Color.FromArgb(78, 94, 118) : PulseContentPanel.ColBtnBd; fg = PulseContentPanel.ColTextBody; }
            DrawBox(g, r, Sf(7f), bg, bd);
            DrawText(g, text, _fBtn, fg, r, StringAlignment.Center);
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
            if (disposing)
            {
                if (_refreshTimer != null) { _refreshTimer.Stop(); _refreshTimer.Dispose(); }
                DisposeFonts();
            }
            base.Dispose(disposing);
        }
    }
}
