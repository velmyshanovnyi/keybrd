using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Keybrd.Controls
{
    /// <summary>Простий лінійний графік по днях: лінія, легка заливка, точки з підказками.</summary>
    public sealed class LineChart : UserControl
    {
        public sealed class Point
        {
            public DateTime Day;
            public double Value;
            public string Tip;
        }

        private readonly Canvas canvas = new Canvas { ClipToBounds = true };
        private List<Point> points = new List<Point>();
        private Func<double, string> format = v => Math.Round(v).ToString();
        private double? fixedMin, fixedMax;

        public LineChart()
        {
            Content = canvas;
            SizeChanged += (s, e) => Draw();
        }

        public void SetData(IEnumerable<Point> data, Func<double, string> fmt, double? min = null, double? max = null)
        {
            points = data.ToList();
            format = fmt ?? format;
            fixedMin = min;
            fixedMax = max;
            Draw();
        }

        public void Redraw() => Draw();

        private void Draw()
        {
            canvas.Children.Clear();
            double w = ActualWidth, h = ActualHeight;
            if (w < 40 || h < 40) return;

            if (points.Count == 0)
            {
                var empty = new TextBlock { Text = "Немає даних за цей період", FontSize = 13 };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "FgMutedBrush");
                empty.Measure(new Size(w, h));
                Canvas.SetLeft(empty, (w - empty.DesiredSize.Width) / 2);
                Canvas.SetTop(empty, (h - empty.DesiredSize.Height) / 2);
                canvas.Children.Add(empty);
                return;
            }

            const double left = 40, right = 10, top = 8, bottom = 22;
            double pw = w - left - right, ph = h - top - bottom;

            double lo = fixedMin ?? points.Min(p => p.Value);
            double hi = fixedMax ?? points.Max(p => p.Value);
            if (fixedMin == null) lo = Math.Max(0, lo - (hi - lo) * 0.15 - 1);
            if (fixedMax == null) hi = hi + (hi - lo) * 0.15 + 1;
            if (hi - lo < 1) hi = lo + 1;
            if (fixedMin == null && fixedMax == null)
            {
                // «Круглі» поділки: крок 5, 10, 20, 25, 50… так, щоб вийшло три інтервали.
                double raw = (hi - lo) / 3, step = 5;
                foreach (var s in new double[] { 5, 10, 20, 25, 50, 100, 200, 250, 500 }) { step = s; if (s >= raw) break; }
                lo = Math.Floor(lo / step) * step;
                hi = lo + step * 3;
                while (hi < points.Max(p => p.Value)) hi += step;
            }

            double X(int i) => left + (points.Count == 1 ? pw / 2 : pw * i / (points.Count - 1));
            double Y(double v) => top + ph * (1 - (Math.Max(lo, Math.Min(hi, v)) - lo) / (hi - lo));

            // сітка й підписи осі Y
            for (int g = 0; g <= 3; g++)
            {
                double v = lo + (hi - lo) * g / 3;
                double y = Y(v);
                var line = new Line { X1 = left, X2 = w - right, Y1 = y, Y2 = y, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 } };
                line.SetResourceReference(Shape.StrokeProperty, "PanelBorderBrush");
                canvas.Children.Add(line);
                AddLabel(format(v), 0, y - 8, left - 6, TextAlignment.Right);
            }

            // підписи осі X: перший, середній, останній день
            var idx = new SortedSet<int> { 0, points.Count - 1 };
            if (points.Count > 4) idx.Add(points.Count / 2);
            foreach (var i in idx)
                AddLabel(points[i].Day.ToString("dd.MM"), X(i) - 30, h - bottom + 4, 60, TextAlignment.Center);

            // заливка під лінією
            var area = new Polygon { Opacity = 0.14 };
            area.SetResourceReference(Shape.FillProperty, "AccentBrush");
            area.Points.Add(new System.Windows.Point(X(0), top + ph));
            for (int i = 0; i < points.Count; i++) area.Points.Add(new System.Windows.Point(X(i), Y(points[i].Value)));
            area.Points.Add(new System.Windows.Point(X(points.Count - 1), top + ph));
            canvas.Children.Add(area);

            var poly = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            poly.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
            for (int i = 0; i < points.Count; i++) poly.Points.Add(new System.Windows.Point(X(i), Y(points[i].Value)));
            canvas.Children.Add(poly);

            for (int i = 0; i < points.Count; i++)
            {
                var dot = new Ellipse { Width = 8, Height = 8, StrokeThickness = 2, ToolTip = points[i].Tip };
                dot.SetResourceReference(Shape.StrokeProperty, "AccentBrush");
                dot.SetResourceReference(Shape.FillProperty, "KeyBgBrush");
                Canvas.SetLeft(dot, X(i) - 4);
                Canvas.SetTop(dot, Y(points[i].Value) - 4);
                canvas.Children.Add(dot);
            }
        }

        private void AddLabel(string text, double x, double y, double width, TextAlignment align)
        {
            var t = new TextBlock { Text = text, FontSize = 11, Width = width, TextAlignment = align };
            t.SetResourceReference(TextBlock.ForegroundProperty, "FgMutedBrush");
            Canvas.SetLeft(t, x);
            Canvas.SetTop(t, y);
            canvas.Children.Add(t);
        }
    }
}
