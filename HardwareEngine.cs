using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace CorePulse
{
    internal sealed class SensorReading
    {
        public string Group; // "CPU", "GPU", "Motherboard", "Memory", "Storage", "Power", "Network"
        public string HardwareName;
        public string Name;
        public string TypeLabel; // "Temp", "Load", "Clock", "Fan", "Power", "Control", "Data"
        public string Unit;
        public float Value;
        public float Min;
        public float Max;
    }

    internal sealed class FanChannelInfo
    {
        public string Id;
        public string DisplayName;
        public string HardwareName;
        public bool IsGpuFan;
        public float CurrentRpm = -1f;
        public float CurrentDutyPct = -1f;
        public bool HasControl;
        public int ManualDutyPct = 55;
        public int Mode; // 0 = BIOS Auto, 1 = Manual, 2 = Curve

        // Live LHM IControl reference (null in demo mode or read-only fan)
        internal IControl NativeControl;
    }

    internal sealed class TelemetrySnapshot
    {
        // Hardware inventory
        public string CpuName = "Detecting CPU...";
        public int CpuCores = Environment.ProcessorCount;
        public string GpuName = "Detecting GPU...";
        public string MotherboardName = "Detecting Motherboard...";
        public string RamSpecText = "";
        public string StorageName = "";
        public string PsuLabel = "";

        // Live CPU metrics (-1 means sensor unavailable / Admin needed)
        public float CpuTempC = -1f;
        public float CpuLoadPct = 0f;
        public float CpuClockMhz = -1f;
        public float CpuPowerW = -1f;

        // Live GPU metrics
        public float GpuTempC = -1f;
        public float GpuHotspotC = -1f;
        public float GpuLoadPct = -1f;
        public float GpuVramUsedGb = -1f;
        public float GpuVramTotalGb = -1f;
        public float GpuClockMhz = -1f;
        public float GpuMemClockMhz = -1f;
        public float GpuFanPct = -1f;
        public float GpuFanRpm = -1f;
        public float GpuPowerW = -1f;

        // Live RAM / Board / Disk / PSU / Net
        public float RamUsedGb = 0f;
        public float RamTotalGb = 0f;
        public float RamLoadPct = 0f;
        public float BoardTempC = -1f;
        public float StorageTempC = -1f;
        public float DiskLoadPct = 0f;
        public float PsuRealPowerW = -1f;
        public float EstimatedTotalPowerW = -1f;
        public float NetDownMbps = 0f;
        public float NetUpMbps = 0f;

        // Live FPS (ETW Zero-Injection)
        public float Fps = 0f;
        public float Fps1Low = 0f;
        public float FrametimeMs = 0f;
        public string ActiveGameProcess = "";
        public bool FpsEtwActive = false;

        // Primary Case / CPU Fan RPM summary
        public float PrimaryCaseFanRpm = -1f;

        // Engine status
        public bool LhmLoaded = false;
        public bool IsAdmin = false;
        public bool PawnIoInstalled = false;
        public bool ThermalSafetyOverride = false;

        public List<SensorReading> AllSensors = new List<SensorReading>();
        public List<FanChannelInfo> Fans = new List<FanChannelInfo>();
    }

    internal static class LibResolver
    {
        private static bool _installed;

        public static void Install()
        {
            if (_installed) return;
            _installed = true;
            AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
        }

        private static Assembly OnResolve(object sender, ResolveEventArgs args)
        {
            try
            {
                string shortName = new AssemblyName(args.Name).Name;
                string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                string p1 = Path.Combine(Path.Combine(exeDir, "lib"), shortName + ".dll");
                if (File.Exists(p1)) return Assembly.LoadFrom(p1);
                string p2 = Path.Combine(exeDir, shortName + ".dll");
                if (File.Exists(p2)) return Assembly.LoadFrom(p2);
            }
            catch { }
            return null;
        }

        public static bool IsLhmDllPresent()
        {
            try
            {
                string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                return File.Exists(Path.Combine(Path.Combine(exeDir, "lib"), "LibreHardwareMonitorLib.dll")) ||
                       File.Exists(Path.Combine(exeDir, "LibreHardwareMonitorLib.dll"));
            }
            catch { return false; }
        }
    }

    internal sealed class HardwareEngine : IDisposable
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, float> _minMap = new Dictionary<string, float>();
        private readonly Dictionary<string, float> _maxMap = new Dictionary<string, float>();
        private readonly Dictionary<string, FanChannelInfo> _fanMap = new Dictionary<string, FanChannelInfo>();

        private Computer _computer;
        private EtwFpsMonitor _fps;
        private PerformanceCounter _cpuFallback;
        private long _lastNetRx;
        private long _lastNetTx;
        private DateTime _lastNetTime = DateTime.UtcNow;

        private string _staticCpu = "";
        private string _staticGpu = "";
        private string _staticBoard = "";
        private string _staticRamSpec = "";
        private string _staticStorage = "";
        private float _staticGpuVramGb = -1f;

        public bool LhmActive { get; private set; }
        public TelemetrySnapshot Latest { get; private set; }

        // 60-sample sparkline ring buffers for Dashboard real-time graphs
        public readonly float[] HistCpuTemp = new float[60];
        public readonly float[] HistGpuTemp = new float[60];
        public readonly float[] HistCpuLoad = new float[60];
        public readonly float[] HistGpuLoad = new float[60];
        public readonly float[] HistFps = new float[60];
        public int HistCount;

        public HardwareEngine()
        {
            Latest = new TelemetrySnapshot();
            Latest.IsAdmin = WinNative.IsAdministrator();
            Latest.PawnIoInstalled = WinNative.IsPawnIoInstalled();
        }

        public void Initialize(bool demoMode)
        {
            if (demoMode)
            {
                BuildDemoSnapshot();
                return;
            }

            ReadStaticWmiInventory();

            try
            {
                _cpuFallback = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuFallback.NextValue();
            }
            catch { }

            SampleNetTotals(out _lastNetRx, out _lastNetTx);
            _lastNetTime = DateTime.UtcNow;

            // Start zero-injection ETW DXGI FPS monitor
            _fps = new EtwFpsMonitor();
            _fps.Start();

            // Initialize LibreHardwareMonitorLib v0.9.6 safely (isolated method so missing DLL never crashes startup!)
            if (LibResolver.IsLhmDllPresent())
            {
                try { OpenLhmSafe(); }
                catch { LhmActive = false; }
            }

            PollOnce();
        }

        private void OpenLhmSafe()
        {
            _computer = new Computer();
            _computer.IsCpuEnabled = true;
            _computer.IsGpuEnabled = true;
            _computer.IsMemoryEnabled = true;
            _computer.IsMotherboardEnabled = true;
            _computer.IsControllerEnabled = true;
            _computer.IsStorageEnabled = true;
            _computer.IsNetworkEnabled = true;
            _computer.IsPsuEnabled = true;
            _computer.Open();
            LhmActive = true;
        }

        private void ReadStaticWmiInventory()
        {
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name, NumberOfCores FROM Win32_Processor"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        _staticCpu = (o["Name"] ?? "").ToString().Trim();
                        break;
                    }
                }
            }
            catch { }

            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        string m = (o["Manufacturer"] ?? "").ToString().Trim();
                        string p = (o["Product"] ?? "").ToString().Trim();
                        if (m.IndexOf("Gigabyte", StringComparison.OrdinalIgnoreCase) >= 0) m = "Gigabyte";
                        else if (m.IndexOf("ASUSTeK", StringComparison.OrdinalIgnoreCase) >= 0) m = "ASUS";
                        else if (m.IndexOf("Micro-Star", StringComparison.OrdinalIgnoreCase) >= 0) m = "MSI";
                        _staticBoard = (m + " " + p).Trim();
                        break;
                    }
                }
            }
            catch { }

            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        string n = (o["Name"] ?? "").ToString().Trim();
                        if (string.IsNullOrEmpty(n)) continue;
                        if (n.IndexOf("Microsoft Basic", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        _staticGpu = n;
                        try
                        {
                            ulong ramBytes = Convert.ToUInt64(o["AdapterRAM"] ?? 0);
                            if (ramBytes > 0) _staticGpuVramGb = (float)(ramBytes / (1024.0 * 1024.0 * 1024.0));
                        }
                        catch { }
                        break;
                    }
                }
            }
            catch { }

            try
            {
                int slots = 0;
                int speed = 0;
                ulong capBytes = 0;
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Capacity, ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        slots++;
                        capBytes += Convert.ToUInt64(o["Capacity"] ?? 0);
                        int sp = Convert.ToInt32(o["ConfiguredClockSpeed"] ?? o["Speed"] ?? 0);
                        if (sp > speed) speed = sp;
                    }
                }
                if (capBytes > 0)
                {
                    double gb = Math.Round(capBytes / (1024.0 * 1024.0 * 1024.0));
                    _staticRamSpec = speed > 0
                        ? string.Format("{0:0} GB ({1}x DIMM · {2} MHz)", gb, slots, speed)
                        : string.Format("{0:0} GB ({1}x DIMM)", gb, slots);
                }
            }
            catch { }

            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT Model FROM Win32_DiskDrive"))
                {
                    foreach (ManagementObject o in s.Get())
                    {
                        string mod = (o["Model"] ?? "").ToString().Trim();
                        if (!string.IsNullOrEmpty(mod)) { _staticStorage = mod; break; }
                    }
                }
            }
            catch { }
        }

        public void ResetMinMax()
        {
            lock (_lock)
            {
                _minMap.Clear();
                _maxMap.Clear();
            }
        }

        public TelemetrySnapshot PollOnce()
        {
            TelemetrySnapshot snap = new TelemetrySnapshot();
            snap.IsAdmin = WinNative.IsAdministrator();
            snap.PawnIoInstalled = WinNative.IsPawnIoInstalled();
            snap.LhmLoaded = LhmActive;
            snap.CpuName = !string.IsNullOrEmpty(_staticCpu) ? _staticCpu : "CPU";
            snap.GpuName = !string.IsNullOrEmpty(_staticGpu) ? _staticGpu : "GPU";
            snap.MotherboardName = !string.IsNullOrEmpty(_staticBoard) ? _staticBoard : "Motherboard";
            snap.RamSpecText = _staticRamSpec;
            snap.StorageName = _staticStorage;
            snap.GpuVramTotalGb = _staticGpuVramGb;

            // 1. Native RAM via GlobalMemoryStatusEx (always 100% accurate)
            WinNative.MEMORYSTATUSEX mem = new WinNative.MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(typeof(WinNative.MEMORYSTATUSEX));
            if (WinNative.GlobalMemoryStatusEx(ref mem) && mem.ullTotalPhys > 0)
            {
                snap.RamTotalGb = (float)(mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0));
                snap.RamUsedGb = (float)((mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024.0 * 1024.0));
                snap.RamLoadPct = mem.dwMemoryLoad;
            }

            // 2. Native Network Throughput
            long rx, tx;
            SampleNetTotals(out rx, out tx);
            DateTime now = DateTime.UtcNow;
            double secs = Math.Max(0.25, (now - _lastNetTime).TotalSeconds);
            if (_lastNetRx > 0 && rx >= _lastNetRx) snap.NetDownMbps = (float)(((rx - _lastNetRx) * 8.0) / (secs * 1000000.0));
            if (_lastNetTx > 0 && tx >= _lastNetTx) snap.NetUpMbps = (float)(((tx - _lastNetTx) * 8.0) / (secs * 1000000.0));
            _lastNetRx = rx; _lastNetTx = tx; _lastNetTime = now;

            // 3. ETW Zero-Injection FPS
            if (_fps != null)
            {
                snap.FpsEtwActive = _fps.IsActive;
                float f, f1, ft;
                string gp;
                _fps.SampleCurrent(out f, out f1, out ft, out gp);
                snap.Fps = f; snap.Fps1Low = f1; snap.FrametimeMs = ft; snap.ActiveGameProcess = gp;
            }

            // 4. LibreHardwareMonitorLib Full Sensors + Fan Controls
            if (LhmActive && _computer != null)
            {
                try { PollLhmIntoSnapshot(snap); } catch { }
            }

            // Fallback CPU load if LHM wasn't active
            if (!LhmActive && _cpuFallback != null)
            {
                try { snap.CpuLoadPct = Math.Max(0f, Math.Min(100f, _cpuFallback.NextValue())); } catch { }
            }

            // Transparent system power estimate (when no smart USB PSU sensor exists)
            if (snap.PsuRealPowerW > 1f)
            {
                snap.EstimatedTotalPowerW = snap.PsuRealPowerW;
                snap.PsuLabel = "USB Smart PSU";
            }
            else
            {
                float cpuW = snap.CpuPowerW > 1f ? snap.CpuPowerW : (18f + (snap.CpuLoadPct / 100f) * 55f);
                float gpuW = snap.GpuPowerW > 1f ? snap.GpuPowerW : (15f + (Math.Max(0f, snap.GpuLoadPct) / 100f) * 110f);
                snap.EstimatedTotalPowerW = cpuW + gpuW + 32f; // Board + RAM + Fans + SSD baseline
                snap.PsuLabel = "Est. System Draw";
            }

            // Copy live fan list
            lock (_lock)
            {
                snap.Fans = new List<FanChannelInfo>(_fanMap.Values);
                foreach (FanChannelInfo fc in snap.Fans)
                {
                    if (!fc.IsGpuFan && fc.CurrentRpm > 0 && snap.PrimaryCaseFanRpm < 0)
                        snap.PrimaryCaseFanRpm = fc.CurrentRpm;
                }
            }

            // Append sparkline history
            PushHistory(snap);

            Latest = snap;
            return snap;
        }

        private void PollLhmIntoSnapshot(TelemetrySnapshot snap)
        {
            foreach (IHardware hw in _computer.Hardware)
            {
                try { hw.Update(); } catch { continue; }
                ProcessHardwareNode(hw, snap);
                foreach (IHardware sub in hw.SubHardware)
                {
                    try { sub.Update(); } catch { continue; }
                    ProcessHardwareNode(sub, snap);
                }
            }
        }

        private void ProcessHardwareNode(IHardware hw, TelemetrySnapshot snap)
        {
            HardwareType ht = hw.HardwareType;
            string grp = MapGroup(ht);
            bool isGpu = (ht == HardwareType.GpuAmd || ht == HardwareType.GpuNvidia || ht == HardwareType.GpuIntel);

            if (ht == HardwareType.Cpu && !string.IsNullOrEmpty(hw.Name)) snap.CpuName = hw.Name;
            else if (isGpu && !string.IsNullOrEmpty(hw.Name)) snap.GpuName = hw.Name;
            else if (ht == HardwareType.Motherboard && !string.IsNullOrEmpty(hw.Name)) snap.MotherboardName = hw.Name;
            else if (ht == HardwareType.Storage && string.IsNullOrEmpty(snap.StorageName)) snap.StorageName = hw.Name;

            // Track Fan RPM & Control pairing within this hardware node
            Dictionary<string, float> fanRpms = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            foreach (ISensor s in hw.Sensors)
            {
                if (!s.Value.HasValue || float.IsNaN(s.Value.Value) || float.IsInfinity(s.Value.Value)) continue;
                float val = s.Value.Value;
                string sName = s.Name ?? "";

                // Record valid readings into AllSensors (skip invalid 0°C temps or D3D spam)
                if (ShouldDisplayInSensorTable(s, val))
                    AddSensorReading(snap, grp, hw.Name, sName, s.SensorType, val);

                if (ht == HardwareType.Cpu)
                {
                    if (s.SensorType == SensorType.Temperature && val > 1f && val < 130f)
                    {
                        if (sName.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            sName.IndexOf("Tctl", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            sName.IndexOf("Tdie", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            snap.CpuTempC < 0f)
                            snap.CpuTempC = val;
                    }
                    else if (s.SensorType == SensorType.Load && sName.Equals("CPU Total", StringComparison.OrdinalIgnoreCase))
                        snap.CpuLoadPct = val;
                    else if (s.SensorType == SensorType.Clock && val > 100f &&
                             (sName.IndexOf("Average", StringComparison.OrdinalIgnoreCase) >= 0 || sName.IndexOf("Core #1", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        if (snap.CpuClockMhz < 0f || sName.IndexOf("Average", StringComparison.OrdinalIgnoreCase) >= 0)
                            snap.CpuClockMhz = val;
                    }
                    else if (s.SensorType == SensorType.Power && val > 0.5f && sName.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0)
                        snap.CpuPowerW = val;
                }
                else if (isGpu)
                {
                    if (s.SensorType == SensorType.Temperature && val > 1f && val < 130f)
                    {
                        if (sName.IndexOf("Hot Spot", StringComparison.OrdinalIgnoreCase) >= 0 || sName.IndexOf("Hotspot", StringComparison.OrdinalIgnoreCase) >= 0)
                            snap.GpuHotspotC = val;
                        else if (sName.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0 || snap.GpuTempC < 0f)
                            snap.GpuTempC = val;
                    }
                    else if (s.SensorType == SensorType.Load && (sName.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) || sName.Equals("D3D 3D", StringComparison.OrdinalIgnoreCase)))
                    {
                        if (sName.Equals("GPU Core", StringComparison.OrdinalIgnoreCase) || snap.GpuLoadPct < 0f)
                            snap.GpuLoadPct = val;
                    }
                    else if (s.SensorType == SensorType.Clock && val > 10f)
                    {
                        if (sName.IndexOf("Core", StringComparison.OrdinalIgnoreCase) >= 0) snap.GpuClockMhz = val;
                        else if (sName.IndexOf("Memory", StringComparison.OrdinalIgnoreCase) >= 0) snap.GpuMemClockMhz = val;
                    }
                    else if (s.SensorType == SensorType.Power && val > 0.5f)
                    {
                        if (snap.GpuPowerW < 0f || sName.IndexOf("Package", StringComparison.OrdinalIgnoreCase) >= 0 || sName.IndexOf("Total", StringComparison.OrdinalIgnoreCase) >= 0)
                            snap.GpuPowerW = val;
                    }
                    else if (s.SensorType == SensorType.SmallData)
                    {
                        if (sName.IndexOf("Memory Used", StringComparison.OrdinalIgnoreCase) >= 0) snap.GpuVramUsedGb = val / 1024f;
                        else if (sName.IndexOf("Memory Total", StringComparison.OrdinalIgnoreCase) >= 0) snap.GpuVramTotalGb = val / 1024f;
                    }
                    else if (s.SensorType == SensorType.Fan)
                    {
                        snap.GpuFanRpm = val;
                        fanRpms[sName] = val;
                    }
                    else if (s.SensorType == SensorType.Control)
                    {
                        snap.GpuFanPct = val;
                    }
                }
                else if (ht == HardwareType.Motherboard || ht == HardwareType.SuperIO)
                {
                    if (s.SensorType == SensorType.Temperature && val > 10f && val < 115f && snap.BoardTempC < 0f)
                        snap.BoardTempC = val;
                    else if (s.SensorType == SensorType.Fan)
                        fanRpms[sName] = val;
                }
                else if (ht == HardwareType.Storage)
                {
                    if (s.SensorType == SensorType.Temperature && val > 5f && val < 105f && snap.StorageTempC < 0f)
                        snap.StorageTempC = val;
                    else if (s.SensorType == SensorType.Load && sName.IndexOf("Total Activity", StringComparison.OrdinalIgnoreCase) >= 0)
                        snap.DiskLoadPct = Math.Max(snap.DiskLoadPct, val);
                }
                else if (ht == HardwareType.Psu)
                {
                    if (s.SensorType == SensorType.Power && val > 1f)
                        snap.PsuRealPowerW = Math.Max(snap.PsuRealPowerW, val);
                }
            }

            // Register controllable PWM fan channels + read-only RPM fans
            lock (_lock)
            {
                foreach (ISensor s in hw.Sensors)
                {
                    if (s.SensorType == SensorType.Control)
                    {
                        string id = s.Identifier != null ? s.Identifier.ToString() : (hw.Name + "/" + s.Name);
                        FanChannelInfo fc;
                        if (!_fanMap.TryGetValue(id, out fc))
                        {
                            fc = new FanChannelInfo();
                            fc.Id = id;
                            fc.DisplayName = isGpu ? ("GPU Fan (" + s.Name + ")") : s.Name;
                            fc.HardwareName = hw.Name;
                            fc.IsGpuFan = isGpu;
                            _fanMap[id] = fc;
                        }
                        fc.HasControl = (s.Control != null);
                        fc.NativeControl = s.Control;
                        fc.CurrentDutyPct = s.Value.HasValue ? s.Value.Value : fc.CurrentDutyPct;

                        // Match RPM sensor with same name (e.g. "Fan #1" / "GPU Fan")
                        float rpm;
                        if (fanRpms.TryGetValue(s.Name, out rpm)) fc.CurrentRpm = rpm;
                        else if (isGpu && snap.GpuFanRpm >= 0) fc.CurrentRpm = snap.GpuFanRpm;
                    }
                    else if (s.SensorType == SensorType.Fan && s.Value.HasValue && s.Value.Value > 0f)
                    {
                        // Ensure active RPM-only motherboard fans still show up even if no Control exists
                        string matchKey = null;
                        foreach (KeyValuePair<string, FanChannelInfo> kv in _fanMap)
                        {
                            if (kv.Value.HardwareName == hw.Name && kv.Value.DisplayName.IndexOf(s.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                kv.Value.CurrentRpm = s.Value.Value;
                                matchKey = kv.Key;
                                break;
                            }
                        }
                        if (matchKey == null)
                        {
                            string id = "rpm:" + hw.Name + "/" + s.Name;
                            FanChannelInfo fc;
                            if (!_fanMap.TryGetValue(id, out fc))
                            {
                                fc = new FanChannelInfo();
                                fc.Id = id;
                                fc.DisplayName = isGpu ? "GPU Fan" : s.Name;
                                fc.HardwareName = hw.Name;
                                fc.IsGpuFan = isGpu;
                                fc.HasControl = false;
                                _fanMap[id] = fc;
                            }
                            fc.CurrentRpm = s.Value.Value;
                        }
                    }
                }
            }
        }

        private static bool ShouldDisplayInSensorTable(ISensor s, float val)
        {
            if (s.SensorType == SensorType.Temperature && (val <= 1f || val > 130f)) return false;
            if (s.SensorType == SensorType.Clock && val <= 1f) return false;
            if (s.SensorType == SensorType.Power && val <= 0.05f) return false;
            if (s.SensorType == SensorType.Load && s.Name.StartsWith("D3D ", StringComparison.OrdinalIgnoreCase) && s.Name != "D3D 3D") return false;
            return s.SensorType == SensorType.Temperature ||
                   s.SensorType == SensorType.Load ||
                   s.SensorType == SensorType.Clock ||
                   s.SensorType == SensorType.Fan ||
                   s.SensorType == SensorType.Control ||
                   s.SensorType == SensorType.Power ||
                   s.SensorType == SensorType.Voltage;
        }

        private void AddSensorReading(TelemetrySnapshot snap, string grp, string hwName, string name, SensorType st, float val)
        {
            string key = grp + "|" + hwName + "|" + st + "|" + name;
            float mn = val, mx = val;
            lock (_lock)
            {
                if (_minMap.ContainsKey(key)) mn = Math.Min(_minMap[key], val);
                if (_maxMap.ContainsKey(key)) mx = Math.Max(_maxMap[key], val);
                _minMap[key] = mn;
                _maxMap[key] = mx;
            }

            SensorReading sr = new SensorReading();
            sr.Group = grp;
            sr.HardwareName = hwName;
            sr.Name = name;
            sr.Value = val;
            sr.Min = mn;
            sr.Max = mx;

            if (st == SensorType.Temperature) { sr.TypeLabel = "Temp"; sr.Unit = "°C"; }
            else if (st == SensorType.Load) { sr.TypeLabel = "Load"; sr.Unit = "%"; }
            else if (st == SensorType.Clock) { sr.TypeLabel = "Clock"; sr.Unit = "MHz"; }
            else if (st == SensorType.Fan) { sr.TypeLabel = "Fan"; sr.Unit = "RPM"; }
            else if (st == SensorType.Control) { sr.TypeLabel = "PWM"; sr.Unit = "%"; }
            else if (st == SensorType.Power) { sr.TypeLabel = "Power"; sr.Unit = "W"; }
            else if (st == SensorType.Voltage) { sr.TypeLabel = "Volt"; sr.Unit = "V"; }
            else { sr.TypeLabel = st.ToString(); sr.Unit = ""; }

            snap.AllSensors.Add(sr);
        }

        private static string MapGroup(HardwareType ht)
        {
            if (ht == HardwareType.Cpu) return "CPU";
            if (ht == HardwareType.GpuAmd || ht == HardwareType.GpuNvidia || ht == HardwareType.GpuIntel) return "GPU";
            if (ht == HardwareType.Motherboard || ht == HardwareType.SuperIO || ht == HardwareType.EmbeddedController) return "Motherboard";
            if (ht == HardwareType.Memory) return "Memory";
            if (ht == HardwareType.Storage) return "Storage";
            if (ht == HardwareType.Psu) return "Power";
            return "Network";
        }

        private static void SampleNetTotals(out long rx, out long tx)
        {
            rx = 0; tx = 0;
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    IPv4InterfaceStatistics st = ni.GetIPv4Statistics();
                    rx += st.BytesReceived;
                    tx += st.BytesSent;
                }
            }
            catch { }
        }

        private void PushHistory(TelemetrySnapshot s)
        {
            for (int i = 0; i < 59; i++)
            {
                HistCpuTemp[i] = HistCpuTemp[i + 1];
                HistGpuTemp[i] = HistGpuTemp[i + 1];
                HistCpuLoad[i] = HistCpuLoad[i + 1];
                HistGpuLoad[i] = HistGpuLoad[i + 1];
                HistFps[i] = HistFps[i + 1];
            }
            HistCpuTemp[59] = s.CpuTempC > 0 ? s.CpuTempC : 0f;
            HistGpuTemp[59] = s.GpuTempC > 0 ? s.GpuTempC : 0f;
            HistCpuLoad[59] = s.CpuLoadPct;
            HistGpuLoad[59] = s.GpuLoadPct > 0 ? s.GpuLoadPct : 0f;
            HistFps[59] = s.Fps;
            if (HistCount < 60) HistCount++;
        }

        private void BuildDemoSnapshot()
        {
            TelemetrySnapshot d = new TelemetrySnapshot();
            d.IsAdmin = true;
            d.PawnIoInstalled = true;
            d.LhmLoaded = true;
            d.CpuName = "AMD Ryzen 7 7800X3D";
            d.CpuCores = 8;
            d.GpuName = "NVIDIA GeForce RTX 4070 Super";
            d.MotherboardName = "ASUS ROG STRIX B650E-F";
            d.RamSpecText = "32 GB (2x16 GB DDR5 · 6000 MHz)";
            d.StorageName = "Samsung 990 PRO 2TB NVMe";
            d.PsuLabel = "Est. System Draw";

            d.CpuTempC = 58.4f;
            d.CpuLoadPct = 36.2f;
            d.CpuClockMhz = 4850f;
            d.CpuPowerW = 54.6f;

            d.GpuTempC = 63.0f;
            d.GpuHotspotC = 74.2f;
            d.GpuLoadPct = 88.5f;
            d.GpuVramUsedGb = 7.4f;
            d.GpuVramTotalGb = 12.0f;
            d.GpuClockMhz = 2640f;
            d.GpuMemClockMhz = 10501f;
            d.GpuFanPct = 56f;
            d.GpuFanRpm = 1680f;
            d.GpuPowerW = 194.2f;

            d.RamUsedGb = 14.8f;
            d.RamTotalGb = 32.0f;
            d.RamLoadPct = 46.3f;
            d.BoardTempC = 39.0f;
            d.StorageTempC = 42.0f;
            d.DiskLoadPct = 6.5f;
            d.EstimatedTotalPowerW = 282.8f;
            d.NetDownMbps = 42.6f;
            d.NetUpMbps = 8.4f;

            d.Fps = 184f;
            d.Fps1Low = 142f;
            d.FrametimeMs = 5.4f;
            d.ActiveGameProcess = "cs2.exe";
            d.FpsEtwActive = true;
            d.PrimaryCaseFanRpm = 1180f;

            // Realistic demo sensor table rows
            AddDemoRow(d, "CPU", d.CpuName, "Core (Tctl/Tdie)", "Temp", "°C", 58.4f, 41.2f, 67.8f);
            AddDemoRow(d, "CPU", d.CpuName, "CPU Total", "Load", "%", 36.2f, 8.4f, 64.0f);
            AddDemoRow(d, "CPU", d.CpuName, "Core Clocks (Avg)", "Clock", "MHz", 4850f, 3200f, 5050f);
            AddDemoRow(d, "CPU", d.CpuName, "CPU Package", "Power", "W", 54.6f, 24.1f, 78.5f);
            AddDemoRow(d, "GPU", d.GpuName, "GPU Core", "Temp", "°C", 63.0f, 36.0f, 68.5f);
            AddDemoRow(d, "GPU", d.GpuName, "GPU Hot Spot", "Temp", "°C", 74.2f, 44.0f, 79.8f);
            AddDemoRow(d, "GPU", d.GpuName, "GPU Core", "Load", "%", 88.5f, 4.0f, 99.0f);
            AddDemoRow(d, "GPU", d.GpuName, "GPU Fan", "Fan", "RPM", 1680f, 0f, 1920f);
            AddDemoRow(d, "GPU", d.GpuName, "GPU Board Power", "Power", "W", 194.2f, 16.0f, 218.4f);
            AddDemoRow(d, "Motherboard", d.MotherboardName, "VRM MOS", "Temp", "°C", 46.0f, 34.0f, 51.0f);
            AddDemoRow(d, "Motherboard", d.MotherboardName, "Chipset", "Temp", "°C", 39.0f, 35.0f, 42.0f);
            AddDemoRow(d, "Motherboard", d.MotherboardName, "CPU Fan", "Fan", "RPM", 1340f, 820f, 1620f);
            AddDemoRow(d, "Motherboard", d.MotherboardName, "Chassis Fan #1 (Front)", "Fan", "RPM", 1180f, 750f, 1380f);
            AddDemoRow(d, "Motherboard", d.MotherboardName, "Chassis Fan #2 (Rear)", "Fan", "RPM", 1090f, 700f, 1290f);
            AddDemoRow(d, "Storage", d.StorageName, "NVMe Drive Temp", "Temp", "°C", 42.0f, 35.0f, 47.0f);

            // Realistic demo controllable fan channels
            d.Fans.Add(new FanChannelInfo { Id = "demo_cpu", DisplayName = "CPU Cooler (PWM #1)", HardwareName = d.MotherboardName, IsGpuFan = false, HasControl = true, CurrentRpm = 1340, CurrentDutyPct = 58, ManualDutyPct = 60, Mode = 2 });
            d.Fans.Add(new FanChannelInfo { Id = "demo_gpu", DisplayName = "GPU Fan (RTX 4070 Super)", HardwareName = d.GpuName, IsGpuFan = true, HasControl = true, CurrentRpm = 1680, CurrentDutyPct = 56, ManualDutyPct = 55, Mode = 2 });
            d.Fans.Add(new FanChannelInfo { Id = "demo_sys1", DisplayName = "Front Intake (SYS_FAN1)", HardwareName = d.MotherboardName, IsGpuFan = false, HasControl = true, CurrentRpm = 1180, CurrentDutyPct = 52, ManualDutyPct = 50, Mode = 2 });
            d.Fans.Add(new FanChannelInfo { Id = "demo_sys2", DisplayName = "Rear Exhaust (SYS_FAN2)", HardwareName = d.MotherboardName, IsGpuFan = false, HasControl = true, CurrentRpm = 1090, CurrentDutyPct = 48, ManualDutyPct = 48, Mode = 0 });

            // Populate smooth sparkline curves
            for (int i = 0; i < 60; i++)
            {
                double t = i / 6.5;
                HistCpuTemp[i] = (float)(52.0 + Math.Sin(t) * 4.5 + (i * 0.11));
                HistGpuTemp[i] = (float)(56.0 + Math.Cos(t * 0.8) * 3.8 + (i * 0.13));
                HistCpuLoad[i] = (float)(30.0 + Math.Sin(t * 1.4) * 11.0);
                HistGpuLoad[i] = (float)(80.0 + Math.Cos(t * 1.1) * 9.0);
                HistFps[i] = (float)(176.0 + Math.Sin(t * 1.3) * 14.0);
            }
            HistCount = 60;
            Latest = d;
        }

        private static void AddDemoRow(TelemetrySnapshot d, string grp, string hw, string name, string typ, string unit, float v, float mn, float mx)
        {
            d.AllSensors.Add(new SensorReading { Group = grp, HardwareName = hw, Name = name, TypeLabel = typ, Unit = unit, Value = v, Min = mn, Max = mx });
        }

        public void Dispose()
        {
            try { if (_fps != null) _fps.Dispose(); } catch { }
            try { if (_cpuFallback != null) _cpuFallback.Dispose(); } catch { }
            try { if (_computer != null) _computer.Close(); } catch { }
            _computer = null;
            LhmActive = false;
        }
    }
}
