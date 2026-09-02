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

    // 表示区間: 約0.1フレーム分（30fps基準、約3.3ms）。LTC矩形を数個まで拡大して形を見やすくする。
    private const double WindowSeconds = 0.1 / 30.0;
    private static readonly int WindowSamples =
        (int)(AudioWaveformViewModel.SampleRate * WindowSeconds);

    private readonly float[] _readBuffer = new float[WindowSamples];
    private readonly DispatcherTimer _renderTimer;

    public AudioWaveformView()
    {
        InitializeComponent();

        // 約30fpsで再描画
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

        vm.CopyRecent(_readBuffer, WindowSamples);

        // 16ms窓の生サンプル(48kHzで約800個)を幅いっぱいに描く。キャンバス幅と同程度の
        // サンプル数なので、点を結ぶだけでLTCの矩形が階段状にはっきり見える。
        WaveformLine.Points = BuildWaveform(_readBuffer, w, h);

        PeakBar.Height = Math.Clamp(peakLevel, 0, 1) * PeakContainer.Bounds.Height;
        PeakBar.Background = peakLevel switch
        {
            >= 0.85f => PeakBrushRed,
            >= 0.6f => PeakBrushYellow,
            _ => PeakBrushGreen,
        };
    }

    private static List<Point> BuildWaveform(float[] samples, double w, double h)
    {
        int n = samples.Length;
        var points = new List<Point>(n);
        for (int i = 0; i < n; i++)
        {
            double x = n == 1 ? 0 : (double)i / (n - 1) * w;
            double y = (0.5 - samples[i] * 0.5) * h;
            points.Add(new Point(x, y));
        }
        return points;
    }
}
