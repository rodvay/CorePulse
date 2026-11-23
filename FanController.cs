using System;
using System.Collections.Generic;

namespace CorePulse
{
    internal sealed class FanController
    {
        public const float ThermalSafetyCpuC = 88.0f;
        public const float ThermalSafetyGpuC = 83.0f;

        private readonly Dictionary<string, float> _lastAppliedTemp = new Dictionary<string, float>();
        private readonly Dictionary<string, int> _lastAppliedDuty = new Dictionary<string, int>();

        public bool SafetyOverrideActive { get; private set; }

        public static void ApplyPresetToSettings(CorePulseSettings cfg, FanProfileMode mode)
        {
            cfg.FanProfile = mode;
            if (mode == FanProfileMode.Silent)
            {
                cfg.CurveTemps = new int[] { 40, 55, 70, 80, 86 };
                cfg.CurveDuties = new int[] { 30, 42, 60, 82, 100 };
            }
            else if (mode == FanProfileMode.Balanced)
            {
                cfg.CurveTemps = new int[] { 35, 50, 65, 75, 84 };
                cfg.CurveDuties = new int[] { 35, 50, 70, 88, 100 };
            }
            else if (mode == FanProfileMode.Turbo)
            {
                cfg.CurveTemps = new int[] { 30, 45, 58, 70, 80 };
                cfg.CurveDuties = new int[] { 55, 70, 85, 100, 100 };
            }
        }

        public static int EvaluateCurve(float tempC, int[] temps, int[] duties)
        {
            if (temps == null || duties == null || temps.Length != 5 || duties.Length != 5)
                return 55;
            if (tempC <= temps[0]) return ClampDuty(duties[0]);
            if (tempC >= temps[4]) return ClampDuty(duties[4]);

            for (int i = 0; i < 4; i++)
            {
                if (tempC >= temps[i] && tempC <= temps[i + 1])
                {
                    float span = Math.Max(1f, temps[i + 1] - temps[i]);
                    float t = (tempC - temps[i]) / span;
                    float duty = duties[i] + t * (duties[i + 1] - duties[i]);
                    return ClampDuty((int)Math.Round(duty));
                }
            }
            return ClampDuty(duties[4]);
        }

        public static int ClampDuty(int pct)
        {
            if (pct < 25) return 25;
            if (pct > 100) return 100;
            return pct;
        }

        public void RestoreSavedSettingsOnStartup(CorePulseSettings cfg, List<FanChannelInfo> fans, bool demoMode)
        {
            if (cfg == null || fans == null) return;
            foreach (FanChannelInfo f in fans)
            {
                if (!f.HasControl) continue;
                int savedMode;
                if (cfg.SavedFanModes.TryGetValue(f.Id, out savedMode))
                    f.Mode = savedMode;
                else if (cfg.FanProfile != FanProfileMode.BiosAuto)
                    f.Mode = 2; // Apply saved global curve automatically!

                int savedMan;
                if (cfg.SavedFanManuals.TryGetValue(f.Id, out savedMan))
                    f.ManualDutyPct = ClampDuty(savedMan);
            }
        }

        public void ApplyGlobalProfile(CorePulseSettings cfg, List<FanChannelInfo> fans, FanProfileMode mode, bool demoMode)
        {
            ApplyPresetToSettings(cfg, mode);
            if (fans == null) return;

            foreach (FanChannelInfo f in fans)
            {
                if (!f.HasControl) continue;
                if (mode == FanProfileMode.BiosAuto)
                {
                    f.Mode = 0;
                    cfg.SavedFanModes[f.Id] = 0;
                    if (!demoMode) SetFanDefaultSafe(f);
                }
                else
                {
                    f.Mode = 2;
                    cfg.SavedFanModes[f.Id] = 2;
                }
            }
            _lastAppliedTemp.Clear();
            _lastAppliedDuty.Clear();
        }

        public void Tick(CorePulseSettings cfg, TelemetrySnapshot snap, bool demoMode)
        {
            if (snap == null || snap.Fans == null || snap.Fans.Count == 0)
            {
                SafetyOverrideActive = false;
                return;
            }

            bool safety = (snap.CpuTempC >= ThermalSafetyCpuC) || (snap.GpuTempC >= ThermalSafetyGpuC);
            SafetyOverrideActive = safety;
            snap.ThermalSafetyOverride = safety;

            foreach (FanChannelInfo f in snap.Fans)
            {
                if (!f.HasControl) continue;

                if (safety)
                {
                    if (!demoMode) SetFanSoftwareSafe(f, 100);
                    else f.CurrentDutyPct = 100;
                    continue;
                }

                if (f.Mode == 0)
                {
                    if (_lastAppliedDuty.ContainsKey(f.Id))
                    {
                        if (!demoMode) SetFanDefaultSafe(f);
                        _lastAppliedDuty.Remove(f.Id);
                        _lastAppliedTemp.Remove(f.Id);
                    }
                }
                else if (f.Mode == 1)
                {
                    int target = ClampDuty(f.ManualDutyPct);
                    int prev;
                    if (!_lastAppliedDuty.TryGetValue(f.Id, out prev) || prev != target)
                    {
                        if (!demoMode) SetFanSoftwareSafe(f, target);
                        else f.CurrentDutyPct = target;
                        _lastAppliedDuty[f.Id] = target;
                    }
                }
                else if (f.Mode == 2)
                {
                    float srcTemp;
                    if (f.IsGpuFan)
                        srcTemp = snap.GpuTempC > 1f ? snap.GpuTempC : 55f;
                    else if (cfg.CurveFollowsGpu && snap.GpuTempC > 1f)
                        srcTemp = snap.GpuTempC;
                    else
                        srcTemp = Math.Max(snap.CpuTempC > 1f ? snap.CpuTempC : 45f, snap.GpuTempC > 1f ? snap.GpuTempC * 0.85f : 40f);

                    float lastT;
                    if (_lastAppliedTemp.TryGetValue(f.Id, out lastT) && Math.Abs(srcTemp - lastT) < 2.0f && _lastAppliedDuty.ContainsKey(f.Id))
                        continue;

                    int target = EvaluateCurve(srcTemp, cfg.CurveTemps, cfg.CurveDuties);
                    if (!demoMode) SetFanSoftwareSafe(f, target);
                    else f.CurrentDutyPct = target;
                    _lastAppliedTemp[f.Id] = srcTemp;
                    _lastAppliedDuty[f.Id] = target;
                }
            }
        }

        public void RestoreAllToBiosAuto(List<FanChannelInfo> fans, bool demoMode)
        {
            if (fans == null) return;
            foreach (FanChannelInfo f in fans)
            {
                if (!f.HasControl) continue;
                f.Mode = 0;
                if (!demoMode) SetFanDefaultSafe(f);
            }
            _lastAppliedTemp.Clear();
            _lastAppliedDuty.Clear();
            SafetyOverrideActive = false;
        }

        private static void SetFanSoftwareSafe(FanChannelInfo f, int pct)
        {
            if (f != null && f.NativeControl != null && LibResolver.IsLhmDllPresent())
                LhmBackend.SetFanSoftware(f.NativeControl, pct);
        }

        private static void SetFanDefaultSafe(FanChannelInfo f)
        {
            if (f != null && f.NativeControl != null && LibResolver.IsLhmDllPresent())
                LhmBackend.SetFanDefault(f.NativeControl);
        }
    }
}
