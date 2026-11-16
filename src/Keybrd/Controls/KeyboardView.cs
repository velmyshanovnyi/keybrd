using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Keybrd.Core;

namespace Keybrd.Controls
{
    /// <summary>
    /// Віртуальна клавіатура: стандартна або ноутбучна, з рядом Fn, кольорами пальців,
    /// літерами другого алфавіту, підсвіткою наступної клавіші та натискань.
    /// </summary>
    public sealed class KeyboardView : UserControl
    {
        // ---------- кольори пальців (мізинці, безіменні, середні, лівий вказівний, правий вказівний, великі) ----------

        private static readonly Color[] LightFill = { C("#F4C0D1"), C("#FAC775"), C("#C0DD97"), C("#B5D4F4"), C("#9FE1CB"), C("#D3D1C7") };
        private static readonly Color[] LightText = { C("#72243E"), C("#633806"), C("#27500A"), C("#0C447C"), C("#085041"), C("#444441") };
        private static readonly Color[] DarkFill = { C("#72243E"), C("#633806"), C("#27500A"), C("#0C447C"), C("#085041"), C("#444441") };
        private static readonly Color[] DarkText = { C("#F4C0D1"), C("#FAC775"), C("#C0DD97"), C("#B5D4F4"), C("#9FE1CB"), C("#D3D1C7") };

        private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

        public static Brush FingerFill(int i) => Frozen((ThemeManager.IsDark ? DarkFill : LightFill)[i]);
        public static Brush FingerText(int i) => Frozen((ThemeManager.IsDark ? DarkText : LightText)[i]);

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // ---------- стан ----------

        private readonly Canvas canvas = new Canvas();
        private readonly Border deck = new Border();
        private readonly Border touchpad = new Border();
        private readonly List<KeyCap> caps = new List<KeyCap>();
        private readonly Dictionary<int, KeyCap> byId = new Dictionary<int, KeyCap>();

        private bool laptop, fnRow, fnLayer;
        private double contentH;
        private char? target;

        public int TargetId { get; private set; }
        public int TargetShiftId { get; private set; }

        /// <summary>Змінилася ціль або її палець (для підказки під клавіатурою).</summary>
        public event Action TargetChanged;

        public KeyboardView()
        {
            Content = canvas;
            Focusable = false;
            SizeChanged += (s, e) => Arrange();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            Build();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            AppConfig.Changed += OnConfigChanged;
            ThemeManager.Changed += Refresh;
            KeyInput.ReleaseAll += ReleaseAll;
            OnConfigChanged();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            AppConfig.Changed -= OnConfigChanged;
            ThemeManager.Changed -= Refresh;
            KeyInput.ReleaseAll -= ReleaseAll;
        }

        private void OnConfigChanged()
        {
            var c = AppConfig.Current;
            if ((c.KeyboardStyle == "laptop") != laptop || c.ShowFnRow != fnRow) Build();
            else Refresh();
        }

        // ---------- публічне API ----------

        /// <summary>Символ, який треба набрати наступним (null — нічого не підсвічувати).</summary>
        public void SetTarget(char? ch)
        {
            target = ch;
            forcedId = 0;
            Refresh();
        }

        /// <summary>Підсвітити службову клавішу (наприклад, Backspace, коли треба виправити помилку).</summary>
        public void SetTargetKey(int id)
        {
            target = null;
            forcedId = id;
            Refresh();
        }

        private int forcedId;

        public void Press(int id, bool down)
        {
            if (!byId.TryGetValue(id, out var cap)) return;
            cap.Pressed = down;
            cap.UpdateDrop();
            Paint(cap);
        }

        /// <summary>Коротко блимнути клавішею зеленим (правильно) або червоним (помилка).</summary>
        public void Flash(int id, bool ok)
        {
            if (!byId.TryGetValue(id, out var cap)) return;
            cap.Flash = ok ? 1 : 2;
            Paint(cap);
            if (cap.FlashTimer == null)
            {
                cap.FlashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                cap.FlashTimer.Tick += (s, e) =>
                {
                    cap.FlashTimer.Stop();
                    cap.Flash = 0;
                    Paint(cap);
                };
            }
            cap.FlashTimer.Stop();
            cap.FlashTimer.Start();
        }

        public void ReleaseAll()
        {
            foreach (var cap in caps)
            {
                if (!cap.Pressed) continue;
                cap.Pressed = false;
                cap.UpdateDrop();
                Paint(cap);
            }
        }

        public bool HasKey(int id) => byId.ContainsKey(id);

        // ---------- побудова ----------

        private void Build()
        {
            var c = AppConfig.Current;
            laptop = c.KeyboardStyle == "laptop";
            fnRow = c.ShowFnRow;

            canvas.Children.Clear();
            caps.Clear();
            byId.Clear();

            if (laptop)
            {
                deck.SetResourceReference(Border.BackgroundProperty, "DeckBrush");
                touchpad.SetResourceReference(Border.BackgroundProperty, "TouchpadBrush");
                canvas.Children.Add(deck);
                canvas.Children.Add(touchpad);
            }

            double y = 0;
            if (fnRow)
            {
                double x = 0;
                foreach (var k in KeyboardLayout.FnRow())
                {
                    k.X = x; k.Y = 0;
                    x += k.W;
                    AddCap(k);
                }
                y = 0.6 + 0.15;
            }

            foreach (var row in KeyboardLayout.MainRows(laptop))
            {
                double x = 0;
                foreach (var k in row)
                {
                    k.X = x;
                    k.Y = y + k.YOff;
                    if (!k.NoAdvance) x += k.W;
                    AddCap(k);
                }
                y += 1;
            }
            contentH = y;

            Refresh();
            Arrange();
        }

        private void AddCap(KeyDef k)
        {
            var cap = new KeyCap(k, laptop);
            if (k.Id == KeyboardLayout.FnKeyId)
            {
                cap.Root.Cursor = Cursors.Hand;
                cap.Root.ToolTip = "Натисніть, щоб показати функції Fn на F1–F12.\nСаму клавішу Fn ноутбук зазвичай не передає у Windows.";
                cap.Root.MouseLeftButtonDown += (s, e) => { fnLayer = !fnLayer; Refresh(); };
            }
            caps.Add(cap);
            byId[k.Id] = cap;
            canvas.Children.Add(cap.Root);
        }

        // ---------- розміщення ----------

        private void Arrange()
        {
            double pad = laptop ? 0.4 : 0;
            double tpGap = laptop ? 0.3 : 0, tpH = laptop ? 1.5 : 0;
            double wU = 15 + 2 * pad;
            double hU = contentH + tpGap + tpH + 2 * pad;

            double u = Math.Min(ActualWidth / wU, ActualHeight / hU);
            u = Math.Min(u, 66);
            if (!(u > 4)) return;

            double ox = Math.Round((ActualWidth - wU * u) / 2);
            double oy = Math.Round((ActualHeight - hU * u) / 2);
            double gap = (laptop ? 0.14 : 0.1) * u;

            if (laptop)
            {
                deck.Width = wU * u; deck.Height = hU * u;
                deck.CornerRadius = new CornerRadius(u * 0.3);
                Canvas.SetLeft(deck, ox); Canvas.SetTop(deck, oy);

                touchpad.Width = 5.5 * u; touchpad.Height = tpH * u;
                touchpad.CornerRadius = new CornerRadius(u * 0.15);
                Canvas.SetLeft(touchpad, ox + (wU - 5.5) / 2 * u);
                Canvas.SetTop(touchpad, oy + (pad + contentH + tpGap) * u);
            }

            foreach (var cap in caps)
            {
                var k = cap.Def;
                Canvas.SetLeft(cap.Root, Math.Round(ox + (pad + k.X) * u + gap / 2));
                Canvas.SetTop(cap.Root, Math.Round(oy + (pad + k.Y) * u + gap / 2));
                cap.Layout(Math.Round(k.W * u - gap), Math.Round(k.H * u - gap), u);
            }
        }

        // ---------- вміст і кольори ----------

        private void Refresh()
        {
            var c = AppConfig.Current;
            string lay = c.Layout, alt = KeyboardLayout.Other(lay);

            TargetId = 0;
            TargetShiftId = 0;
            if (target.HasValue && KeyboardLayout.Find(lay, target.Value, out var id, out var shift))
            {
                TargetId = id;
                TargetShiftId = shift ? KeyboardLayout.ShiftFor(id) : 0;
            }
            if (forcedId != 0) TargetId = forcedId;

            foreach (var cap in caps)
            {
                cap.Target = cap.Def.Id == TargetId || cap.Def.Id == TargetShiftId;
                SetLabels(cap, lay, alt, c.ShowAltLetters);
                Paint(cap);
            }
            TargetChanged?.Invoke();
        }

        private void SetLabels(KeyCap cap, string lay, string alt, bool showAlt)
        {
            var d = cap.Def;
            cap.Shift.Text = "";
            cap.Alt.Text = "";
            cap.Main.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
            cap.IsIcon = false;

            if (fnLayer && d.FnGlyph != null) { cap.Main.Text = d.FnGlyph; cap.IsIcon = true; }
            else if (d.Glyph != null) { cap.Main.Text = d.Glyph; cap.IsIcon = true; }
            else if (d.Label != null) cap.Main.Text = d.Label;
            else
            {
                var ch = KeyboardLayout.Chars(lay, d.Id);
                if (ch == null || d.Id == KeyboardLayout.Space) cap.Main.Text = "";
                else if (char.IsLetter(ch[0][0])) cap.Main.Text = ch[1];
                else
                {
                    cap.Main.Text = ch[0];
                    if (ch[1] != ch[0]) cap.Shift.Text = ch[1];
                }

                if (showAlt && d.Id != KeyboardLayout.Space)
                {
                    var a = KeyboardLayout.Chars(alt, d.Id);
                    if (a != null)
                    {
                        var at = char.IsLetter(a[0][0]) ? a[1] : a[0];
                        if (at != cap.Main.Text) cap.Alt.Text = at;
                    }
                }
            }

            if (cap.IsIcon) cap.Main.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            cap.UpdateFonts();
        }

        private void Paint(KeyCap cap)
        {
            var c = AppConfig.Current;
            string bgKey = null, fgKey = null;
            Brush bg = null, fg = null;
            int color = KeyboardLayout.ColorIndex(cap.Def.Finger);

            if (cap.Flash == 1) { bgKey = "OkBrush"; fgKey = "AccentFgBrush"; }
            else if (cap.Flash == 2) { bgKey = "ErrorBrush"; fgKey = "AccentFgBrush"; }
            else if (cap.Target) { bgKey = "AccentBrush"; fgKey = "AccentFgBrush"; }
            else if (cap.Pressed || (cap.Def.Id == KeyboardLayout.FnKeyId && fnLayer)) { bgKey = "KeyPressedBrush"; fgKey = "FgBrush"; }
            else if (heat != null)
            {
                if (heat.TryGetValue(cap.Def.Id, out var cell))
                {
                    var baseColor = (TryFindResource(laptop ? "LaptopKeyBrush" : "KeyBgBrush") as SolidColorBrush)?.Color ?? Colors.White;
                    var hot = heatSlow ? (ThemeManager.IsDark ? C("#B9770E") : C("#EF9F27"))
                                       : (ThemeManager.IsDark ? C("#B83A3A") : C("#E24B4A"));
                    double t = Math.Pow(Math.Max(0, Math.Min(1, cell.Level)), 0.8);
                    bg = Frozen(Lerp(baseColor, hot, t));
                    if (t > 0.55) fg = Brushes.White; else fgKey = "FgBrush";
                    cap.Root.ToolTip = cell.Tip;
                }
                else
                {
                    bgKey = laptop ? "LaptopKeyBrush" : "KeyBgBrush";
                    fgKey = "FgMutedBrush";
                    cap.Root.ToolTip = null;
                }
            }
            else if (c.ShowFingerColors && color >= 0) { bg = FingerFill(color); fg = FingerText(color); }
            else { bgKey = laptop ? "LaptopKeyBrush" : "KeyBgBrush"; fgKey = "FgBrush"; }

            Set(cap.Face, Border.BackgroundProperty, bgKey, bg);
            Set(cap.Bump, Border.BackgroundProperty, fgKey, fg);
            Set(cap.Main, TextBlock.ForegroundProperty, fgKey, fg);
            Set(cap.Shift, TextBlock.ForegroundProperty, fgKey, fg);
            Set(cap.Alt, TextBlock.ForegroundProperty, fgKey, fg);
            cap.Face.SetResourceReference(Border.BorderBrushProperty, laptop ? "LaptopKeyBorderBrush" : "KeyBorderBrush");
            cap.ShadowBorder.SetResourceReference(Border.BackgroundProperty, "KeyShadowBrush");
        }

        private static Color Lerp(Color a, Color b, double t)
            => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        // ---------- теплова карта ----------

        public struct HeatCell
        {
            public double Level;   // 0 — добре, 1 — найгірше
            public string Tip;
        }

        private Dictionary<int, HeatCell> heat;
        private bool heatSlow;

        /// <summary>Режим теплової карти (вкладка «Статистика»): клавіші фарбуються за помилками або повільністю.</summary>
        public void SetHeat(Dictionary<int, HeatCell> cells, bool slow)
        {
            heat = cells;
            heatSlow = slow;
            target = null;
            Refresh();
        }

        private static void Set(FrameworkElement el, DependencyProperty p, string key, Brush direct)
        {
            if (key != null) el.SetResourceReference(p, key);
            else el.SetValue(p, direct);
        }

        // ---------- одна клавіша ----------

        private sealed class KeyCap
        {
            public readonly KeyDef Def;
            public readonly Grid Root = new Grid();
            public readonly Border ShadowBorder = new Border();
            public readonly Border Face = new Border { BorderThickness = new Thickness(1) };
            public readonly TextBlock Main = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            public readonly TextBlock Shift = new TextBlock { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Opacity = 0.7 };
            public readonly TextBlock Alt = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Opacity = 0.6 };
            public readonly Border Bump = new Border { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };

            public bool Pressed, Target, IsIcon;
            public int Flash;
            public DispatcherTimer FlashTimer;

            private readonly bool laptop;
            private double unit = 40, drop;

            public KeyCap(KeyDef def, bool laptop)
            {
                Def = def;
                this.laptop = laptop;
                var inner = new Grid();
                inner.Children.Add(Main);
                inner.Children.Add(Shift);
                inner.Children.Add(Alt);
                inner.Children.Add(Bump);
                Face.Child = inner;
                Root.Children.Add(ShadowBorder);
                Root.Children.Add(Face);
                Root.SnapsToDevicePixels = true;
                ShadowBorder.Visibility = laptop ? Visibility.Collapsed : Visibility.Visible;
                Bump.Visibility = def.HomeBump ? Visibility.Visible : Visibility.Collapsed;
            }

            public void Layout(double w, double h, double u)
            {
                unit = u;
                Root.Width = Math.Max(1, w);
                Root.Height = Math.Max(1, h);
                var r = new CornerRadius(u * (laptop ? 0.1 : 0.12));
                ShadowBorder.CornerRadius = r;
                Face.CornerRadius = r;
                drop = laptop ? 0 : Math.Max(2, Math.Round(u * 0.05));
                UpdateDrop();

                Shift.Margin = new Thickness(u * 0.1, u * 0.04, 0, 0);
                Alt.Margin = new Thickness(0, u * 0.04, u * 0.1, 0);
                Bump.Width = u * 0.28;
                Bump.Height = Math.Max(1.5, Math.Round(u * 0.04));
                Bump.CornerRadius = new CornerRadius(1);
                Bump.Margin = new Thickness(0, 0, 0, u * 0.12);
                UpdateFonts();
            }

            public void UpdateFonts()
            {
                double u = unit;
                double size;
                if (Def.H < 1) size = IsIcon ? u * 0.24 : u * 0.2;          // ряд Fn, стрілки
                else if (IsIcon) size = u * 0.3;
                else if (Def.IsChar) size = u * 0.36;
                else size = u * 0.24;
                Main.FontSize = Math.Max(9, size);
                Shift.FontSize = Alt.FontSize = Math.Max(9, u * 0.2);
                // Символ із Shift-підписом зсуваємо трохи вниз, щоб не налазили.
                Main.Margin = Shift.Text.Length > 0 ? new Thickness(0, u * 0.18, 0, 0) : new Thickness(0);
            }

            public void UpdateDrop()
            {
                Face.Margin = Pressed ? new Thickness(0, drop, 0, 0) : new Thickness(0, 0, 0, drop);
            }
        }
    }
}
