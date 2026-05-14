using KoeNote.App.Services;

namespace KoeNote.App.ViewModels;

internal sealed class MainContentZoomState
{
    private const double DefaultScale = 1.0;
    private const double MinScale = 0.9;
    private const double MaxScale = 1.5;
    private static readonly double[] Steps = [0.9, 1.0, 1.1, 1.25, 1.5];

    private readonly UiPreferencesService _preferencesService;
    private double _scale;

    public MainContentZoomState(AppPaths paths)
        : this(new UiPreferencesService(paths))
    {
    }

    internal MainContentZoomState(UiPreferencesService preferencesService)
    {
        _preferencesService = preferencesService;
        _scale = NormalizeScale(_preferencesService.Load().MainContentZoomScale);
    }

    public double Scale => _scale;

    public string PercentText => $"{Scale * 100:0}%";

    public string ToolTip => $"文字起こし本文のフォントサイズ {PercentText}";

    public bool CanZoomOut => Scale > MinScale + 0.001;

    public bool CanZoomIn => Scale < MaxScale - 0.001;

    public bool CanReset => Math.Abs(Scale - DefaultScale) > 0.001;

    public bool ZoomOut()
    {
        return SetScale(GetPreviousStep(Scale));
    }

    public bool ZoomIn()
    {
        return SetScale(GetNextStep(Scale));
    }

    public bool Reset()
    {
        return SetScale(DefaultScale);
    }

    public bool SetScale(double value)
    {
        var normalized = NormalizeScale(value);
        if (Math.Abs(_scale - normalized) < 0.001)
        {
            return false;
        }

        _scale = normalized;
        _preferencesService.Save(new UiPreferences(_scale));
        return true;
    }

    private static double GetPreviousStep(double current)
    {
        var normalized = NormalizeScale(current);
        return Steps.LastOrDefault(step => step < normalized - 0.001, MinScale);
    }

    private static double GetNextStep(double current)
    {
        var normalized = NormalizeScale(current);
        return Steps.FirstOrDefault(step => step > normalized + 0.001, MaxScale);
    }

    private static double NormalizeScale(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return DefaultScale;
        }

        return Math.Clamp(value, MinScale, MaxScale);
    }
}
