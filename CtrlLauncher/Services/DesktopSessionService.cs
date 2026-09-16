using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CtrlLauncher.Services;

/// <summary>
/// ランチャーを一時的なWin32デスクトップ上で実行します。
/// P/Invokeをこのクラスに隔離し、通常のUIやViewModelからWin32依存を分離しています。
/// </summary>
public sealed partial class DesktopSessionService(FileLogService log)
{
    private const uint DesktopAllAccess = 0x000F01FF;
    private const uint CreateUnicodeEnvironment = 0x00000400;

    public async Task RunAsync()
    {
        var desktopName = $"CtrlLauncher-{Guid.NewGuid():N}";
        var readyEventName = $"Local\\CtrlLauncherReady-{Guid.NewGuid():N}";
        var defaultDesktop = NativeMethods.OpenDesktop("Default", 0, false, DesktopAllAccess);
        if (defaultDesktop == IntPtr.Zero) ThrowLastWin32("通常デスクトップを開けませんでした。");

        var exhibitionDesktop = NativeMethods.CreateDesktop(desktopName, null, IntPtr.Zero, 0, DesktopAllAccess, IntPtr.Zero);
        if (exhibitionDesktop == IntPtr.Zero)
        {
            NativeMethods.CloseDesktop(defaultDesktop);
            ThrowLastWin32("展示用デスクトップを作成できませんでした。");
        }

        try
        {
            using var readyEvent = new EventWaitHandle(false, EventResetMode.ManualReset, readyEventName);
            using var child = StartChildProcess(desktopName, readyEventName);
            log.Info($"展示用デスクトップを開始しました: {desktopName} (PID {child.Id})");
            var readyTask = Task.Run(() => readyEvent.WaitOne(TimeSpan.FromSeconds(15)));
            var exitTask = child.WaitForExitAsync();
            var firstCompleted = await Task.WhenAny(readyTask, exitTask);

            if (firstCompleted == exitTask)
            {
                readyEvent.Set();
                await readyTask;
                throw new InvalidOperationException("展示用ランチャーが画面を表示する前に終了しました。");
            }

            if (!await readyTask)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
                throw new TimeoutException("展示用ランチャーの画面準備が15秒以内に完了しませんでした。");
            }

            log.Info("展示用ランチャーの準備完了を確認しました。デスクトップを切り替えます。");
            if (!NativeMethods.SwitchDesktop(exhibitionDesktop))
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                ThrowLastWin32("展示用デスクトップへ切り替えられませんでした。");
            }
            HideInputIndicatorWindows(exhibitionDesktop);
            await Task.Delay(500);
            HideInputIndicatorWindows(exhibitionDesktop);
            while (!exitTask.IsCompleted)
            {
                await Task.Delay(250);
                HideInputIndicatorWindows(exhibitionDesktop);
            }
            await exitTask;
        }
        finally
        {
            if (!NativeMethods.SwitchDesktop(defaultDesktop))
                log.Error("通常デスクトップへの復帰に失敗しました。", new Win32Exception(Marshal.GetLastWin32Error()));
            NativeMethods.CloseDesktop(exhibitionDesktop);
            NativeMethods.CloseDesktop(defaultDesktop);
            log.Info("展示用デスクトップを終了しました。");
        }
    }

    private static Process StartChildProcess(string desktopName, string readyEventName)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("実行ファイルのパスを取得できません。");
        var executablePointer = Marshal.StringToHGlobalUni(executable);
        var commandLinePointer = Marshal.StringToHGlobalUni(
            $"\"{executable}\" --desktop-child --ready-event \"{readyEventName}\"");
        var desktopPointer = Marshal.StringToHGlobalUni(desktopName);
        var workingDirectoryPointer = Marshal.StringToHGlobalUni(AppContext.BaseDirectory);
        var startupInfo = new NativeMethods.StartupInfo
        {
            Size = Marshal.SizeOf<NativeMethods.StartupInfo>(),
            Desktop = desktopPointer
        };
        try
        {
            if (!NativeMethods.CreateProcess(executablePointer, commandLinePointer, IntPtr.Zero, IntPtr.Zero, false,
                    CreateUnicodeEnvironment, IntPtr.Zero, workingDirectoryPointer, ref startupInfo, out var processInfo))
                ThrowLastWin32("展示用ランチャーを起動できませんでした。");

            try { return Process.GetProcessById((int)processInfo.ProcessId); }
            finally
            {
                NativeMethods.CloseHandle(processInfo.Thread);
                NativeMethods.CloseHandle(processInfo.Process);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(executablePointer);
            Marshal.FreeHGlobal(commandLinePointer);
            Marshal.FreeHGlobal(desktopPointer);
            Marshal.FreeHGlobal(workingDirectoryPointer);
        }
    }

    private static void ThrowLastWin32(string message) =>
        throw new Win32Exception(Marshal.GetLastWin32Error(), message);

    private void HideInputIndicatorWindows(IntPtr desktop)
    {
        NativeMethods.EnumDesktopWindows(desktop, (window, _) =>
        {
            var className = NativeMethods.GetWindowClassName(window);
            if (className is "UAC_InputIndicatorOverlayWnd" or "UAC Input Indicator")
                NativeMethods.ShowWindow(window, 0);
            return true;
        }, IntPtr.Zero);
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", EntryPoint = "CreateDesktopW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial IntPtr CreateDesktop(string desktop, string? device, IntPtr deviceMode,
            uint flags, uint desiredAccess, IntPtr securityAttributes);

        [LibraryImport("user32.dll", EntryPoint = "OpenDesktopW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        internal static partial IntPtr OpenDesktop(string desktop, uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit,
            uint desiredAccess);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SwitchDesktop(IntPtr desktop);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseDesktop(IntPtr desktop);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool EnumDesktopWindows(IntPtr desktop, DesktopWindowCallback callback, IntPtr lParam);

        [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int GetClassName(IntPtr window, Span<char> className, int maxCount);

        internal static string GetWindowClassName(IntPtr window)
        {
            Span<char> buffer = stackalloc char[256];
            var length = GetClassName(window, buffer, buffer.Length);
            return new string(buffer[..Math.Max(0, length)]);
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool ShowWindow(IntPtr window, int command);

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        internal delegate bool DesktopWindowCallback(IntPtr window, IntPtr lParam);

        [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CreateProcess(IntPtr applicationName, IntPtr commandLine, IntPtr processAttributes,
            IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint creationFlags,
            IntPtr environment, IntPtr currentDirectory, ref StartupInfo startupInfo, out ProcessInformation processInformation);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        internal struct StartupInfo
        {
            public int Size;
            public IntPtr Reserved;
            public IntPtr Desktop;
            public IntPtr Title;
            public int X;
            public int Y;
            public int XSize;
            public int YSize;
            public int XCountChars;
            public int YCountChars;
            public int FillAttribute;
            public int Flags;
            public short ShowWindow;
            public short Reserved2Size;
            public IntPtr Reserved2;
            public IntPtr StandardInput;
            public IntPtr StandardOutput;
            public IntPtr StandardError;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ProcessInformation
        {
            public IntPtr Process;
            public IntPtr Thread;
            public uint ProcessId;
            public uint ThreadId;
        }
    }
}
