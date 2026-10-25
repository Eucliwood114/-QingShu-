using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace QingShu
{
    internal static class Native
    {
        internal const int WM_HOTKEY = 0x0312;
        internal const uint MOD_NOREPEAT = 0x4000;
        internal const uint MOD_CONTROL = 0x0002;
        internal const uint MOD_ALT = 0x0001;
        internal const ushort VK_ESCAPE = 0x1B;
        internal const ushort VK_F8 = 0x77;
        internal const ushort VK_CONTROL = 0x11;
        internal const ushort VK_MENU = 0x12;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_UNICODE = 0x0004;

        [StructLayout(LayoutKind.Sequential)]
        internal struct INPUT
        {
            internal uint type;
            internal InputUnion data;
        }

        // MOUSEINPUT makes this union the correct size on both 32-bit and 64-bit Windows.
        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion
        {
            [FieldOffset(0)] internal MOUSEINPUT mouse;
            [FieldOffset(0)] internal KEYBDINPUT keyboard;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MOUSEINPUT
        {
            internal int dx;
            internal int dy;
            internal uint mouseData;
            internal uint dwFlags;
            internal uint time;
            internal UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct KEYBDINPUT
        {
            internal ushort wVk;
            internal ushort wScan;
            internal uint dwFlags;
            internal uint time;
            internal UIntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            internal int left, top, right, bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct GUITHREADINFO
        {
            internal uint cbSize;
            internal uint flags;
            internal IntPtr hwndActive;
            internal IntPtr hwndFocus;
            internal IntPtr hwndCapture;
            internal IntPtr hwndMenuOwner;
            internal IntPtr hwndMoveSize;
            internal IntPtr hwndCaret;
            internal RECT rcCaret;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, INPUT[] inputs, int size);

        [DllImport("user32.dll")]
        internal static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint RegisterWindowMessage(string message);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyIcon(IntPtr icon);

        internal static IntPtr FocusWindow(IntPtr foreground)
        {
            uint processId;
            uint threadId = GetWindowThreadProcessId(foreground, out processId);
            GUITHREADINFO info = new GUITHREADINFO();
            info.cbSize = (uint)Marshal.SizeOf(typeof(GUITHREADINFO));
            return threadId != 0 && GetGUIThreadInfo(threadId, ref info) ? info.hwndFocus : IntPtr.Zero;
        }

        internal static bool IsDown(int key)
        {
            return (GetAsyncKeyState(key) & 0x8000) != 0;
        }

        internal static bool TriggerKeysDown()
        {
            int[] keys = { 0x10, 0x11, 0x12, 0x5B, 0x5C, VK_F8, 0x56 };
            foreach (int key in keys)
                if (IsDown(key)) return true;
            return false;
        }

        internal static INPUT Key(ushort key, bool up)
        {
            INPUT input = new INPUT();
            input.type = 1;
            input.data.keyboard.wVk = key;
            input.data.keyboard.dwFlags = up ? KEYEVENTF_KEYUP : 0;
            return input;
        }

        internal static void Send(INPUT[] inputs)
        {
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (sent != inputs.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "系统未能发送按键。请确认目标窗口可以正常手动输入，且没有以管理员身份运行。");
        }

        internal static void SendTextElement(string element)
        {
            INPUT[] inputs = new INPUT[element.Length * 2];
            for (int i = 0; i < element.Length; i++)
            {
                INPUT down = new INPUT();
                down.type = 1;
                down.data.keyboard.wScan = element[i];
                down.data.keyboard.dwFlags = KEYEVENTF_UNICODE;
                inputs[i * 2] = down;
                down.data.keyboard.dwFlags |= KEYEVENTF_KEYUP;
                inputs[i * 2 + 1] = down;
            }
            Send(inputs);
        }
    }

    internal sealed class InputTarget
    {
        internal readonly IntPtr Window;
        internal readonly IntPtr Focus;

        internal InputTarget(IntPtr window)
        {
            Window = window;
            Focus = Native.FocusWindow(window);
        }

        internal bool IsCurrent()
        {
            return Window != IntPtr.Zero && Native.GetForegroundWindow() == Window
                && (Focus == IntPtr.Zero || Native.FocusWindow(Window) == Focus);
        }
    }

    internal sealed class TargetChangedException : Exception
    {
        internal TargetChangedException() : base("输入位置已改变，已停止。已输入的部分会保留。") { }
    }

    internal static class Typer
    {
        internal static async Task DelayChecked(int milliseconds, CancellationToken token)
        {
            int remaining = milliseconds;
            do
            {
                token.ThrowIfCancellationRequested();
                if (Native.IsDown(Native.VK_ESCAPE)) throw new OperationCanceledException();
                if (remaining <= 0) break;
                int step = Math.Min(15, remaining);
                await Task.Delay(step, token).ConfigureAwait(false);
                remaining -= step;
            } while (true);
        }

        internal static async Task WaitForRelease(CancellationToken token)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(4);
            while (Native.TriggerKeysDown())
            {
                if (DateTime.UtcNow > deadline)
                    throw new InvalidOperationException("请松开快捷键后再试。");
                await DelayChecked(15, token).ConfigureAwait(false);
            }
            await DelayChecked(35, token).ConfigureAwait(false);
        }

        internal static async Task TypeAsync(string text, int interval, InputTarget target,
            CancellationToken token, Action<int, int> progress)
        {
            await WaitForRelease(token).ConfigureAwait(false);
            List<string> elements = TextTools.Elements(text);
            for (int i = 0; i < elements.Count; i++)
            {
                await DelayChecked(0, token).ConfigureAwait(false);
                if (!target.IsCurrent()) throw new TargetChangedException();
                if (Native.TriggerKeysDown())
                    throw new InvalidOperationException("检测到其他按键，已停止。请松开按键后重试。");
                Native.SendTextElement(elements[i]);
                if (progress != null && (i % 8 == 0 || i == elements.Count - 1))
                    progress(i + 1, elements.Count);
                if (i + 1 < elements.Count)
                    await DelayChecked(interval, token).ConfigureAwait(false);
            }
        }
    }
}
