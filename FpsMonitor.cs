using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace CorePulse
{
    // Zero-injection, 100% Anti-Cheat-safe real FPS / 1% Low / Frametime monitor
    // Uses Windows built-in ETW (Microsoft-Windows-DXGI + Microsoft-Windows-D3D9 Present events)
    // with exact hardware QPC event timestamps (immune to ETW 1-second buffer flush batching!).
    internal sealed class EtwFpsMonitor : IDisposable
    {
        private const string SessionName = "CorePulse_DXGI_FPS_Session";
        private static readonly Guid DxgiProviderGuid = new Guid("ca11c036-0102-4a2d-a6ad-f03cfed5d3c9");
        private static readonly Guid D3d9ProviderGuid = new Guid("783aca0a-790e-4d7f-8451-aa850511c6b9");

        private const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
        private const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
        private const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
        private const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
        private const uint EVENT_TRACE_CONTROL_STOP = 1;
        private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
        private const byte TRACE_LEVEL_INFORMATION = 4;

        [StructLayout(LayoutKind.Sequential)]
        internal struct WNODE_HEADER
        {
            public uint BufferSize;
            public uint ProviderId;
            public ulong HistoricalContext;
            public long TimeStamp;
            public Guid Guid;
            public uint ClientContext;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EVENT_TRACE_PROPERTIES
        {
            public WNODE_HEADER Wnode;
            public uint BufferSize;
            public uint MinimumBuffers;
            public uint MaximumBuffers;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint FlushTimer;
            public uint EnableFlags;
            public int AgeLimit;
            public uint NumberOfBuffers;
            public uint FreeBuffers;
            public uint EventsLost;
            public uint BuffersWritten;
            public uint LogBuffersLost;
            public uint RealTimeBuffersLost;
            public IntPtr LoggerThreadId;
            public uint LogFileNameOffset;
            public uint LoggerNameOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EVENT_DESCRIPTOR
        {
            public ushort Id;
            public byte Version;
            public byte Channel;
            public byte Level;
            public byte Opcode;
            public ushort Task;
            public ulong Keyword;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EVENT_HEADER
        {
            public ushort Size;
            public ushort HeaderType;
            public ushort Flags;
            public ushort EventProperty;
            public uint ThreadId;
            public uint ProcessId;
            public long TimeStamp;
            public Guid ProviderId;
            public EVENT_DESCRIPTOR EventDescriptor;
            public ulong KernelTimeOrProcessorTime;
            public Guid ActivityId;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ETW_BUFFER_CONTEXT
        {
            public byte ProcessorNumber;
            public byte Alignment;
            public ushort LoggerId;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EVENT_RECORD
        {
            public EVENT_HEADER EventHeader;
            public ETW_BUFFER_CONTEXT BufferContext;
            public ushort ExtendedDataCount;
            public ushort UserDataLength;
            public IntPtr ExtendedData;
            public IntPtr UserData;
            public IntPtr UserContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SYSTEMTIME
        {
            public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct TIME_ZONE_INFORMATION
        {
            public int Bias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string StandardName;
            public SYSTEMTIME StandardDate;
            public int StandardBias;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DaylightName;
            public SYSTEMTIME DaylightDate;
            public int DaylightBias;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct TRACE_LOGFILE_HEADER
        {
            public uint BufferSize;
            public uint Version;
            public uint ProviderVersion;
            public uint NumberOfProcessors;
            public long EndTime;
            public uint TimerResolution;
            public uint MaximumFileSize;
            public uint LogFileMode;
            public uint BuffersWritten;
            public Guid LogInstanceGuid;
            public IntPtr LoggerName;
            public IntPtr LogFileName;
            public TIME_ZONE_INFORMATION TimeZone;
            public long BootTime;
            public long PerfFreq;
            public long StartTime;
            public uint ReservedFlags;
            public uint BuffersLost;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EVENT_TRACE_HEADER
        {
            public ushort Size;
            public ushort FieldTypeFlags;
            public uint Version;
            public uint ThreadId;
            public uint ProcessId;
            public long TimeStamp;
            public Guid Guid;
            public uint KernelTime;
            public uint UserTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct EVENT_TRACE
        {
            public EVENT_TRACE_HEADER Header;
            public uint InstanceId;
            public uint ParentInstanceId;
            public Guid ParentGuid;
            public IntPtr MofData;
            public uint MofLength;
            public uint ClientContext;
        }

        internal delegate void EventRecordCallback(ref EVENT_RECORD eventRecord);
        internal delegate bool EventTraceBufferCallback(IntPtr logfile);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct EVENT_TRACE_LOGFILE
        {
            public string LogFileName;
            public string LoggerName;
            public long CurrentTime;
            public uint BuffersRead;
            public uint ProcessTraceMode;
            public EVENT_TRACE CurrentEvent;
            public TRACE_LOGFILE_HEADER LogfileHeader;
            public EventTraceBufferCallback BufferCallback;
            public uint BufferSize;
            public uint Filled;
            public uint EventsLost;
            public EventRecordCallback EventRecordCallback;
            public uint IsKernelTrace;
            public IntPtr Context;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint StartTrace(out ulong sessionHandle, string instanceName, IntPtr properties);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ControlTrace(ulong sessionHandle, string instanceName, IntPtr properties, uint controlCode);

        [DllImport("advapi32.dll")]
        private static extern uint EnableTraceEx2(ulong traceHandle, ref Guid providerId, uint controlCode, byte level, ulong matchAnyKeyword, ulong matchAllKeyword, uint timeout, IntPtr enableParameters);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern ulong OpenTrace(ref EVENT_TRACE_LOGFILE logfile);

        [DllImport("advapi32.dll")]
        private static extern uint ProcessTrace(ulong[] handleArray, uint handleCount, IntPtr startTime, IntPtr endTime);

        [DllImport("advapi32.dll")]
        private static extern uint CloseTrace(ulong traceHandle);

        private readonly object _lock = new object();
        private readonly uint _ownPid;
        private readonly Dictionary<uint, Queue<double>> _pidPresentQpcMs = new Dictionary<uint, Queue<double>>();
        private readonly Dictionary<uint, string> _pidNameCache = new Dictionary<uint, string>();
        private readonly double _qpcToMs;

        private static readonly HashSet<string> IgnoredShellProcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dwm", "explorer", "shellexperiencehost", "startmenuexperiencehost",
            "searchhost", "searchapp", "textinputhost", "lockapp", "applicationframehost",
            "systemsettings", "taskmgr", "corepulse", "gamebar", "widgets",
            "widgetservice", "snippingtool", "screenclippinghost", "nvcontainer",
            "nvidia overlay", "nvidia share", "radeonsoftware", "amdrsserv"
        };

        internal static bool IsIgnoredSystemProcess(string procBaseName)
        {
            if (string.IsNullOrEmpty(procBaseName)) return false;
            string n = procBaseName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? procBaseName.Substring(0, procBaseName.Length - 4)
                : procBaseName;
            return IgnoredShellProcs.Contains(n);
        }

        private string ResolveProcessBaseName(uint pid)
        {
            string cached;
            lock (_lock)
            {
                if (_pidNameCache.TryGetValue(pid, out cached)) return cached;
            }
            string name = "";
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                    name = p.ProcessName ?? "";
            }
            catch { }
            lock (_lock)
            {
                if (_pidNameCache.Count > 64) _pidNameCache.Clear();
                _pidNameCache[pid] = name;
            }
            return name;
        }

        private ulong _sessionHandle;
        private ulong _traceHandle;
        private Thread _worker;
        private EventRecordCallback _callbackRef;
        private volatile bool _running;

        public bool IsActive { get; private set; }
        public bool NeedsAdmin { get; private set; }

        public EtwFpsMonitor()
        {
            _ownPid = (uint)Process.GetCurrentProcess().Id;
            _qpcToMs = 1000.0 / Math.Max(1L, Stopwatch.Frequency);
        }

        public void Start()
        {
            if (_running) return;
            if (!WinNative.IsAdministrator())
            {
                NeedsAdmin = true;
                IsActive = false;
                return;
            }

            try
            {
                StopStaleSession();
                int propSize = Marshal.SizeOf(typeof(EVENT_TRACE_PROPERTIES)) + 1024;
                IntPtr mem = Marshal.AllocHGlobal(propSize);
                try
                {
                    ZeroMemory(mem, propSize);
                    EVENT_TRACE_PROPERTIES props = new EVENT_TRACE_PROPERTIES();
                    props.Wnode.BufferSize = (uint)propSize;
                    props.Wnode.Flags = WNODE_FLAG_TRACED_GUID;
                    props.Wnode.ClientContext = 1; // Hardware QPC timestamps!
                    props.LogFileMode = EVENT_TRACE_REAL_TIME_MODE;
                    props.FlushTimer = 1;
                    props.BufferSize = 64;
                    props.MinimumBuffers = 4;
                    props.MaximumBuffers = 16;
                    props.LoggerNameOffset = (uint)Marshal.SizeOf(typeof(EVENT_TRACE_PROPERTIES));
                    Marshal.StructureToPtr(props, mem, false);

                    uint rc = StartTrace(out _sessionHandle, SessionName, mem);
                    if (rc != 0)
                    {
                        NeedsAdmin = (rc == 5);
                        IsActive = false;
                        return;
                    }
                }
                finally { Marshal.FreeHGlobal(mem); }

                Guid dxgi = DxgiProviderGuid;
                uint erc = EnableTraceEx2(_sessionHandle, ref dxgi, EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_INFORMATION, 0xFFFFFFFFFFFFFFFF, 0, 0, IntPtr.Zero);
                if (erc != 0)
                {
                    StopStaleSession();
                    return;
                }

                // Also enable D3D9 Present events for legacy DirectX 9 titles
                Guid d3d9 = D3d9ProviderGuid;
                EnableTraceEx2(_sessionHandle, ref d3d9, EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_INFORMATION, 0xFFFFFFFFFFFFFFFF, 0, 0, IntPtr.Zero);

                _callbackRef = new EventRecordCallback(OnEventRecord);
                EVENT_TRACE_LOGFILE log = new EVENT_TRACE_LOGFILE();
                log.LoggerName = SessionName;
                log.ProcessTraceMode = PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD;
                log.EventRecordCallback = _callbackRef;

                _traceHandle = OpenTrace(ref log);
                if (_traceHandle == ulong.MaxValue || _traceHandle == 0)
                {
                    StopStaleSession();
                    return;
                }

                _running = true;
                IsActive = true;
                NeedsAdmin = false;
                _worker = new Thread(new ThreadStart(delegate
                {
                    try { ProcessTrace(new ulong[] { _traceHandle }, 1, IntPtr.Zero, IntPtr.Zero); }
                    catch { }
                }));
                _worker.IsBackground = true;
                _worker.Name = "CorePulse_ETW_FPS";
                _worker.Start();
            }
            catch
            {
                IsActive = false;
            }
        }

        private void OnEventRecord(ref EVENT_RECORD rec)
        {
            ushort id = rec.EventHeader.EventDescriptor.Id;
            bool isDxgiPresent = (id == 42 || id == 55) && rec.EventHeader.ProviderId == DxgiProviderGuid;
            bool isD3d9Present = (id == 1) && rec.EventHeader.ProviderId == D3d9ProviderGuid;
            if (!isDxgiPresent && !isD3d9Present) return;

            uint pid = rec.EventHeader.ProcessId;
            if (pid == 0 || pid == _ownPid) return;

            // Exact hardware QPC presentation timestamp (immune to 1s ETW buffer flush batching!)
            double qpcMs = rec.EventHeader.TimeStamp * _qpcToMs;
            if (qpcMs <= 0.0) qpcMs = Stopwatch.GetTimestamp() * _qpcToMs;

            lock (_lock)
            {
                Queue<double> q;
                if (!_pidPresentQpcMs.TryGetValue(pid, out q))
                {
                    if (_pidPresentQpcMs.Count > 24) PruneStalePids(qpcMs);
                    q = new Queue<double>(256);
                    _pidPresentQpcMs[pid] = q;
                }
                q.Enqueue(qpcMs);
                while (q.Count > 0 && qpcMs - q.Peek() > 2500.0)
                    q.Dequeue();
            }
        }

        private void PruneStalePids(double nowMs)
        {
            List<uint> dead = new List<uint>();
            foreach (KeyValuePair<uint, Queue<double>> kv in _pidPresentQpcMs)
            {
                if (kv.Value.Count == 0) dead.Add(kv.Key);
                else
                {
                    double last = 0;
                    foreach (double t in kv.Value) last = t;
                    if (nowMs - last > 5000.0) dead.Add(kv.Key);
                }
            }
            foreach (uint p in dead) _pidPresentQpcMs.Remove(p);
        }

        // Pure mathematical FPS / 1% Low / Frametime calculator from real QPC timestamps
        internal static void ComputeFpsStats(double[] qpcStampsMs, double nowQpcMs, out float fps, out float fps1Low, out float frametimeMs)
        {
            fps = 0f;
            fps1Low = 0f;
            frametimeMs = 0f;
            if (qpcStampsMs == null || qpcStampsMs.Length < 3) return;

            // Only keep frames within the last 2.2 seconds (allowing for 1s ETW flush latency)
            List<double> window = new List<double>(qpcStampsMs.Length);
            for (int i = 0; i < qpcStampsMs.Length; i++)
            {
                if (nowQpcMs - qpcStampsMs[i] <= 2200.0)
                    window.Add(qpcStampsMs[i]);
            }
            if (window.Count < 3) return;

            window.Sort();
            double newest = window[window.Count - 1];
            if (nowQpcMs - newest > 1800.0) return; // Stale (game stopped rendering)

            // Build frame-to-frame deltas from real presentation timestamps
            List<double> deltas = new List<double>(window.Count);
            double recentFirst = newest;
            int recentCount = 0;
            for (int i = 0; i < window.Count; i++)
            {
                if (newest - window[i] <= 1000.0)
                {
                    if (recentCount == 0) recentFirst = window[i];
                    recentCount++;
                }
                if (i > 0)
                {
                    double dt = window[i] - window[i - 1];
                    if (dt >= 0.3 && dt <= 500.0) deltas.Add(dt);
                }
            }

            if (deltas.Count < 2 || recentCount < 2) return;

            double sumDt = 0;
            for (int i = 0; i < deltas.Count; i++) sumDt += deltas[i];
            double avgDt = sumDt / deltas.Count;
            frametimeMs = (float)avgDt;

            double recentSpan = newest - recentFirst;
            fps = recentSpan >= 120.0
                ? (float)(((recentCount - 1) * 1000.0) / recentSpan)
                : (float)(1000.0 / avgDt);

            deltas.Sort();
            int idx99 = Math.Min(deltas.Count - 1, (int)Math.Floor(deltas.Count * 0.99));
            double p99Dt = deltas[idx99];
            fps1Low = p99Dt >= 0.3 ? (float)Math.Min(fps, 1000.0 / p99Dt) : fps;
        }

        public void SampleCurrent(out float fps, out float fps1Low, out float frametimeMs, out string procName)
        {
            fps = 0f;
            fps1Low = 0f;
            frametimeMs = 0f;
            procName = "";
            if (!IsActive) return;

            double nowMs = Stopwatch.GetTimestamp() * _qpcToMs;
            uint fgPid = WinNative.GetForegroundPid();
            uint chosenPid = 0;
            double[] stamps = null;
            List<KeyValuePair<uint, double[]>> candidates = new List<KeyValuePair<uint, double[]>>();

            lock (_lock)
            {
                if (fgPid != 0 && fgPid != _ownPid && _pidPresentQpcMs.ContainsKey(fgPid))
                {
                    Queue<double> q = _pidPresentQpcMs[fgPid];
                    while (q.Count > 0 && nowMs - q.Peek() > 2500.0) q.Dequeue();
                    if (q.Count >= 4) candidates.Add(new KeyValuePair<uint, double[]>(fgPid, q.ToArray()));
                }

                foreach (KeyValuePair<uint, Queue<double>> kv in _pidPresentQpcMs)
                {
                    if (kv.Key == fgPid) continue;
                    while (kv.Value.Count > 0 && nowMs - kv.Value.Peek() > 2500.0) kv.Value.Dequeue();
                    if (kv.Value.Count >= 8)
                        candidates.Add(new KeyValuePair<uint, double[]>(kv.Key, kv.Value.ToArray()));
                }
            }

            string chosenName = "";
            // Priority 1: Active foreground app (if not a Windows desktop shell compositor!)
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Key == fgPid)
                {
                    string baseName = ResolveProcessBaseName(fgPid);
                    if (!IsIgnoredSystemProcess(baseName))
                    {
                        chosenPid = fgPid;
                        stamps = candidates[i].Value;
                        chosenName = baseName;
                    }
                    break;
                }
            }

            // Priority 2: Highest-activity non-shell 3D app (e.g. game on 2nd monitor or user checking CorePulse dashboard)
            if (stamps == null)
            {
                int bestCount = 7;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].Value.Length <= bestCount) continue;
                    string baseName = ResolveProcessBaseName(candidates[i].Key);
                    if (IsIgnoredSystemProcess(baseName)) continue;
                    bestCount = candidates[i].Value.Length;
                    chosenPid = candidates[i].Key;
                    stamps = candidates[i].Value;
                    chosenName = baseName;
                }
            }

            if (stamps == null) return;
            ComputeFpsStats(stamps, nowMs, out fps, out fps1Low, out frametimeMs);
            if (fps <= 0.5f) return;

            procName = !string.IsNullOrEmpty(chosenName) ? (chosenName + ".exe") : ("PID " + chosenPid);
        }

        private static void StopStaleSession()
        {
            int propSize = Marshal.SizeOf(typeof(EVENT_TRACE_PROPERTIES)) + 1024;
            IntPtr mem = Marshal.AllocHGlobal(propSize);
            try
            {
                ZeroMemory(mem, propSize);
                EVENT_TRACE_PROPERTIES props = new EVENT_TRACE_PROPERTIES();
                props.Wnode.BufferSize = (uint)propSize;
                props.LoggerNameOffset = (uint)Marshal.SizeOf(typeof(EVENT_TRACE_PROPERTIES));
                Marshal.StructureToPtr(props, mem, false);
                ControlTrace(0, SessionName, mem, EVENT_TRACE_CONTROL_STOP);
            }
            catch { }
            finally { Marshal.FreeHGlobal(mem); }
        }

        private static void ZeroMemory(IntPtr ptr, int size)
        {
            byte[] zeros = new byte[size];
            Marshal.Copy(zeros, 0, ptr, size);
        }

        public void Dispose()
        {
            if (!_running) return;
            _running = false;
            IsActive = false;
            try { if (_traceHandle != 0) CloseTrace(_traceHandle); } catch { }
            StopStaleSession();
            _traceHandle = 0;
            _sessionHandle = 0;
        }
    }
}
