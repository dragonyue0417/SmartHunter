using System;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace SmartHunter.Core
{
    public class KeyboardInput : IDisposable
    {
        readonly System.Collections.Generic.HashSet<Key> pressed = new System.Collections.Generic.HashSet<Key>();
        readonly System.Collections.Generic.HashSet<Key> consumed = new System.Collections.Generic.HashSet<Key>();
        public void Dispose() { Unhook(); GC.SuppressFinalize(this); }
        IntPtr m_User32Handle;
        IntPtr m_KeyboardHookHandle;

        WindowsApi.HookProc m_KeyboardHook;

        public event EventHandler<KeyboardInputEventArgs> InputReceived;

        public KeyboardInput()
        {
            Hook();
        }

        ~KeyboardInput()
        {
            Unhook();
        }

        void Hook()
        {
            m_KeyboardHookHandle = IntPtr.Zero;
            m_User32Handle = IntPtr.Zero;
            m_KeyboardHook = KeyboardHook;

            m_User32Handle = WindowsApi.LoadLibrary("User32");
            if (m_User32Handle != IntPtr.Zero)
            {
                m_KeyboardHookHandle = WindowsApi.SetWindowsHookEx((int)WindowsApi.WindowsHook.WH_KEYBOARD_LL, m_KeyboardHook, m_User32Handle, 0);
            }
        }

        void Unhook()
        {
            if (m_KeyboardHookHandle != IntPtr.Zero)
            {
                WindowsApi.UnhookWindowsHookEx(m_KeyboardHookHandle);
                m_KeyboardHookHandle = IntPtr.Zero;
                m_KeyboardHook -= KeyboardHook;
            }

            if (m_User32Handle != IntPtr.Zero)
            {
                WindowsApi.FreeLibrary(m_User32Handle);
                m_User32Handle = IntPtr.Zero;
            }
        }

        static Key KeyFromVirtualCode(uint virtualCode)
        {
            return KeyInterop.KeyFromVirtualKey((int)virtualCode);
        }

        IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var keyboardMessage = (WindowsApi.KeyboardMessage)wParam.ToInt32();
                var keyboardData = (WindowsApi.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(WindowsApi.KBDLLHOOKSTRUCT));

                if ((keyboardData.Flags & 0x10) != 0) return WindowsApi.CallNextHookEx(IntPtr.Zero,nCode,wParam,lParam);
                var key = KeyFromVirtualCode(keyboardData.VkCode);
                bool isDown = keyboardMessage == WindowsApi.KeyboardMessage.WM_KEYDOWN || keyboardMessage == WindowsApi.KeyboardMessage.WM_SYSKEYDOWN;

                bool repeat = isDown && !pressed.Add(key);
                if(!isDown) pressed.Remove(key);
                var args = new KeyboardInputEventArgs(key,isDown) { IsRepeat=repeat, Handled=consumed.Contains(key) };
                Dispatch(args);
                if(args.Handled && isDown) consumed.Add(key);
                if(!isDown) consumed.Remove(key);
                if(args.Handled) return new IntPtr(1);
            }

            return WindowsApi.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        // Managed exceptions must never cross a native hook callback. In
        // particular, clipboard and WPF errors otherwise surface as c000041d.
        void Dispatch(KeyboardInputEventArgs args)
        {
            try { InputReceived?.Invoke(this, args); }
            catch (Exception error)
            {
                args.Handled = false;
                pressed.Clear(); consumed.Clear();
                Log.WriteException(error);
            }
        }
    }
}
