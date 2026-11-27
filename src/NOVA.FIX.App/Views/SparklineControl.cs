using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace NOVA.FIX.App.Views;

/// <summary>
/// Lightweight sparkline chart drawn from a comma-separated numeric series.
/// No third-party chart library — pure WPF drawing, bounded by the sampler.
/// </summary>
public sealed class SparklineControl : FrameworkElement
{
    public static readonly DependencyProperty SeriesProperty = DependencyProperty.Register(
        nameof(Series), typeof(string), typeof(SparklineControl),
        new FrameworkPropertyMetadata(string.Empty,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(SparklineControl),
        new FrameworkPropertyMetadata(Brushes.Cyan,
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(SparklineControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public string Series
    {
        get => (string)GetValue(SeriesProperty);
        set => SetValue(SeriesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var values = Parse(Series);
        if (values.Count < 2) return;

        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        var min = values.Min();
        var max = values.Max();
        var range = Math.Max(1e-6, max - min);

        var points = new PointCollection(values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            var x = i * (w - 2) / (values.Count - 1) + 1;
            var y = h - 2 - (values[i] - min) / range * (h - 6);
            points.Add(new Point(x, y));
        }

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(points[0], isFilled: false, isClosed: false);
            ctx.PolyLineTo(points.Skip(1).ToList(), isStroked: true, isSmoothJoin: false);
        }
        geo.Freeze();

        // area fill under the line
        if (Fill is not null)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(points[0], isFilled: true, isClosed: false);
                ctx.PolyLineTo(points.Skip(1).ToList(), isStroked: false, isSmoothJoin: false);
                ctx.LineTo(new Point(w - 1, h), isStroked: false, isSmoothJoin: false);
                ctx.LineTo(new Point(1, h), isStroked: false, isSmoothJoin: false);
            }
            area.Freeze();
            dc.DrawGeometry(Fill, null, area);
        }

        dc.DrawGeometry(null, new Pen(Stroke, 1.6), geo);
    }

    private static IReadOnlyList<double> Parse(string? series)
    {
        if (string.IsNullOrWhiteSpace(series)) return Array.Empty<double>();
        var list = new List<double>();
        foreach (var part in series.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var v))
                list.Add(v);
        }
        return list;
    }
}
