using System.Runtime.InteropServices;
using XantaComfyLauncher.Models;

namespace XantaComfyLauncher.Services;

public sealed class SystemStatusService
{
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _hasCpuSample;

    public SystemStatusSnapshot GetSnapshot()
    {
        try
        {
            var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref memory))
                throw new InvalidOperationException("读取系统内存失败：" + Marshal.GetLastWin32Error());

            var totalMb = (int)(memory.TotalPhys / 1024 / 1024);
            var usedMb = (int)((memory.TotalPhys - memory.AvailPhys) / 1024 / 1024);
            var percent = totalMb > 0 ? (int)Math.Round(usedMb * 100.0 / totalMb) : 0;
            return new SystemStatusSnapshot(ReadCpuPercent(), usedMb, totalMb, percent);
        }
        catch (Exception ex)
        {
            return new SystemStatusSnapshot(0, 0, 0, 0, ex.Message);
        }
    }

    private int ReadCpuPercent()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            throw new InvalidOperationException("读取 CPU 使用率失败：" + Marshal.GetLastWin32Error());

        var idle = ToUInt64(idleTime);
        var kernel = ToUInt64(kernelTime);
        var user = ToUInt64(userTime);
        if (!_hasCpuSample)
        {
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
            _hasCpuSample = true;
            return 0;
        }

        var idleDiff = idle - _previousIdle;
        var kernelDiff = kernel - _previousKernel;
        var userDiff = user - _previousUser;
        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;

        // Kernel time includes idle time on Windows.
        var total = kernelDiff + userDiff;
        if (total == 0) return 0;
        return (int)Math.Clamp(Math.Round((total - idleDiff) * 100.0 / total), 0, 100);
    }

    private static ulong ToUInt64(FileTime time) =>
        ((ulong)time.HighDateTime << 32) | time.LowDateTime;

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
