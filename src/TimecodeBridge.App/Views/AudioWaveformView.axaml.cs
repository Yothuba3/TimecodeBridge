using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using TimecodeBridge.App.ViewModels;

namespace TimecodeBridge.App.Views;

public partial class AudioWaveformView : UserControl
{
    private static readonly SolidColorBrush PeakBrushRed = new(Color.FromRgb(0xE0, 0x52, 0x52));
    private static readonly SolidColorBrush PeakBrushYellow = new(Color.FromRgb(0xE0, 0xA0, 0x40));
    private static readonly SolidColorBrush PeakBrushGreen = new(Color.FromRgb(0x60, 0xC0, 0x60));

    private readonly float[] _readBuffer = new float[AudioWaveformViewModel.DisplaySampleCount];
    private readonly DispatcherTimer _renderTimer;

    public AudioWaveformView()
    {
        InitializeComponent();

        // WPFのCompositionTarget.Rendering相当: 約30fpsで再描画
        _renderTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _renderTimer.Tick += OnRenderTick;

        AttachedToVisualTree += (_, _) => _renderTimer.Start();
        DetachedFromVisualTree += (_, _) => _renderTimer.Stop();
    }

    private void OnRenderTick(object? sender, EventArgs e)
    {
        if (DataContext is not AudioWaveformViewModel vm) return;
        if (!vm.ConsumeUpdate(out float peakLevel)) return;

        double w = WaveformCanvas.Bounds.Width;
        double h = WaveformCanvas.Bounds.Height;
        if (w <= 0 || h <= 0) return;

        vm.CopyDisplayBuffer(_readBuffer);

        int count = AudioWaveformViewModel.DisplaySampleCount;
        var points = new List<Point>(count);
        for (int i = 0; i < count; i++)
        {
            double x = (double)i / (count - 1) * w;
            double y = (0.5 - _readBuffer[i] * 0.5) * h;
            points.Add(new Point(x, y));
        }

        WaveformLine.Points = points;

        // Update peak bar（コンテナ実高さに合わせて100%が枠内に収まるようにする）
        PeakBar.Height = Math.Clamp(peakLevel, 0, 1) * PeakContainer.Bounds.Height;
        PeakBar.Background = peakLevel switch
        {
            >= 0.85f => PeakBrushRed,
            >= 0.6f => PeakBrushYellow,
            _ => PeakBrushGreen,
        };
    }
}
