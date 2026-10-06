using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace JonsboCanvas
{
    internal sealed class MetricsSnapshot
    {
        public DateTime Timestamp;
        public double CpuUsage;
        public double? CpuTemperature;
        public double? CpuPower;
        public double MemoryUsage;
        public double MemoryUsedGb;
        public double MemoryTotalGb;
        public double? GpuUsage;
        public double? GpuTemperature;
        public double? GpuMemoryUsage;
        public double? GpuMemoryUsedGb;
        public double? GpuMemoryTotalGb;
        public double? GpuPower;
        public string CpuName;
        public string GpuName;
        public TimeSpan Uptime;
        public DiskSnapshot[] Disks = Array.Empty<DiskSnapshot>();
    }

    internal sealed class DiskSnapshot
    {
        public string Name;
        public string Label;
        public double UsedGb;
        public double TotalGb;
        public double Usage => TotalGb > 0 ? 100.0 * UsedGb / TotalGb : 0;
    }

    internal sealed class MetricsCollector : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct FileTime
        {
            public uint Low;
            public uint High;

            public ulong Value { get { return ((ulong)High << 32) | Low; } }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MemoryStatus
        {
            public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            public uint MemoryLoad;
            public ulong TotalPhysical;
            public ulong AvailablePhysical;
            public ulong TotalPageFile;
            public ulong AvailablePageFile;
            public ulong TotalVirtual;
            public ulong AvailableVirtual;
            public ulong AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        private readonly CpuidSensors _cpuid;
        private FileTime _previousIdle;
        private FileTime _previousKernel;
        private FileTime _previousUser;
        private bool _hasPreviousCpu;
        private string _cpuName;
        private string _gpuName;
        private DiskSnapshot[] _disks = Array.Empty<DiskSnapshot>();
        private DateTime _nextDiskRead = DateTime.MinValue;
        private bool _loggedCpuSensors;

        public MetricsCollector(bool enableCpuidSensors)
        {
            _cpuName = ReadCpuName();
            _cpuid = new CpuidSensors();
            if (enableCpuidSensors)
                _cpuid.TryInitialize();
        }

        public MetricsSnapshot Collect()
        {
            MetricsSnapshot snapshot = new MetricsSnapshot();
            snapshot.Timestamp = DateTime.Now;
            snapshot.CpuName = _cpuName;
            snapshot.CpuUsage = ReadCpuUsage();
            ReadMemory(snapshot);
            snapshot.Uptime = TimeSpan.FromMilliseconds(GetTickCount64());

            try
            {
                _cpuid.Refresh();
                snapshot.CpuTemperature = ValidSensor(_cpuid.ReadCoreTemperature());
                if (!snapshot.CpuTemperature.HasValue)
                    snapshot.CpuTemperature = ValidSensor(_cpuid.ReadSensor(0x01802000));
                if (!snapshot.CpuTemperature.HasValue)
                    snapshot.CpuTemperature = ValidSensor(_cpuid.ReadSensor(0x02802000));
                snapshot.CpuPower = ValidSensor(_cpuid.ReadSensor(0x00405000));
                if (!_loggedCpuSensors && (snapshot.CpuTemperature.HasValue || snapshot.CpuPower.HasValue))
                {
                    _loggedCpuSensors = true;
                    Log.Write(string.Format(CultureInfo.InvariantCulture, "CPUID sensors: temperature={0}, power={1}",
                        snapshot.CpuTemperature, snapshot.CpuPower));
                }
            }
            catch (Exception exception)
            {
                Log.Write("CPUID sample failed: " + exception.Message);
            }

            ReadNvidia(snapshot);
            snapshot.GpuName = string.IsNullOrWhiteSpace(_gpuName) ? "NVIDIA GPU" : _gpuName;
            snapshot.Disks = ReadDisks();
            return snapshot;
        }

        // Capacity changes slowly, and querying a sleeping hard disk wakes it up.
        private DiskSnapshot[] ReadDisks()
        {
            if (DateTime.UtcNow < _nextDiskRead)
                return _disks;
            _nextDiskRead = DateTime.UtcNow.AddSeconds(30);
            try
            {
                const double gigabyte = 1024.0 * 1024.0 * 1024.0;
                _disks = DriveInfo.GetDrives()
                    .Where(drive => drive.DriveType == DriveType.Fixed && drive.IsReady)
                    .OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .Select(drive => new DiskSnapshot
                    {
                        Name = drive.Name.TrimEnd('\\'),
                        Label = drive.VolumeLabel,
                        TotalGb = drive.TotalSize / gigabyte,
                        UsedGb = (drive.TotalSize - drive.TotalFreeSpace) / gigabyte,
                    })
                    .ToArray();
            }
            catch (Exception exception)
            {
                Log.Write("Disk sample failed: " + exception.Message);
            }
            return _disks;
        }

        private double ReadCpuUsage()
        {
            FileTime idle;
            FileTime kernel;
            FileTime user;
            if (!GetSystemTimes(out idle, out kernel, out user))
                return 0;

            if (!_hasPreviousCpu)
            {
                _previousIdle = idle;
                _previousKernel = kernel;
                _previousUser = user;
                _hasPreviousCpu = true;
                return 0;
            }

            ulong idleDelta = idle.Value - _previousIdle.Value;
            ulong kernelDelta = kernel.Value - _previousKernel.Value;
            ulong userDelta = user.Value - _previousUser.Value;
            ulong total = kernelDelta + userDelta;

            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;

            if (total == 0)
                return 0;
            return Clamp(100.0 * (total - idleDelta) / total, 0, 100);
        }

        private static void ReadMemory(MetricsSnapshot snapshot)
        {
            MemoryStatus status = new MemoryStatus();
            if (!GlobalMemoryStatusEx(status))
                return;
            const double gigabyte = 1024.0 * 1024.0 * 1024.0;
            snapshot.MemoryTotalGb = status.TotalPhysical / gigabyte;
            snapshot.MemoryUsedGb = (status.TotalPhysical - status.AvailablePhysical) / gigabyte;
            snapshot.MemoryUsage = status.MemoryLoad;
        }

        private void ReadNvidia(MetricsSnapshot snapshot)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
                info.Arguments = "--query-gpu=name,utilization.gpu,temperature.gpu,memory.used,memory.total,power.draw --format=csv,noheader,nounits";
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;

                using (Process process = Process.Start(info))
                {
                    string line = process.StandardOutput.ReadLine();
                    if (!process.WaitForExit(1800))
                    {
                        try { process.Kill(); } catch { }
                        return;
                    }
                    if (string.IsNullOrWhiteSpace(line))
                        return;

                    string[] parts = line.Split(',');
                    if (parts.Length < 6)
                        return;
                    _gpuName = parts[0].Trim();
                    snapshot.GpuUsage = Parse(parts[1]);
                    snapshot.GpuTemperature = Parse(parts[2]);
                    double? usedMb = Parse(parts[3]);
                    double? totalMb = Parse(parts[4]);
                    snapshot.GpuPower = Parse(parts[5]);
                    if (usedMb.HasValue && totalMb.HasValue && totalMb.Value > 0)
                    {
                        snapshot.GpuMemoryUsedGb = usedMb.Value / 1024.0;
                        snapshot.GpuMemoryTotalGb = totalMb.Value / 1024.0;
                        snapshot.GpuMemoryUsage = Clamp(100.0 * usedMb.Value / totalMb.Value, 0, 100);
                    }
                }
            }
            catch (Exception exception)
            {
                Log.Write("nvidia-smi sample failed: " + exception.Message);
            }
        }

        private static string ReadCpuName()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                {
                    object value = key == null ? null : key.GetValue("ProcessorNameString");
                    if (value != null)
                        return value.ToString().Trim();
                }
            }
            catch { }
            return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "CPU";
        }

        private static double? Parse(string value)
        {
            double parsed;
            if (double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                return parsed;
            return null;
        }

        private static double? ValidSensor(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1000)
                return null;
            return value;
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        public void Dispose()
        {
            _cpuid.Dispose();
        }
    }

    internal sealed class CpuidSensors : IDisposable
    {
        [DllImport("cpuidsdk.dll", EntryPoint = "QueryInterface", CallingConvention = CallingConvention.Winapi)]
        private static extern IntPtr QueryInterface(uint code);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr CreateInstanceDelegate();
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void DestroyInstanceDelegate(IntPtr instance);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int InitDelegate(IntPtr instance, int flags, ref int error, ref int extendedError);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void CloseDelegate(IntPtr instance);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void RefreshDelegate(IntPtr instance);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate float ReadSensorDelegate(IntPtr instance, int sensorType, ref int deviceIndex, ref int sensorIndex);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate float ReadCoreTemperatureDelegate(IntPtr instance, int processorIndex, int coreIndex);

        private IntPtr _instance;
        private DestroyInstanceDelegate _destroy;
        private CloseDelegate _close;
        private RefreshDelegate _refresh;
        private ReadSensorDelegate _readSensor;
        private ReadCoreTemperatureDelegate _readCoreTemperature;
        private bool _initialized;

        public bool TryInitialize()
        {
            try
            {
                CreateInstanceDelegate create = GetDelegate<CreateInstanceDelegate>(1418504473u);
                _destroy = GetDelegate<DestroyInstanceDelegate>(341059752u);
                InitDelegate init = GetDelegate<InitDelegate>(1961421265u);
                _close = GetDelegate<CloseDelegate>(unchecked((uint)-680415517));
                _refresh = GetDelegate<RefreshDelegate>(2113600501u);
                _readSensor = GetDelegate<ReadSensorDelegate>(1094091373u);
                _readCoreTemperature = GetDelegate<ReadCoreTemperatureDelegate>(3574311447u);
                if (create == null || init == null || _refresh == null || _readSensor == null)
                    return false;

                _instance = create();
                if (_instance == IntPtr.Zero)
                    return false;

                const int flags = int.MaxValue;
                int error = 0;
                int extendedError = 0;
                int result = init(_instance, flags, ref error, ref extendedError);
                _initialized = result == 1;
                Log.Write(string.Format("CPUID init result={0}, error={1}, extended={2}", result, error, extendedError));
                return _initialized;
            }
            catch (Exception exception)
            {
                Log.Write("CPUID init failed: " + exception);
                return false;
            }
        }

        public void Refresh()
        {
            if (_initialized && _refresh != null)
                _refresh(_instance);
        }

        public float ReadSensor(int sensorType)
        {
            if (!_initialized || _readSensor == null)
                return -1;
            int device = -1;
            int sensor = -1;
            return _readSensor(_instance, sensorType, ref device, ref sensor);
        }

        public float ReadCoreTemperature()
        {
            if (!_initialized || _readCoreTemperature == null)
                return -1;

            float hottest = -1;
            for (int core = 0; core < 64; core++)
            {
                float value = _readCoreTemperature(_instance, 0, core);
                if (!float.IsNaN(value) && !float.IsInfinity(value) && value >= 0 && value < 150)
                    hottest = Math.Max(hottest, value);
            }
            return hottest;
        }

        private static T GetDelegate<T>(uint code) where T : class
        {
            IntPtr pointer = QueryInterface(code);
            if (pointer == IntPtr.Zero)
                return null;
            return Marshal.GetDelegateForFunctionPointer(pointer, typeof(T)) as T;
        }

        public void Dispose()
        {
            try
            {
                if (_initialized && _close != null)
                    _close(_instance);
            }
            catch { }
            try
            {
                if (_instance != IntPtr.Zero && _destroy != null)
                    _destroy(_instance);
            }
            catch { }
            _instance = IntPtr.Zero;
            _initialized = false;
        }
    }
}
