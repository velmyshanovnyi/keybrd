using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Keybrd.Controls
{
    /// <summary>
    /// «Пасхалки»: оптичні ілюзії та фрактали, що проявляються на місці схованої клавіатури,
    /// поки людина друкує без помилок. Усе малюється кодом — жодних файлів з картинками.
    ///
    /// Картинка пам'ятає кожне натискання вправи. Фрактал росте по одному елементу на натискання:
    /// гілка дерева, насінина соняшника, зубець сніжинки, ланка дракона. Помилкове натискання
    /// «заражає» елемент, який мав з'явитися саме тоді: він червоний і зламаний, а все, що росте
    /// з нього далі, росте хворобливо. В ілюзіях помилка лишає червоний збитий елемент.
    /// Чистий набір дає правильну фігуру; помилки видно на ній до кінця вправи.
    /// </summary>
    public sealed class EggView : FrameworkElement
    {
        public const int Count = 11;
        public const int FirstFractal = 7;
        public static readonly string[] Names =
        {
            "переплетені кільця", "спіраль з кіл", "стіна кафе", "мерехтлива решітка",
            "лінії Цьолльнера", "сітка Герінга", "кільця, що обертаються",
            "дерево", "соняшник", "сніжинка", "дракон",
        };

        private int kind;
        private double cpm = 200, rhythm = 60;
        private int count;                                        // скільки натискань у вправі
        private readonly HashSet<int> errors = new HashSet<int>(); // номери помилкових натискань
        private readonly List<int> errorList = new List<int>();

        private static readonly Brush Light = Frozen(Color.FromRgb(0xF4, 0xF4, 0xF4));
        private static readonly Brush Dark = Frozen(Color.FromRgb(0x10, 0x10, 0x10));
        private static readonly Brush Mid = Frozen(Color.FromRgb(0x80, 0x84, 0x88));
        private static readonly Brush Sick = Frozen(Color.FromRgb(0xE5, 0x3B, 0x2F));      // хворий елемент
        private static readonly Brush SickDim = Frozen(Color.FromRgb(0x8E, 0x3A, 0x30));   // те, що виросло з хворого
        private static readonly Brush Blue = Frozen(Color.FromRgb(0x2A, 0x4F, 0xC8));
        private static readonly Brush Yellow = Frozen(Color.FromRgb(0xE6, 0xD2, 0x2A));
        private static readonly Brush Backdrop = MakeBackdrop();

        public EggView()
        {
            IsHitTestVisible = false;
            Opacity = 0;
            Visibility = Visibility.Collapsed;
        }

        public int Kind => kind;

        /// <summary>Нова вправа: інша картинка, чиста історія.</summary>
        public void Reset(int newKind)
        {
            kind = ((newKind % Count) + Count) % Count;
            count = 0;
            errors.Clear();
            errorList.Clear();
            InvalidateVisual();
        }

        /// <summary>Одне натискання: фрактал отримує новий елемент — здоровий або хворий.</summary>
        public void Add(bool error)
        {
            if (error) { errors.Add(count); errorList.Add(count); }
            count++;
            if (Visibility == Visibility.Visible) InvalidateVisual();
        }

        /// <summary>Форма фракталів залежить від швидкості й рівномірності набору.</summary>
        public void SetDynamics(double cpm, double rhythm)
        {
            if (cpm > 0) this.cpm = cpm;
            if (rhythm > 0) this.rhythm = rhythm;
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

        /// <summary>Стале «випадкове» число 0…1 для елемента: хвороба щоразу виглядає однаково.</summary>
        private static double Hash(int i)
        {
            double x = Math.Sin(i * 12.9898 + 4.1414) * 43758.5453;
            return x - Math.Floor(x);
        }

        private readonly Dictionary<long, Pen> pens = new Dictionary<long, Pen>();

        private Pen PenOf(Brush brush, double thickness)
        {
            long key = ((long)brush.GetHashCode() << 20) ^ (long)Math.Round(thickness * 8);
            if (!pens.TryGetValue(key, out var p))
            {
                p = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                p.Freeze();
                if (pens.Count > 400) pens.Clear();
                pens[key] = p;
            }
            return p;
        }

        /// <summary>В ілюзіях елементів скінченна кількість: помилка «b» псує елемент b mod total.</summary>
        private HashSet<int> Defects(int total)
        {
            var set = new HashSet<int>();
            if (total > 0) foreach (var e in errorList) set.Add((int)(Hash(e) * total) % total);
            return set;
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
                case 4: Zollner(dc, c, u); break;
                case 5: Hering(dc, c, u); break;
                case 6: Snakes(dc, c, u); break;
                case 7: Tree(dc, c, u); break;
                case 8: Sunflower(dc, c, u); break;
                case 9: Snowflake(dc, c, u); break;
                default: Dragon(dc, c, u); break;
            }
        }

        // ---------- ілюзії ----------

        /// <summary>
        /// Кола з чорно-білих рисок, нахилених до дотичної. Якщо нахил змінює знак групами (group &gt; 0),
        /// рівні кола здаються хвилястими й переплетеними; якщо нахил сталий — концентричні кола здаються спіраллю.
        /// </summary>
        private void Rings(DrawingContext dc, Point c, double[] radii, double thick, double tilt, int group)
        {
            var counts = new int[radii.Length];
            int total = 0;
            for (int ri = 0; ri < radii.Length; ri++)
            {
                int n = Math.Max(16, (int)(2 * Math.PI * radii[ri] / (thick * 1.25)) / 2 * 2);
                if (group > 0) n = n / (group * 2) * (group * 2);
                counts[ri] = n;
                total += n;
            }
            var bad = Defects(total);
            int index = 0;
            for (int ri = 0; ri < radii.Length; ri++)
            {
                double rad = radii[ri];
                int n = counts[ri];
                double len = 2 * Math.PI * rad / n;
                for (int k = 0; k < n; k++, index++)
                {
                    double a = 360.0 * k / n;
                    double t = group > 0 ? (((k / group) + ri) % 2 == 0 ? tilt : -tilt) : (ri % 2 == 0 ? tilt : -tilt);
                    bool sick = bad.Contains(index);
                    if (sick) t += 70;   // збита риска стоїть майже поперек кола
                    dc.PushTransform(new RotateTransform(a, c.X, c.Y));
                    dc.PushTransform(new RotateTransform(t, c.X, c.Y - rad));
                    dc.DrawRoundedRectangle(sick ? Sick : k % 2 == 0 ? Light : Dark, null,
                        new Rect(c.X - len * 0.5, c.Y - rad - thick / 2, len, thick), thick * 0.3, thick * 0.3);
                    dc.Pop();
                    dc.Pop();
                }
            }
        }

        /// <summary>Стіна кафе: ряди чорно-білих плиток зі зсувом; рівні сірі шви здаються похилими.</summary>
        private void CafeWall(DrawingContext dc, Point c, double u)
        {
            double side = u * 1.36, x0 = c.X - side / 2, y0 = c.Y - side / 2;
            int rows = 8;
            double th = side / rows, tw = th * 1.25;
            double[] shift = { 0, 0.25, 0.5, 0.25 };
            int perRow = (int)(side / tw) + 4;
            var bad = Defects(rows * perRow);
            dc.PushClip(new RectangleGeometry(new Rect(x0, y0, side, side)));
            dc.DrawRectangle(Mid, null, new Rect(x0, y0, side, side));
            for (int j = 0; j < rows; j++)
            {
                double off = shift[j % 4] * tw * 2;
                for (int i = -2; i < perRow - 2; i++)
                {
                    bool sick = bad.Contains(j * perRow + i + 2);
                    // Хвора плитка червона і з'їхала з ряду.
                    dc.DrawRectangle(sick ? Sick : i % 2 == 0 ? Dark : Light, null,
                        new Rect(x0 + i * tw + off, y0 + j * th + 1 + (sick ? th * 0.28 : 0), tw, th - 2));
                }
            }
            dc.Pop();
        }

        /// <summary>Мерехтлива решітка: у білих кружках на перетинах боковим зором видно темні цятки.</summary>
        private void Grid(DrawingContext dc, Point c, double u)
        {
            double side = u * 1.36, x0 = c.X - side / 2, y0 = c.Y - side / 2;
            int n = 7;
            double cell = side / n, gap = cell * 0.16;
            var bad = Defects((n - 1) * (n - 1));
            dc.DrawRectangle(Frozen(Color.FromRgb(0x9A, 0x9E, 0xA2)), null, new Rect(x0, y0, side, side));
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    dc.DrawRectangle(Dark, null, new Rect(x0 + i * cell + gap / 2, y0 + j * cell + gap / 2, cell - gap, cell - gap));
            for (int j = 1; j < n; j++)
                for (int i = 1; i < n; i++)
                {
                    bool sick = bad.Contains((j - 1) * (n - 1) + i - 1);
                    double rr = gap * (sick ? 1.25 : 0.72);
                    dc.DrawEllipse(sick ? Sick : Light, null, new Point(x0 + i * cell, y0 + j * cell), rr, rr);
                }
        }

        /// <summary>Лінії Цьолльнера: паралельні діагоналі з короткими штрихами здаються непаралельними.</summary>
        private void Zollner(DrawingContext dc, Point c, double u)
        {
            int lines = 7, hatches = 11;
            double side = u * 1.3, step = side / (lines - 1), hl = step * 0.34;
            var bad = Defects(lines * hatches);
            dc.PushTransform(new RotateTransform(45, c.X, c.Y));
            var main = PenOf(Dark, Math.Max(2, u * 0.022));
            for (int j = 0; j < lines; j++)
            {
                double y = c.Y - side / 2 + j * step;
                // довжина лінії в межах кола
                double half = Math.Sqrt(Math.Max(0, u * u - (y - c.Y) * (y - c.Y)));
                if (half < step) continue;
                dc.DrawLine(main, new Point(c.X - half, y), new Point(c.X + half, y));
                for (int i = 0; i < hatches; i++)
                {
                    double x = c.X - half + (i + 0.5) * (2 * half / hatches);
                    bool sick = bad.Contains(j * hatches + i);
                    // Штрихи через одну лінію нахилені в різні боки; хворий штрих перевернуто.
                    double dir = (j % 2 == 0 ? 1 : -1) * (sick ? -1 : 1);
                    dc.DrawLine(PenOf(sick ? Sick : j % 2 == 0 ? Light : Dark, Math.Max(1.5, u * (sick ? 0.03 : 0.016))),
                        new Point(x - hl * 0.7, y - hl * dir), new Point(x + hl * 0.7, y + hl * dir));
                }
            }
            dc.Pop();
        }

        /// <summary>Сітка Герінга: дві прямі на тлі променів з центру здаються вигнутими назовні.</summary>
        private void Hering(DrawingContext dc, Point c, double u)
        {
            int rays = 30;
            var bad = Defects(rays);
            for (int i = 0; i < rays; i++)
            {
                double a = Math.PI * i / rays;
                bool sick = bad.Contains(i);
                // Хворий промінь зламано посередині.
                double bend = sick ? 0.22 : 0;
                var p1 = new Point(c.X + Math.Cos(a) * u, c.Y + Math.Sin(a) * u);
                var p2 = new Point(c.X - Math.Cos(a + bend) * u, c.Y - Math.Sin(a + bend) * u);
                var pen = PenOf(sick ? Sick : i % 2 == 0 ? Dark : Light, Math.Max(1, u * (sick ? 0.02 : 0.009)));
                dc.DrawLine(pen, p1, c);
                dc.DrawLine(pen, c, p2);
            }
            var bar = PenOf(Blue, Math.Max(2.5, u * 0.03));
            double off = u * 0.28, hy = Math.Sqrt(u * u - off * off);
            dc.DrawLine(bar, new Point(c.X - off, c.Y - hy), new Point(c.X - off, c.Y + hy));
            dc.DrawLine(bar, new Point(c.X + off, c.Y - hy), new Point(c.X + off, c.Y + hy));
        }

        /// <summary>
        /// Кільця, що «обертаються»: сегменти йдуть у порядку чорний — синій — білий — жовтий,
        /// і боковим зором здається, що сусідні кільця повільно крутяться в різні боки.
        /// </summary>
        private void Snakes(DrawingContext dc, Point c, double u)
        {
            Brush[] cycle = { Dark, Blue, Light, Yellow };
            int rings = 5;
            double width = u / (rings + 0.6);
            var counts = new int[rings];
            int total = 0;
            for (int ri = 0; ri < rings; ri++) { counts[ri] = 12 + ri * 8; total += counts[ri]; }
            var bad = Defects(total);
            int index = 0;
            for (int ri = 0; ri < rings; ri++)
            {
                double rad = width * (ri + 1.1);
                int n = counts[ri];
                for (int k = 0; k < n; k++, index++)
                {
                    bool sick = bad.Contains(index);
                    int order = ri % 2 == 0 ? k : n - 1 - k;   // сусідні кільця — у різні боки
                    double a0 = 2 * Math.PI * k / n, a1 = 2 * Math.PI * (k + 1) / n;
                    double rr = sick ? rad + width * 0.3 : rad;   // хворий сегмент випав з кільця
                    var pen = new Pen(sick ? Sick : cycle[order % 4], width * 0.78);
                    pen.Freeze();
                    dc.DrawLine(pen, new Point(c.X + Math.Cos(a0) * rr, c.Y + Math.Sin(a0) * rr),
                                     new Point(c.X + Math.Cos(a1) * rr, c.Y + Math.Sin(a1) * rr));
                }
            }
        }

        // ---------- фрактали: один елемент на натискання ----------

        /// <summary>
        /// Дерево: кожне натискання — нова гілка (зверху вниз, зліва направо). Гілка, що з'явилась
        /// на помилковому натисканні, червона, з наростом і зламана; усе, що росте з неї, криве,
        /// коротше й бурувате — хвороба слабшає лише через кілька поколінь.
        /// Швидший набір розкриває крону ширше, нерівний ритм хилить дерево вбік.
        /// </summary>
        private void Tree(DrawingContext dc, Point c, double u)
        {
            const int max = 1023;                               // 10 рівнів
            int visible = Math.Min(max, count + 1);
            double angle = 14 + Math.Max(0, Math.Min(1, cpm / 500.0)) * 22;
            double skew = (100 - Math.Max(20, Math.Min(100, rhythm))) / 80.0 * 6;
            Branch(dc, 0, visible, new Point(c.X, c.Y + u * 0.95), -90, u * 0.46, angle, skew, 0, 0);
        }

        private void Branch(DrawingContext dc, int k, int visible, Point from, double dir, double len, double angle, double skew, int level, double sick)
        {
            if (k >= visible || len < 1.2) return;
            // Гілка k виросла на натисканні k (після 1023 гілок помилки вражають уже наявні гілки по колу).
            bool bad = errors.Contains(k);
            if (!bad && count > 1023) foreach (var e in errorList) if (e >= 1023 && e % 1023 == k) { bad = true; break; }
            sick = bad ? 1 : sick * 0.78;
            if (sick > 0.04)
            {
                dir += (Hash(k) - 0.5) * 80 * sick;      // хвора гілка росте не туди
                len *= 1 - 0.35 * sick;                  // і коротша
            }
            double rad = dir * Math.PI / 180;
            var to = new Point(from.X + Math.Cos(rad) * len, from.Y + Math.Sin(rad) * len);
            var brush = sick > 0.55 ? Sick : sick > 0.12 ? SickDim : level % 2 == 0 ? Light : Dark;
            double thick = Math.Max(1, len * 0.09);
            if (bad)
            {
                // злам: гілка йде уступом, на місці зламу — наріст
                var mid = new Point((from.X + to.X) / 2 + Math.Sin(rad) * len * 0.18, (from.Y + to.Y) / 2 - Math.Cos(rad) * len * 0.18);
                dc.DrawLine(PenOf(brush, thick), from, mid);
                dc.DrawLine(PenOf(brush, thick), mid, to);
                dc.DrawEllipse(Sick, null, mid, Math.Max(2.5, thick * 1.5), Math.Max(2.5, thick * 1.5));
            }
            else dc.DrawLine(PenOf(brush, thick), from, to);

            Branch(dc, 2 * k + 1, visible, to, dir - angle + skew, len * 0.72, angle, skew, level + 1, sick);
            Branch(dc, 2 * k + 2, visible, to, dir + angle + skew, len * 0.72, angle, skew, level + 1, sick);
        }

        /// <summary>
        /// Соняшник: кожне натискання — насінина. Помилкова насінина червона й більша, і вона збиває
        /// кут для всіх наступних: у візерунку лишається шрам, від якого спіралі йдуть уже інакше.
        /// </summary>
        private void Sunflower(DrawingContext dc, Point c, double u)
        {
            const int max = 720;
            int n = Math.Min(max, count + 8);
            double angle = 137.5 + (Math.Max(0, Math.Min(600, cpm)) - 300) / 300.0 * 0.4;
            double k = u / Math.Sqrt(max);
            double phase = 0;
            int sinceError = 1000;
            for (int i = 1; i <= n; i++)
            {
                bool bad = errors.Contains(i);
                if (bad) { phase += 9 + Hash(i) * 14; sinceError = 0; } else sinceError++;
                double rr = k * Math.Sqrt(i), a = (i * angle + phase) * Math.PI / 180;
                double dot = 1.2 + rr / u * (u * 0.035);
                if (bad) { dot *= 1.9; rr += k * 1.5; }
                var brush = bad ? Sick : sinceError < 14 ? SickDim : i % 2 == 0 ? Light : Dark;
                dc.DrawEllipse(brush, null, new Point(c.X + Math.Cos(a) * rr, c.Y + Math.Sin(a) * rr), dot, dot);
            }
        }

        /// <summary>
        /// Сніжинка Коха малюється по контуру: два зубці на натискання. На помилці контур провалюється
        /// всередину і кілька наступних зубців ідуть криво й червоним, поки лінія не вирівняється.
        /// </summary>
        private void Snowflake(DrawingContext dc, Point c, double u)
        {
            var pts = new List<Point>();
            Point P(double deg) => new Point(c.X + Math.Cos(deg * Math.PI / 180) * u * 0.9, c.Y + Math.Sin(deg * Math.PI / 180) * u * 0.9);
            Point a = P(-90), b = P(30), d = P(150);
            double spike = 0.6 + Math.Max(20, Math.Min(100, rhythm)) / 100.0 * 0.4;
            pts.Add(a);
            Koch(pts, a, b, 4, spike);
            Koch(pts, b, d, 4, spike);
            Koch(pts, d, a, 4, spike);
            int segments = pts.Count - 1;                 // 768
            int shown = Math.Min(segments, count * 2 + 6);

            // Хвороба: від помилки контур зсувається до центру і поступово повертається.
            var sick = new double[pts.Count];
            foreach (var e in errorList)
            {
                int at = (e * 2) % segments;
                for (int j = 0; j < 14 && at + j < pts.Count; j++)
                    sick[at + j] = Math.Max(sick[at + j], 1 - j / 14.0);
            }
            for (int i = 0; i < pts.Count; i++)
            {
                if (sick[i] <= 0) continue;
                double f = sick[i] * (0.16 + 0.12 * Hash(i));
                pts[i] = new Point(pts[i].X + (c.X - pts[i].X) * f, pts[i].Y + (c.Y - pts[i].Y) * f);
            }

            var dark = PenOf(Dark, Math.Max(2, u * 0.028));
            var light = PenOf(Light, Math.Max(1, u * 0.011));
            var red = PenOf(Sick, Math.Max(2, u * 0.022));
            for (int i = 0; i < shown; i++)
            {
                if (sick[i] > 0 || sick[i + 1] > 0) dc.DrawLine(red, pts[i], pts[i + 1]);
                else { dc.DrawLine(dark, pts[i], pts[i + 1]); dc.DrawLine(light, pts[i], pts[i + 1]); }
            }
        }

        private static void Koch(List<Point> pts, Point a, Point b, int level, double spike)
        {
            if (level == 0) { pts.Add(b); return; }
            double dx = (b.X - a.X) / 3, dy = (b.Y - a.Y) / 3;
            var p1 = new Point(a.X + dx, a.Y + dy);
            var p2 = new Point(a.X + 2 * dx, a.Y + 2 * dy);
            double hgt = Math.Sqrt(3) / 2 * spike;
            // Вершина зубця: від середини відрізка назовні, перпендикулярно до нього.
            var peak = new Point((a.X + b.X) / 2 + dy * hgt, (a.Y + b.Y) / 2 - dx * hgt);
            Koch(pts, a, p1, level - 1, spike);
            Koch(pts, p1, peak, level - 1, spike);
            Koch(pts, peak, p2, level - 1, spike);
            Koch(pts, p2, b, level - 1, spike);
        }

        /// <summary>
        /// Крива дракона: кожне натискання — ланка, що повертає ліворуч або праворуч за суворим правилом.
        /// Помилка — це поворот не в той бік: від цього місця вся подальша крива йде іншим шляхом
        /// і наїжджає сама на себе. Чистий набір дає правильного дракона без жодного самоперетину.
        /// </summary>
        private void Dragon(DrawingContext dc, Point c, double u)
        {
            const int max = 1024;
            int n = Math.Min(max, count + 3);
            var pts = new List<Point>(n + 1) { new Point(0, 0), new Point(1, 0) };
            var bad = new bool[n + 1];
            int dx = 1, dy = 0;
            for (int i = 1; i < n; i++)
            {
                bool right = (((i & -i) << 1) & i) != 0;
                if (errors.Contains(i)) { right = !right; bad[i] = true; }
                int t = dx;
                if (right) { dx = -dy; dy = t; } else { dx = dy; dy = -t; }
                var last = pts[pts.Count - 1];
                pts.Add(new Point(last.X + dx, last.Y + dy));
            }

            // Масштаб: уся крива вміщується в коло; поки вона мала — не роздуваємо її на весь екран.
            double minX = 0, maxX = 0, minY = 0, maxY = 0;
            foreach (var p in pts) { minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); }
            double span = Math.Max(10, Math.Max(maxX - minX, maxY - minY));
            double s = u * 1.35 / span, ox = c.X - (minX + maxX) / 2 * s, oy = c.Y - (minY + maxY) / 2 * s;
            Point M(Point p) => new Point(ox + p.X * s, oy + p.Y * s);

            double thick = Math.Max(1.2, s * 0.42);
            int sinceError = 1000;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                if (bad[i]) sinceError = 0; else sinceError++;
                var brush = sinceError < 3 ? Sick : sinceError < 24 ? SickDim : (i / 16) % 2 == 0 ? Light : Dark;
                dc.DrawLine(PenOf(brush, thick), M(pts[i]), M(pts[i + 1]));
                if (bad[i]) dc.DrawEllipse(Sick, null, M(pts[i]), thick * 1.6, thick * 1.6);
            }
        }
    }
}
