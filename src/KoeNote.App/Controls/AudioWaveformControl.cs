using System.Collections;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace KoeNote.App.Controls;

public sealed class AudioWaveformControl : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples),
        typeof(IEnumerable),
        typeof(AudioWaveformControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PositionSecondsProperty = DependencyProperty.Register(
        nameof(PositionSeconds),
        typeof(double),
        typeof(AudioWaveformControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty DurationSecondsProperty = DependencyProperty.Register(
        nameof(DurationSeconds),
        typeof(double),
        typeof(AudioWaveformControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public IEnumerable? Samples
    {
        get => (IEnumerable?)GetValue(SamplesProperty);
        set => SetValue(SamplesProperty, value);
    }

    public double PositionSeconds
    {
        get => (double)GetValue(PositionSecondsProperty);
        set => SetValue(PositionSecondsProperty, value);
    }

    public double DurationSeconds
    {
        get => (double)GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(120, 46);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        drawingContext.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(244, 248, 246)), null, bounds, 8, 8);
        var values = ReadSamples();
        if (values.Count == 0)
        {
            DrawEmptyLine(drawingContext, bounds);
            return;
        }

        var progress = DurationSeconds > 0 ? Math.Clamp(PositionSeconds / DurationSeconds, 0, 1) : 0;
        var playedBrush = new SolidColorBrush(Color.FromRgb(47, 143, 91));
        var remainingBrush = new SolidColorBrush(Color.FromRgb(183, 216, 196));
        var xStep = bounds.Width / values.Count;
        var barWidth = Math.Clamp(xStep * 0.58, 2, 5);
        var centerY = bounds.Height / 2;
        var maxBarHeight = Math.Max(8, bounds.Height - 10);

        for (var i = 0; i < values.Count; i++)
        {
            var sample = Math.Clamp(values[i], 0.04, 1.0);
            var height = Math.Max(4, sample * maxBarHeight);
            var x = i * xStep + (xStep - barWidth) / 2;
            var y = centerY - height / 2;
            var brush = (i + 0.5) / values.Count <= progress ? playedBrush : remainingBrush;
            drawingContext.DrawRoundedRectangle(brush, null, new Rect(x, y, barWidth, height), barWidth / 2, barWidth / 2);
        }

        if (DurationSeconds > 0)
        {
            var markerX = progress * bounds.Width;
            drawingContext.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(21, 128, 61)), 1.5), new Point(markerX, 5), new Point(markerX, bounds.Height - 5));
        }
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        CaptureMouse();
        SeekTo(e.GetPosition(this).X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            SeekTo(e.GetPosition(this).X);
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (IsMouseCaptured)
        {
            SeekTo(e.GetPosition(this).X);
            ReleaseMouseCapture();
        }
    }

    private void SeekTo(double x)
    {
        if (DurationSeconds <= 0 || ActualWidth <= 0)
        {
            return;
        }

        PositionSeconds = Math.Clamp(x / ActualWidth, 0, 1) * DurationSeconds;
    }

    private List<double> ReadSamples()
    {
        if (Samples is null)
        {
            return [];
        }

        return Samples.Cast<object>()
            .Select(value => value is double sample ? sample : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
    }

    private static void DrawEmptyLine(DrawingContext drawingContext, Rect bounds)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(183, 216, 196)), 2);
        var y = bounds.Height / 2;
        drawingContext.DrawLine(pen, new Point(10, y), new Point(Math.Max(10, bounds.Width - 10), y));
    }
}
