using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DesktopMediaServer.Macros
{
    public static class KeySender
    {
        private const uint INPUT_KEYBOARD = 1;

        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        // IMPORTANT: union must be big enough for MOUSEINPUT on x64
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        public static void KeyCombo(params ushort[] vks)
        {
            if (vks == null || vks.Length == 0) return;

            var inputs = new INPUT[vks.Length * 2];
            int idx = 0;

            foreach (var vk in vks) inputs[idx++] = MakeKey(vk, keyUp: false);
            for (int i = vks.Length - 1; i >= 0; i--) inputs[idx++] = MakeKey(vks[i], keyUp: true);

            int cbSize = Marshal.SizeOf(typeof(INPUT)); // should be 40 on x64, 28 on x86
            var sent = SendInput((uint)inputs.Length, inputs, cbSize);

            if (sent == 0)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"SendInput failed. Win32Error={err} ({new Win32Exception(err).Message}). cbSize={cbSize}");
            }
        }

        private static INPUT MakeKey(ushort vk, bool keyUp)
        {
            ushort scan = (ushort)MapVirtualKey(vk, 0);

            uint flags = KEYEVENTF_SCANCODE;
            if (keyUp) flags |= KEYEVENTF_KEYUP;
            if (IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;

            return new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0, // must be 0 when using scancode
                        wScan = scan,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        private static bool IsExtended(ushort vk)
        {
            return vk is VK.LEFT or VK.RIGHT or VK.UP or VK.DOWN
                or VK.INSERT or VK.DELETE or VK.HOME or VK.END or VK.PRIOR or VK.NEXT
                or VK.RCONTROL or VK.RMENU
                or VK.LWIN or VK.RWIN;
        }
    }

    public static class VK
    {
        public const ushort CONTROL = 0x11;
        public const ushort SHIFT = 0x10;
        public const ushort ALT = 0x12;

        public const ushort TAB = 0x09;
        public const ushort M = 0x4D;
        public const ushort D = 0x44;

        public const ushort LEFT = 0x25;
        public const ushort UP = 0x26;
        public const ushort RIGHT = 0x27;
        public const ushort DOWN = 0x28;

        public const ushort PRIOR = 0x21;   // Page Up
        public const ushort NEXT = 0x22;    // Page Down
        public const ushort END = 0x23;
        public const ushort HOME = 0x24;
        public const ushort INSERT = 0x2D;
        public const ushort DELETE = 0x2E;

        public const ushort LWIN = 0x5B;
        public const ushort RWIN = 0x5C;

        public const ushort RCONTROL = 0xA3;
        public const ushort RMENU = 0xA5;   // Right Alt
    }
}