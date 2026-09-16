using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CtrlLauncher.Services;

/// <summary>
/// 展示用デスクトップだけで管理キー列とAlt+Tabを先取りします。
/// フックは同じデスクトップ上でのみ有効で、破棄時に必ず解除します。
/// </summary>
public sealed partial class ExhibitionKeyboardHook : IDisposable
{
    private const int WhKeyboardLowLevel = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSystemKeyDown = 0x0104;
    private const int WmSystemKeyUp = 0x0105;
    private const uint VkTab = 0x09;
    private const uint VkSpace = 0x20;
    private const uint VkLeftWindows = 0x5B;
    private const uint VkRightWindows = 0x5C;
    private const uint AltDownFlag = 0x20;
    private readonly Func<bool> _shouldCaptureAdminKeys;
    private readonly Action<char?> _adminKeyPressed;
    private readonly Action _altTabPressed;
    private readonly HookProcedure _hookProcedure;
    private IntPtr _hook;
    private bool _windowsKeyDown;

    public ExhibitionKeyboardHook(Func<bool> shouldCaptureAdminKeys, Action<char?> adminKeyPressed, Action altTabPressed)
    {
        _shouldCaptureAdminKeys = shouldCaptureAdminKeys;
        _adminKeyPressed = adminKeyPressed;
        _altTabPressed = altTabPressed;
        _hookProcedure = HookCallback;
    }

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = NativeMethods.SetWindowsHookEx(WhKeyboardLowLevel, _hookProcedure,
            NativeMethods.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "展示モードのキーボード制御を開始できませんでした。");
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        GC.SuppressFinalize(this);
    }

    private IntPtr HookCallback(int code, IntPtr messagePointer, IntPtr dataPointer)
    {
        if (code < 0)
            return NativeMethods.CallNextHookEx(_hook, code, messagePointer, dataPointer);

        var message = unchecked((int)messagePointer.ToInt64());
        var isDown = message is WmKeyDown or WmSystemKeyDown;
        var isUp = message is WmKeyUp or WmSystemKeyUp;
        var data = Marshal.PtrToStructure<KeyboardData>(dataPointer);

        if (data.VirtualKey is VkLeftWindows or VkRightWindows)
        {
            if (isDown) _windowsKeyDown = true;
            if (isUp) _windowsKeyDown = false;
            return (IntPtr)1;
        }

        // The shell's language switcher is invoked by Win+Space before WPF
        // receives any input.  Use the physical key state as a second source
        // of truth because a Win-key transition can be missed when focus moves
        // between windows on the isolated desktop.
        var windowsKeyPressed = _windowsKeyDown ||
            (NativeMethods.GetAsyncKeyState((int)VkLeftWindows) & 0x8000) != 0 ||
            (NativeMethods.GetAsyncKeyState((int)VkRightWindows) & 0x8000) != 0;
        if (windowsKeyPressed && data.VirtualKey == VkSpace && (isDown || isUp))
            return (IntPtr)1;

        if (IsImeModeKey(data.VirtualKey) && (isDown || isUp))
            return (IntPtr)1;

        if (data.VirtualKey == VkTab && (data.Flags & AltDownFlag) != 0)
        {
            if (isDown) _altTabPressed();
            if (isDown || isUp) return (IntPtr)1;
        }

        if (_shouldCaptureAdminKeys())
        {
            var letter = data.VirtualKey switch
            {
                0x43 => 'C',
                0x54 => 'T',
                0x52 => 'R',
                0x4C => 'L',
                _ => (char?)null
            };
            if (isDown) _adminKeyPressed(letter);
            if (letter is not null && (isDown || isUp)) return (IntPtr)1;
        }

        return NativeMethods.CallNextHookEx(_hook, code, messagePointer, dataPointer);
    }

    private static bool IsImeModeKey(uint virtualKey) => virtualKey is
        0x15 or // VK_KANA / VK_HANGUL
        0x19 or // VK_KANJI / VK_HANJA
        0x1C or // VK_CONVERT
        0x1D or // VK_NONCONVERT
        0xF0 or 0xF1 or 0xF2 or 0xF3 or 0xF4 or 0xF5;

    private delegate IntPtr HookProcedure(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardData
    {
        public uint VirtualKey;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
        internal static partial IntPtr SetWindowsHookEx(int hookType, HookProcedure callback, IntPtr module, uint threadId);

        [LibraryImport("user32.dll")]
        internal static partial IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

        [LibraryImport("user32.dll")]
        internal static partial short GetAsyncKeyState(int virtualKey);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool UnhookWindowsHookEx(IntPtr hook);

        [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr GetModuleHandle(string? moduleName);
    }
}
