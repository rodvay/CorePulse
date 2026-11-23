using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace CorePulse
{
    // Zero-injection, 100% Anti-Cheat-safe real FPS / 1% Low / Frametime monitor
    // Uses Windows built-in ETW (Microsoft-Windows-DXGI SwapChain Present events).
    internal sealed class EtwFpsMonitor : IDisposable
    {
        private const string SessionName = "CorePulse_DXGI_FPS_Session";
        private static readonly Guid DxgiProviderGuid = new Guid("ca11c036-0102-4a2d-a6ad-f03cfed5d3c9");

        private const uint WNODE_FLAG_TRACED_GUID = 0x00020000;
        private const uint EVENT_TRACE_REAL_TIME_MODE = 0x00000100;
        private const uint PROCESS_TRACE_MODE_REAL_TIME = 0x00000100;
        private const uint PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
        private const uint EVENT_TRACE_CONTROL_STOP = 1;
        private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
        private const byte TRACE_LEVEL_INFORMATION = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct WNODE_HEADER
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
        private struct EVENT_TRACE_PROPERTIES
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
        private struct EVENT_DESCRIPTOR
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
        private struct EVENT_HEADER
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
        private struct ETW_BUFFER_CONTEXT
        {
            public byte ProcessorNumber;
            public byte Alignment;
            public ushort LoggerId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct EVENT_RECORD
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
        private struct SYSTEMTIME
        {
            public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct TIME_ZONE_INFORMATION
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
        private struct TRACE_LOGFILE_HEADER
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
        private struct EVENT_TRACE_HEADER
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
        private struct EVENT_TRACE
        {
            public EVENT_TRACE_HEADER Header;
            public uint InstanceId;
            public uint ParentInstanceId;
            public Guid ParentGuid;
            public IntPtr MofData;
            public uint MofLength;
            public uint ClientContext;
        }

        private delegate void EventRecordCallback(ref EVENT_RECORD eventRecord);
        private delegate bool EventTraceBufferCallback(IntPtr logfile);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct EVENT_TRACE_LOGFILE
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
        private readonly Dictionary<uint, Queue<double>> _pidPresentTimes = new Dictionary<uint, Queue<double>>();
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private ulong _sessionHandle;
        private ulong _traceHandle;
        private Thread _worker;
        private EventRecordCallback _callbackRef; // pin delegate from GC!
        private volatile bool _running;

        public bool IsActive { get; private set; }
        public bool NeedsAdmin { get; private set; }

        public EtwFpsMonitor()
        {
            _ownPid = (uint)Process.GetCurrentProcess().Id;
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
                    props.Wnode.ClientContext = 1; // QPC
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

                Guid prov = DxgiProviderGuid;
                uint erc = EnableTraceEx2(_sessionHandle, ref prov, EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_INFORMATION, 0xFFFFFFFFFFFFFFFF, 0, 0, IntPtr.Zero);
                if (erc != 0)
                {
                    StopStaleSession();
                    return;
                }

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
            // DXGI SwapChain::Present Start (Id 42) or DXGI PresentMultiplaneOverlay Start (Id 55)
            ushort id = rec.EventHeader.EventDescriptor.Id;
            if (id != 42 && id != 55) return;

            uint pid = rec.EventHeader.ProcessId;
            if (pid == 0 || pid == _ownPid) return;

            double nowMs = _clock.Elapsed.TotalMilliseconds;
            lock (_lock)
            {
                Queue<double> q;
                if (!_pidPresentTimes.TryGetValue(pid, out q))
                {
                    if (_pidPresentTimes.Count > 24) PruneStalePids(nowMs);
                    q = new Queue<double>(256);
                    _pidPresentTimes[pid] = q;
                }
                q.Enqueue(nowMs);
                while (q.Count > 0 && nowMs - q.Peek() > 2200.0)
                    q.Dequeue();
            }
        }

        private void PruneStalePids(double nowMs)
        {
            List<uint> dead = new List<uint>();
            foreach (KeyValuePair<uint, Queue<double>> kv in _pidPresentTimes)
            {
                if (kv.Value.Count == 0) dead.Add(kv.Key);
                else
                {
                    double last = 0;
                    foreach (double t in kv.Value) last = t;
                    if (nowMs - last > 5000.0) dead.Add(kv.Key);
                }
            }
            foreach (uint p in dead) _pidPresentTimes.Remove(p);
        }

        public void SampleCurrent(out float fps, out float fps1Low, out float frametimeMs, out string procName)
        {
            fps = 0f;
            fps1Low = 0f;
            frametimeMs = 0f;
            procName = "";
            if (!IsActive) return;

            double nowMs = _clock.Elapsed.TotalMilliseconds;
            uint fgPid = WinNative.GetForegroundPid();
            uint chosenPid = 0;
            double[] stamps = null;

            lock (_lock)
            {
                // Prefer foreground window if it is actively presenting frames; otherwise pick highest-activity 3D process
                if (fgPid != 0 && fgPid != _ownPid && _pidPresentTimes.ContainsKey(fgPid))
                {
                    Queue<double> q = _pidPresentTimes[fgPid];
                    while (q.Count > 0 && nowMs - q.Peek() > 2000.0) q.Dequeue();
                    if (q.Count >= 4) { chosenPid = fgPid; stamps = q.ToArray(); }
                }

                if (stamps == null)
                {
                    int bestCount = 3;
                    foreach (KeyValuePair<uint, Queue<double>> kv in _pidPresentTimes)
                    {
                        while (kv.Value.Count > 0 && nowMs - kv.Value.Peek() > 2000.0) kv.Value.Dequeue();
                        if (kv.Value.Count > bestCount)
                        {
                            bestCount = kv.Value.Count;
                            chosenPid = kv.Key;
                            stamps = kv.Value.ToArray();
                        }
                    }
                }
            }

            if (stamps == null || stamps.Length < 3) return;

            // Count frames in last 1000ms for responsive live FPS + frame deltas for 1% Low
            int recentCount = 0;
            List<double> deltas = new List<double>(stamps.Length);
            for (int i = 0; i < stamps.Length; i++)
            {
                if (nowMs - stamps[i] <= 1000.0) recentCount++;
                if (i > 0)
                {
                    double dt = stamps[i] - stamps[i - 1];
                    if (dt > 0.2 && dt < 500.0) deltas.Add(dt);
                }
            }

            if (recentCount < 2 || deltas.Count < 2) return;
            fps = recentCount;

            double sumDt = 0;
            for (int i = 0; i < deltas.Count; i++) sumDt += deltas[i];
            frametimeMs = (float)(sumDt / deltas.Count);

            deltas.Sort();
            int idx99 = Math.Min(deltas.Count - 1, (int)Math.Floor(deltas.Count * 0.99));
            double p99Dt = deltas[idx99];
            fps1Low = p99Dt > 0.2 ? (float)Math.Min(fps, 1000.0 / p99Dt) : fps;

            try
            {
                using (Process p = Process.GetProcessById((int)chosenPid))
                    procName = p.ProcessName + ".exe";
            }
            catch { procName = "PID " + chosenPid; }
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
