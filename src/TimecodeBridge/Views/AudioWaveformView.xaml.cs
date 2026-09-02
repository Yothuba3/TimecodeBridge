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

    // 表示区間: 約0.1フレーム分（30fps基準、約3.3ms）。LTC矩形を数個まで拡大して形を見やすくする。
    private const double WindowSeconds = 0.1 / 30.0;
    private static readonly int WindowSamples =
        (int)(AudioWaveformViewModel.SampleRate * WindowSeconds);

    private readonly float[] _readBuffer = new float[WindowSamples];
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

        vm.CopyRecent(_readBuffer, WindowSamples);

        // 16ms窓の生サンプル(48kHzで約800個)を幅いっぱいに描く。キャンバス幅と同程度の
        // サンプル数なので、点を結ぶだけでLTCの矩形が階段状にはっきり見える。
        int count = WindowSamples;

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
