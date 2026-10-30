using System;
using System.Windows.Input;

namespace SmartHunter.Core
{
    public class KeyboardInputEventArgs : EventArgs
    {
        public bool Handled { get; set; }
        public bool IsRepeat { get; set; }
        public Key Key { get; private set; }
        public bool IsDown { get; private set; }

        public KeyboardInputEventArgs(Key key, bool isDown)
        {
            Key = key;
            IsDown = isDown;
        }
    }
}
