param(
    [ValidateRange(1, 60)][int]$Samples = 5,
    [string]$OutputPath = (Join-Path (Get-Location) 'DesktopPuppy-startup-diagnostics.json')
)

# Read-only probe of the 0.1.0 fullscreen policy. Does not launch/stop applications,
# read window titles/database contents, change settings, or force windows visible.
$ErrorActionPreference = 'Stop'
if (-not ('DesktopPuppyStartupProbe.Native' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
namespace DesktopPuppyStartupProbe {
    public static class Native {
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
        public struct MonitorInfo { public int Size; public Rect Bounds, WorkArea; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string Device; }
        public class WindowInfo {
            public string Handle, ProcessName, ClassName, Style, ExtendedStyle;
            public uint ProcessId; public Rect Bounds;
            public bool Visible, Minimized, Cloaked, Framed, Desktop, CoversMonitor, Candidate;
            public int CloakResult;
        }
        public class Snapshot {
            public string AtUtc;
            public int ShellResult, ShellState;
            public bool WouldSuppress;
            public List<MonitorInfo> Monitors = new List<MonitorInfo>();
            public List<WindowInfo> Windows = new List<WindowInfo>();
        }
        delegate bool EnumProc(IntPtr hwnd, IntPtr data);
        delegate bool MonitorProc(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorProc callback, IntPtr data);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint="GetClassNameW", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
        [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        static long Style(IntPtr hwnd, int index) { return IntPtr.Size == 8 ? GetWindowLongPtr(hwnd,index).ToInt64() : GetWindowLong(hwnd,index); }
        static bool Covers(Rect a, Rect b) {
            return Math.Abs((long)a.Left-b.Left)<=1 && Math.Abs((long)a.Top-b.Top)<=1 &&
                   Math.Abs((long)a.Right-b.Right)<=1 && Math.Abs((long)a.Bottom-b.Bottom)<=1;
        }
        public static Snapshot Capture() {
            IntPtr old = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try {
                Snapshot s = new Snapshot { AtUtc=DateTime.UtcNow.ToString("o") };
                int state; s.ShellResult=SHQueryUserNotificationState(out state); s.ShellState=state;
                MonitorProc monitorCallback = delegate(IntPtr m, IntPtr dc, ref Rect rect, IntPtr data) {
                    MonitorInfo info = new MonitorInfo { Size=Marshal.SizeOf(typeof(MonitorInfo)) };
                    if (!GetMonitorInfo(m,ref info)) throw new InvalidOperationException("GetMonitorInfo failed");
                    s.Monitors.Add(info); return true;
                };
                if (!EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,monitorCallback,IntPtr.Zero) || s.Monitors.Count==0)
                    throw new InvalidOperationException("Monitor enumeration failed");
                EnumProc callback = delegate(IntPtr hwnd, IntPtr data) {
                    uint pid; GetWindowThreadProcessId(hwnd,out pid);
                    string processName; try { using (Process p=Process.GetProcessById((int)pid)) processName=p.ProcessName; } catch { processName="unavailable"; }
                    bool visible=IsWindowVisible(hwnd), minimized=IsIconic(hwnd);
                    Rect rect; if (!GetWindowRect(hwnd,out rect)) return true;
                    StringBuilder name = new StringBuilder(256); GetClassName(hwnd,name,name.Capacity);
                    int cloaked; int cloakResult=DwmGetWindowAttribute(hwnd,14,out cloaked,sizeof(int));
                    long style=Style(hwnd,-16);
                    bool framed=(style & (0x00C00000L|0x00040000L|0x01000000L))!=0;
                    string cls=name.ToString();
                    bool desktop=hwnd==GetShellWindow() || hwnd==GetDesktopWindow() || cls=="WorkerW" || cls=="Progman" || cls=="Shell_TrayWnd" || cls=="Shell_SecondaryTrayWnd";
                    bool covers=false; foreach(MonitorInfo m in s.Monitors) if(Covers(m.Bounds,rect)) covers=true;
                    bool pet=processName.Equals("DesktopPuppy",StringComparison.OrdinalIgnoreCase) || cls.StartsWith("HwndWrapper[DesktopPuppy;",StringComparison.Ordinal);
                    bool candidate=visible && !minimized && cloaked==0 && !framed && !desktop && covers && !pet;
                    // Retain screen-covering windows and all pet HWNDs, including hidden ones.
                    if (covers || pet)
                        s.Windows.Add(new WindowInfo { Handle="0x"+hwnd.ToInt64().ToString("X"), ProcessName=processName, ProcessId=pid, ClassName=cls,
                            Style="0x"+style.ToString("X"), ExtendedStyle="0x"+Style(hwnd,-20).ToString("X"), Bounds=rect,
                            Visible=visible, Minimized=minimized, Cloaked=cloaked!=0, CloakResult=cloakResult, Framed=framed, Desktop=desktop, CoversMonitor=covers, Candidate=candidate });
                    if(candidate) s.WouldSuppress=true;
                    return true;
                };
                EnumWindows(callback,IntPtr.Zero);
                if(s.ShellResult==0 && s.ShellState==3) s.WouldSuppress=true;
                GC.KeepAlive(callback); GC.KeepAlive(monitorCallback); return s;
            } finally { if(old!=IntPtr.Zero) SetThreadDpiAwarenessContext(old); }
        }
    }
}
'@
}

$snapshots = @(
    for ($sample = 0; $sample -lt $Samples; $sample++) {
        [DesktopPuppyStartupProbe.Native]::Capture()
        if ($sample + 1 -lt $Samples) { Start-Sleep -Seconds 1 }
    }
)
$report = [ordered]@{
    PolicyVersion = 'DesktopPuppy 0.1.0 / cdcb6d5'
    Note = 'External observation, not internal application state. No window titles or database contents collected.'
    Processes = @(Get-Process -Name DesktopPuppy -ErrorAction SilentlyContinue | ForEach-Object {
        [ordered]@{ Id = $_.Id; FileVersion = $_.FileVersion; ProductVersion = $_.ProductVersion }
    })
    Snapshots = $snapshots
}
$report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Output ([IO.Path]::GetFullPath($OutputPath))
$snapshots | ForEach-Object {
    Write-Output ("{0} ShellState={1} WouldSuppress={2}" -f $_.AtUtc, $_.ShellState, $_.WouldSuppress)
    $_.Windows | Where-Object Candidate | Select-Object Handle, ProcessId, ProcessName, ClassName
}
