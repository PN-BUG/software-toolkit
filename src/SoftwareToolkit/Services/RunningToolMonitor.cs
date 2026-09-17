using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SoftwareToolkit.Models;

namespace SoftwareToolkit.Services;

/// <summary>
/// Tracks processes launched by SoftwareToolkit and samples their CPU and memory usage.
/// All public methods are expected to be called from the UI thread.
/// </summary>
public sealed class RunningToolMonitor : IDisposable
{
    private const uint Th32csSnapProcess = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExecutableFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private sealed class TrackedProcess
    {
        public required Process Process { get; init; }
        public required RunningToolProcess Item { get; init; }
        public required TimeSpan LastCpuTime { get; set; }
        public required DateTime LastSampleAt { get; set; }
    }

    private readonly Dictionary<int, TrackedProcess> _tracked = new();

    public ObservableCollection<RunningToolProcess> Items { get; } = new();
    public double TotalCpuPercent { get; private set; }
    public long TotalMemoryBytes { get; private set; }

    public void Track(ToolDefinition tool, Process process)
    {
        try
        {
            if (process.HasExited || _tracked.ContainsKey(process.Id))
            {
                process.Dispose();
                return;
            }

            process.Refresh();
            var now = DateTime.Now;
            var startedAt = TryGetStartTime(process, now);
            ReadProcessTreeMetrics(process, CaptureProcessTree(), out var initialCpu, out var initialMemory);
            var item = new RunningToolProcess(tool, process.Id, SafeProcessName(process), startedAt)
            {
                MemoryBytes = initialMemory,
                RunningTime = now - startedAt
            };

            var tracked = new TrackedProcess
            {
                Process = process,
                Item = item,
                LastCpuTime = initialCpu,
                LastSampleAt = now
            };
            _tracked.Add(process.Id, tracked);
            Items.Insert(0, item);
            RecalculateTotals();
        }
        catch (InvalidOperationException)
        {
            process.Dispose();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            process.Dispose();
        }
    }

    public void Refresh()
    {
        var now = DateTime.Now;
        var stopped = new List<int>();
        var childrenByParent = CaptureProcessTree();

        foreach (var pair in _tracked)
        {
            var tracked = pair.Value;
            try
            {
                if (tracked.Process.HasExited)
                {
                    stopped.Add(pair.Key);
                    continue;
                }

                tracked.Process.Refresh();
                ReadProcessTreeMetrics(tracked.Process, childrenByParent, out var currentCpu, out var currentMemory);
                var elapsedMs = (now - tracked.LastSampleAt).TotalMilliseconds;
                var cpuMs = (currentCpu - tracked.LastCpuTime).TotalMilliseconds;
                tracked.Item.CpuPercent = elapsedMs <= 0
                    ? 0
                    : Math.Clamp(cpuMs / elapsedMs / Environment.ProcessorCount * 100, 0, 100);
                tracked.Item.MemoryBytes = currentMemory;
                tracked.Item.RunningTime = now - tracked.Item.StartedAt;
                tracked.LastCpuTime = currentCpu;
                tracked.LastSampleAt = now;
            }
            catch (InvalidOperationException)
            {
                stopped.Add(pair.Key);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The process is still running but its counters are inaccessible (for example elevated tools).
                tracked.Item.CpuPercent = 0;
                tracked.Item.RunningTime = now - tracked.Item.StartedAt;
            }
        }

        foreach (var processId in stopped)
            Remove(processId);

        RecalculateTotals();
    }

    public bool Stop(RunningToolProcess item)
    {
        if (!_tracked.TryGetValue(item.ProcessId, out var tracked)) return false;
        try
        {
            if (!tracked.Process.HasExited)
            {
                tracked.Process.Kill(entireProcessTree: true);
                tracked.Process.WaitForExit(1500);
            }
            Remove(item.ProcessId);
            RecalculateTotals();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    private void Remove(int processId)
    {
        if (!_tracked.Remove(processId, out var tracked)) return;
        Items.Remove(tracked.Item);
        tracked.Process.Dispose();
    }

    private void RecalculateTotals()
    {
        TotalCpuPercent = Math.Clamp(Items.Sum(item => item.CpuPercent), 0, 100);
        TotalMemoryBytes = Items.Sum(item => item.MemoryBytes);
    }

    private static DateTime TryGetStartTime(Process process, DateTime fallback)
    {
        try { return process.StartTime; }
        catch { return fallback; }
    }

    private static void ReadProcessTreeMetrics(
        Process root,
        IReadOnlyDictionary<int, List<int>> childrenByParent,
        out TimeSpan cpuTime,
        out long memoryBytes)
    {
        cpuTime = TimeSpan.Zero;
        memoryBytes = 0;
        foreach (var processId in GetProcessTreeIds(root.Id, childrenByParent))
        {
            Process? process = null;
            try
            {
                process = processId == root.Id ? root : Process.GetProcessById(processId);
                process.Refresh();
                cpuTime += process.TotalProcessorTime;
                memoryBytes += process.WorkingSet64;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
            {
                // A child can exit between the snapshot and reading its counters.
            }
            finally
            {
                if (process != null && !ReferenceEquals(process, root)) process.Dispose();
            }
        }
    }

    private static Dictionary<int, List<int>> CaptureProcessTree()
    {
        var childrenByParent = new Dictionary<int, List<int>>();
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == InvalidHandleValue) return childrenByParent;

        try
        {
            var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    var parentId = unchecked((int)entry.ParentProcessId);
                    if (!childrenByParent.TryGetValue(parentId, out var children))
                        childrenByParent[parentId] = children = new List<int>();
                    children.Add(unchecked((int)entry.ProcessId));
                    entry.Size = (uint)Marshal.SizeOf<ProcessEntry32>();
                }
                while (Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return childrenByParent;
    }

    private static HashSet<int> GetProcessTreeIds(int rootId, IReadOnlyDictionary<int, List<int>> childrenByParent)
    {
        var result = new HashSet<int> { rootId };
        var pending = new Queue<int>();
        pending.Enqueue(rootId);
        while (pending.Count > 0)
        {
            var parentId = pending.Dequeue();
            if (!childrenByParent.TryGetValue(parentId, out var children)) continue;
            foreach (var childId in children)
                if (result.Add(childId)) pending.Enqueue(childId);
        }
        return result;
    }

    private static string SafeProcessName(Process process)
    {
        try { return process.ProcessName; }
        catch { return "process"; }
    }

    public void Dispose()
    {
        foreach (var tracked in _tracked.Values)
            tracked.Process.Dispose();
        _tracked.Clear();
        Items.Clear();
    }
}
