using System;
using System.Windows;
using System.Windows.Media;

namespace Keybrd.Controls
{
    /// <summary>
    /// «Пасхалки»: оптичні ілюзії та фрактали, що проявляються на місці схованої клавіатури,
    /// поки людина друкує без помилок. Усе малюється кодом — жодних файлів з картинками.
    ///
    /// Ілюзії статичні (кільця, що здаються переплетеними; кола, що здаються спіраллю; стіна кафе;
    /// мерехтлива решітка). Фрактали ростуть разом із серією чистих натискань, а їхня форма
    /// залежить від швидкості й рівномірності набору.
    /// </summary>
    public sealed class EggView : FrameworkElement
    {
        public const int Count = 7;
        public static readonly string[] Names =
        {
            "переплетені кільця", "спіраль з кіл", "стіна кафе", "мерехтлива решітка",
            "дерево", "соняшник", "сніжинка",
        };

        private int kind;
        private double flow, cpm = 200, rhythm = 60;

        private static readonly Brush Light = Frozen(Color.FromRgb(0xF4, 0xF4, 0xF4));
        private static readonly Brush Dark = Frozen(Color.FromRgb(0x10, 0x10, 0x10));
        private static readonly Brush Backdrop = MakeBackdrop();

        public EggView()
        {
            IsHitTestVisible = false;
            Opacity = 0;
        }

        public int Kind
        {
            get => kind;
            set { kind = ((value % Count) + Count) % Count; InvalidateVisual(); }
        }

        /// <summary>Фрактали залежать від набору: довжини чистої серії, швидкості й рівномірності.</summary>
        public void Update(double flow, double cpm, double rhythm)
        {
            this.flow = flow;
            if (cpm > 0) this.cpm = cpm;
            if (rhythm > 0) this.rhythm = rhythm;
            if (kind >= 4 && Opacity > 0) InvalidateVisual();
        }

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // Сіре тло, що розчиняється до країв: на ньому видно і світлі, і темні елементи в обох темах.
        private static Brush MakeBackdrop()
        {
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0x80, 0x84, 0x88), 0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, 0x80, 0x84, 0x88), 0.72));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x80, 0x84, 0x88), 1));
            b.Freeze();
            return b;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 40 || h < 40) return;
            double size = Math.Min(w, h);
            var c = new Point(w / 2, h / 2);
            double r = size / 2;

            dc.DrawEllipse(Backdrop, null, c, r, r);
            double u = r * 0.82;   // робочий радіус усередині тла
            switch (kind)
            {
                case 0: Rings(dc, c, new[] { u * 0.78, u * 0.98 }, u * 0.062, 18, 4); break;
                case 1: Rings(dc, c, new[] { u * 0.30, u * 0.47, u * 0.64, u * 0.81, u * 0.98 }, u * 0.055, 24, 0); break;
                case 2: CafeWall(dc, c, u); break;
                case 3: Grid(dc, c, u); break;
                case 4: Tree(dc, c, u); break;
                case 5: Sunflower(dc, c, u); break;
                default: Snowflake(dc, c, u); break;
            }
        }

        // ---------- ілюзії ----------

        /// <summary>
        /// Кола з чорно-білих рисок, нахилених до дотичної. Якщо нахил змінює знак групами (group &gt; 0),
        /// рівні кола здаються хвилястими й переплетеними; якщо нахил сталий — концентричні кола здаються спіраллю.
        /// </summary>
        private static void Rings(DrawingContext dc, Point c, double[] radii, double thick, double tilt, int group)
        {
            for (int ri = 0; ri < radii.Length; ri++)
            {
                double rad = radii[ri];
                int n = Math.Max(16, (int)(2 * Math.PI * rad / (thick * 1.25)) / 2 * 2);
                if (group > 0) n = n / (group * 2) * (group * 2);
                double len = 2 * Math.PI * rad / n;
                for (int k = 0; k < n; k++)
                {
                    double a = 360.0 * k / n;
                    double t = group > 0 ? (((k / group) + ri) % 2 == 0 ? tilt : -tilt) : (ri % 2 == 0 ? tilt : -tilt);
                    dc.PushTransform(new RotateTransform(a, c.X, c.Y));
                    dc.PushTransform(new RotateTransform(t, c.X, c.Y - rad));
                    dc.DrawRoundedRectangle(k % 2 == 0 ? Light : Dark, null,
                        new Rect(c.X - len * 0.5, c.Y - rad - thick / 2, len, thick), thick * 0.3, thick * 0.3);
                    dc.Pop();
                    dc.Pop();
                }
            }
        }

        /// <summary>Стіна кафе: ряди чорно-білих плиток зі зсувом; рівні сірі шви здаються похилими.</summary>
        private static void CafeWall(DrawingContext dc, Point c, double u)
        {
            double side = u * 1.36, x0 = c.X - side / 2, y0 = c.Y - side / 2;
            int rows = 8;
            double th = side / rows, tw = th * 1.25;
            double[] shift = { 0, 0.25, 0.5, 0.25 };
            dc.PushClip(new RectangleGeometry(new Rect(x0, y0, side, side)));
            dc.DrawRectangle(Frozen(Color.FromRgb(0x80, 0x84, 0x88)), null, new Rect(x0, y0, side, side));
            for (int j = 0; j < rows; j++)
            {
                double off = shift[j % 4] * tw * 2;
                for (int i = -2; i * tw < side + tw * 2; i++)
                    dc.DrawRectangle(i % 2 == 0 ? Dark : Light, null,
                        new Rect(x0 + i * tw + off, y0 + j * th + 1, tw, th - 2));
            }
            dc.Pop();
        }

        /// <summary>Мерехтлива решітка: у білих кружках на перетинах боковим зором видно темні цятки.</summary>
        private static void Grid(DrawingContext dc, Point c, double u)
        {
            double side = u * 1.36, x0 = c.X - side / 2, y0 = c.Y - side / 2;
            int n = 7;
            double cell = side / n, gap = cell * 0.16;
            dc.DrawRectangle(Frozen(Color.FromRgb(0x9A, 0x9E, 0xA2)), null, new Rect(x0, y0, side, side));
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    dc.DrawRectangle(Dark, null, new Rect(x0 + i * cell + gap / 2, y0 + j * cell + gap / 2, cell - gap, cell - gap));
            for (int j = 1; j < n; j++)
                for (int i = 1; i < n; i++)
                    dc.DrawEllipse(Light, null, new Point(x0 + i * cell, y0 + j * cell), gap * 0.72, gap * 0.72);
        }

        // ---------- фрактали, що ростуть від набору ----------

        /// <summary>Наскільки фрактал виріс: 0…1 за приблизно 250 чистих натискань.</summary>
        private double Growth => Math.Max(0, Math.Min(1, flow / 250.0));

        /// <summary>Дерево: глибина росте із серією, кут гілок залежить від швидкості, асиметрія — від рівномірності.</summary>
        private void Tree(DrawingContext dc, Point c, double u)
        {
            double depth = 2 + Growth * 8;                                   // 2…10 рівнів
            double angle = 14 + Math.Max(0, Math.Min(1, cpm / 500.0)) * 26;   // швидше — ширша крона
            double skew = (100 - Math.Max(20, Math.Min(100, rhythm))) / 80.0 * 12;   // нерівний ритм хилить дерево
            Branch(dc, new Point(c.X, c.Y + u * 0.95), -90, u * 0.50, depth, angle, skew, 0);
        }

        private static void Branch(DrawingContext dc, Point from, double dir, double len, double depth, double angle, double skew, int level)
        {
            if (depth <= 0 || len < 1.5) return;
            double part = Math.Min(1, depth);   // остання гілка відростає поступово
            double rad = dir * Math.PI / 180;
            var to = new Point(from.X + Math.Cos(rad) * len * part, from.Y + Math.Sin(rad) * len * part);
            var pen = new Pen(level % 2 == 0 ? Light : Dark, Math.Max(1, len * 0.09)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawLine(pen, from, to);
            if (depth <= 1) return;
            Branch(dc, to, dir - angle + skew, len * 0.72, depth - 1, angle, skew, level + 1);
            Branch(dc, to, dir + angle + skew, len * 0.72, depth - 1, angle, skew, level + 1);
        }

        /// <summary>Соняшник (філотаксис): насінин більшає із серією, кут розходження зсувається від швидкості.</summary>
        private void Sunflower(DrawingContext dc, Point c, double u)
        {
            int n = (int)(20 + Growth * 700);
            double angle = 137.5 + (Math.Max(0, Math.Min(600, cpm)) - 300) / 300.0 * 0.6;   // біля золотого кута візерунок різко змінюється
            double k = u / Math.Sqrt(720);
            for (int i = 1; i <= n; i++)
            {
                double rr = k * Math.Sqrt(i), a = i * angle * Math.PI / 180;
                double dot = 1.2 + rr / u * (u * 0.035);
                dc.DrawEllipse(i % 2 == 0 ? Light : Dark, null, new Point(c.X + Math.Cos(a) * rr, c.Y + Math.Sin(a) * rr), dot, dot);
            }
        }

        /// <summary>Сніжинка Коха: рівень деталізації росте із серією; рівномірний ритм робить зубці гострішими.</summary>
        private void Snowflake(DrawingContext dc, Point c, double u)
        {
            int level = Math.Min(4, (int)(Growth * 5));
            double spike = 0.5 + Math.Max(20, Math.Min(100, rhythm)) / 100.0 * 0.5;   // 0.6…1.0 від висоти рівностороннього трикутника
            var g = new StreamGeometry();
            using (var s = g.Open())
            {
                Point P(double deg) => new Point(c.X + Math.Cos(deg * Math.PI / 180) * u * 0.9, c.Y + Math.Sin(deg * Math.PI / 180) * u * 0.9);
                Point a = P(-90), b = P(30), d = P(150);
                s.BeginFigure(a, false, true);
                Koch(s, a, b, level, spike);
                Koch(s, b, d, level, spike);
                Koch(s, d, a, level, spike);
            }
            g.Freeze();
            var dark = new Pen(Dark, Math.Max(2, u * 0.03)) { LineJoin = PenLineJoin.Round };
            var light = new Pen(Light, Math.Max(1, u * 0.012)) { LineJoin = PenLineJoin.Round };
            dark.Freeze(); light.Freeze();
            dc.DrawGeometry(null, dark, g);
            dc.DrawGeometry(null, light, g);
        }

        private static void Koch(StreamGeometryContext s, Point a, Point b, int level, double spike)
        {
            if (level == 0) { s.LineTo(b, true, true); return; }
            double dx = (b.X - a.X) / 3, dy = (b.Y - a.Y) / 3;
            var p1 = new Point(a.X + dx, a.Y + dy);
            var p2 = new Point(a.X + 2 * dx, a.Y + 2 * dy);
            double hgt = Math.Sqrt(3) / 2 * spike;
            // Вершина зубця: від середини відрізка назовні, перпендикулярно до нього.
            var peak = new Point((a.X + b.X) / 2 + dy * hgt, (a.Y + b.Y) / 2 - dx * hgt);
            Koch(s, a, p1, level - 1, spike);
            Koch(s, p1, peak, level - 1, spike);
            Koch(s, peak, p2, level - 1, spike);
            Koch(s, p2, b, level - 1, spike);
        }
    }
}
