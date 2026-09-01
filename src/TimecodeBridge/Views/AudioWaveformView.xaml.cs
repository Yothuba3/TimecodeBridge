using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimecodeBridge.ViewModels;

namespace TimecodeBridge.Views;

public partial class AudioWaveformView : UserControl
{
    private static readonly SolidColorBrush PeakBrushRed = new(Color.FromRgb(0xE0, 0x52, 0x52));
    private static readonly SolidColorBrush PeakBrushYellow = new(Color.FromRgb(0xE0, 0xA0, 0x40));
    private static readonly SolidColorBrush PeakBrushGreen = new(Color.FromRgb(0x60, 0xC0, 0x60));

    static AudioWaveformView()
    {
        PeakBrushRed.Freeze();
        PeakBrushYellow.Freeze();
        PeakBrushGreen.Freeze();
    }

    private readonly float[] _readBuffer = new float[AudioWaveformViewModel.DisplaySampleCount];
    private PointCollection? _reusablePoints;

    public AudioWaveformView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (DataContext is not AudioWaveformViewModel vm) return;
        if (!vm.ConsumeUpdate(out float peakLevel)) return;

        double w = WaveformCanvas.ActualWidth;
        double h = WaveformCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        vm.CopyDisplayBuffer(_readBuffer);

        int count = AudioWaveformViewModel.DisplaySampleCount;

        // Reuse or create PointCollection
        if (_reusablePoints == null || _reusablePoints.Count != count)
        {
            _reusablePoints = new PointCollection(count);
            for (int i = 0; i < count; i++)
                _reusablePoints.Add(default);
        }

        for (int i = 0; i < count; i++)
        {
            double x = (double)i / (count - 1) * w;
            double y = (0.5 - _readBuffer[i] * 0.5) * h;
            _reusablePoints[i] = new Point(x, y);
        }

        WaveformLine.Points = _reusablePoints;

        // Update peak bar（コンテナ実高さに合わせて100%が枠内に収まるようにする）
        double peakHeight = Math.Clamp(peakLevel, 0, 1) * PeakContainer.ActualHeight;
        PeakBar.Height = peakHeight;

        PeakBar.Background = peakLevel switch
        {
            >= 0.85f => PeakBrushRed,
            >= 0.6f => PeakBrushYellow,
            _ => PeakBrushGreen,
        };
    }
}
