using System;
using System.Windows.Interop;

namespace Keybrd.Core
{
    /// <summary>
    /// Фізичні натискання клавіш за скан-кодом (не залежать від розкладки Windows).
    ///
    /// Слухаємо чергу повідомлень потоку (ComponentDispatcher.ThreadPreprocessMessage), а не віконну
    /// процедуру: коли вікно у фокусі, WPF сам обробляє клавіатурні повідомлення до віконної процедури,
    /// і клавіші, позначені як оброблені (пробіл, Tab, Enter), туди вже не доходять.
    /// Нічого не блокуємо — WPF отримує ті самі клавіші.
    /// </summary>
    public static class KeyInput
    {
        /// <summary>(id клавіші, натиснута?, автоповтор?)</summary>
        public static event Action<int, bool, bool> Key;

        /// <summary>Вікно втратило фокус — відпустити всі клавіші на екрані.</summary>
        public static event Action ReleaseAll;

        private const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;

        private static bool attached;

        public static void Attach()
        {
            if (attached) return;
            attached = true;
            ComponentDispatcher.ThreadPreprocessMessage += OnMessage;
        }

        public static void RaiseReleaseAll() => ReleaseAll?.Invoke();

        private static void OnMessage(ref MSG msg, ref bool handled)
        {
            int m = msg.message;
            if (m != WM_KEYDOWN && m != WM_SYSKEYDOWN && m != WM_KEYUP && m != WM_SYSKEYUP) return;

            long lp = msg.lParam.ToInt64();
            int scan = (int)((lp >> 16) & 0xFF);
            if (scan == 0) return;
            bool ext = ((lp >> 24) & 1) != 0;
            bool down = m == WM_KEYDOWN || m == WM_SYSKEYDOWN;
            bool repeat = down && ((lp >> 30) & 1) != 0;

            int id = KeyboardLayout.Normalize(scan | (ext ? KeyboardLayout.Ext : 0));
            Key?.Invoke(id, down, repeat);
        }
    }
}
