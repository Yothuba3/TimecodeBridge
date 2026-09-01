using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.macOS.ViewModels;

/// <summary>
/// タイムコード表示・ソース制御のViewModel（macOS版）
/// Windows版 TimecodeViewModel と同一のUI契約を提供する
/// </summary>
public partial class TimecodeViewModel : DispatcherViewModel
{
    private readonly ITimecodeEngine _timecodeEngine;
    private readonly IAudioDeviceService _audioDeviceService;
    private readonly ICueManager _cueManager;
    private readonly IProjectService _projectService;
    private bool _hasEverReceived;
    private bool _generatorInitialized;
    private bool _generatorSettingsDirty;
    private bool _restoring;

    // 受信前でも空欄にせずゼロ表記を出す（FallbackValueはバインディング失敗時にしか効かない）
    [ObservableProperty] private string _rawTimecodeDisplay = "00:00:00:00";
    [ObservableProperty] private string _offsetTimecodeDisplay = "00:00:00:00";

    /// <summary>受信フレームから検出したフレームレート表示（例: "30fps" / "29.97DF"）。</summary>
    [ObservableProperty] private string _detectedFrameRateText = "";

    /// <summary>信号喪失時の補足情報（例: "最終受信 12秒前"）。</summary>
    [ObservableProperty] private string _signalDetailText = "";

    /// <summary>ステータスバー用の「ソース: 状態」要約。</summary>
    public string SourceStatusSummary =>
        $"{(SelectedSource == TimecodeSourceType.Generator ? "内部生成" : "LTC")}: {StatusText}";

    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(SourceStatusSummary));

    private DateTime? _lastFrameReceivedAt;
    // テストホスト等Dispatcherループのないスレッドで終了しなくならないよう
    // バックグラウンドのThreading.Timer + RunOnUIThreadで更新する
    private System.Threading.Timer? _signalInfoTimer;
    [ObservableProperty] private bool _isReceiving;
    [ObservableProperty] private string _statusText = "停止";
    [ObservableProperty] private AudioDeviceInfo? _selectedDevice;

    // Generator properties
    [ObservableProperty] private TimecodeSourceType _selectedSource = TimecodeSourceType.Ltc;
    [ObservableProperty] private bool _isGeneratorRunning;
    [ObservableProperty] private string _generatorStartTime = "00:00:00:00";
    [ObservableProperty] private FrameRate _generatorFrameRate = FrameRate.Fps30;
    [ObservableProperty] private AudioDeviceInfo? _selectedOutputDevice;
    [ObservableProperty] private float _outputVolumeLevel = 0.8f;
    [ObservableProperty] private bool _isLtcOutputActive;
    [ObservableProperty] private bool _isTriggerMuted;

    public bool IsGeneratorMode
    {
        get => SelectedSource == TimecodeSourceType.Generator;
        set { if (value) SelectedSource = TimecodeSourceType.Generator; }
    }

    public bool IsLtcMode
    {
        get => SelectedSource == TimecodeSourceType.Ltc;
        set { if (value) SelectedSource = TimecodeSourceType.Ltc; }
    }

    public ObservableCollection<AudioDeviceInfo> AudioDevices { get; } = new();
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = new();

    public IReadOnlyList<FrameRate> AvailableFrameRates { get; } =
        [FrameRate.Fps24, FrameRate.Fps25, FrameRate.Fps2997Drop, FrameRate.Fps30];

    private TimecodeOffset _offset;

    public TimecodeOffset Offset
    {
        get => _offset;
        set
        {
            if (SetProperty(ref _offset, value))
            {
                _timecodeEngine.Offset = value;
                OnPropertyChanged(nameof(OffsetText));
                MarkDirty();
            }
        }
    }

    /// <summary>
    /// エンジン側のオフセットを表示へ反映する（プロジェクト読込・新規作成用）。
    /// エンジンへの書き戻しやdirty化はしない。
    /// </summary>
    public void SyncOffsetFromEngine()
    {
        _offset = _timecodeEngine.Offset;
        OnPropertyChanged(nameof(Offset));
        OnPropertyChanged(nameof(OffsetText));
    }

    public string OffsetText
    {
        get => _offset.ToString();
        set
        {
            if (TimecodeOffset.TryParse(value, _timecodeEngine.FrameRate, out var parsed))
            {
                Offset = parsed;
            }
            // 不正入力がTextBoxに残らないよう、常に実値の表記へ戻す
            Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(OffsetText)));
        }
    }

    /// <summary>Cue-Syncワンショット（TimecodeDisplayView内のボタン領域が参照する）。</summary>
    public CueSyncViewModel CueSync { get; }

    public TimecodeViewModel(
        ITimecodeEngine timecodeEngine,
        IAudioDeviceService audioDeviceService,
        ICueManager cueManager,
        IProjectService projectService,
        CueSyncViewModel cueSyncViewModel)
    {
        _timecodeEngine = timecodeEngine;
        _audioDeviceService = audioDeviceService;
        _cueManager = cueManager;
        _projectService = projectService;
        CueSync = cueSyncViewModel;
        _offset = timecodeEngine.Offset;

        _timecodeEngine.TimecodeUpdated += OnTimecodeUpdated;
        _timecodeEngine.StatusChanged += OnStatusChanged;

        RefreshAudioDevices();

        // 信号喪失時の経過秒表示を1秒ごとに更新する
        _signalInfoTimer = new System.Threading.Timer(
            _ => RunOnUIThread(UpdateSignalDetail), null,
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void UpdateSignalDetail()
    {
        if (StatusText == "信号喪失" && _lastFrameReceivedAt is { } lastAt)
        {
            var seconds = (int)(DateTime.Now - lastAt).TotalSeconds;
            SignalDetailText = $"最終受信 {seconds}秒前";
        }
        else if (SignalDetailText.Length > 0 && StatusText != "信号喪失")
        {
            SignalDetailText = "";
        }
    }

    private void MarkDirty()
    {
        if (!_restoring) _projectService.MarkAsChanged();
    }

    partial void OnGeneratorFrameRateChanged(FrameRate value)
    {
        // 稼働中のジェネレーターとエンジンのレートがずれると表示が壊れるため、
        // 初期化済みの間は即時変更せず次回リセット時にまとめて適用する
        if (!_generatorInitialized)
        {
            _timecodeEngine.FrameRate = value;
        }
        _generatorSettingsDirty = _generatorInitialized;
        MarkDirty();
    }

    // 出力デバイス・音量の変更もリセット時の再初期化で反映する
    partial void OnSelectedOutputDeviceChanged(AudioDeviceInfo? value)
    {
        _generatorSettingsDirty = _generatorInitialized;
        MarkDirty();
    }

    partial void OnOutputVolumeLevelChanged(float value)
    {
        _generatorSettingsDirty = _generatorInitialized;
        MarkDirty();
    }

    partial void OnGeneratorStartTimeChanged(string value) => MarkDirty();

    partial void OnIsTriggerMutedChanged(bool value)
    {
        _cueManager.IsMuted = value;
    }

    partial void OnSelectedSourceChanged(TimecodeSourceType value)
    {
        OnPropertyChanged(nameof(IsGeneratorMode));
        OnPropertyChanged(nameof(IsLtcMode));
        OnPropertyChanged(nameof(SourceStatusSummary));

        MarkDirty();

        // プロジェクト復元中は RestoreSourceSettings が停止・再開をまとめて面倒を見る
        if (_restoring) return;

        _timecodeEngine.Stop();
        _hasEverReceived = false;
        _generatorInitialized = false;
        _generatorSettingsDirty = false;
        StatusText = "停止";
        IsReceiving = false;
        IsGeneratorRunning = false;
        IsLtcOutputActive = false;

        // If switching to LTC and a device is selected, auto-start
        if (value == TimecodeSourceType.Ltc && SelectedDevice is not null)
        {
            try
            {
                _timecodeEngine.StartLtc(SelectedDevice.Id, SelectedDevice.IsLoopback);
            }
            catch (Exception ex)
            {
                StatusText = "エラー";
                System.Diagnostics.Debug.WriteLine($"Device start failed: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    private void RefreshAudioDevices()
    {
        // Clear で選択が null に飛ぶため、更新前の選択を控えて更新後に復元する
        var prevInput = SelectedDevice;
        var prevOutput = SelectedOutputDevice;
        _restoring = true;
        try
        {
            AudioDevices.Clear();
            OutputDevices.Clear();

            foreach (var device in _audioDeviceService.GetCaptureDevices())
            {
                AudioDevices.Add(device);
            }

            foreach (var device in _audioDeviceService.GetRenderDevices())
            {
                OutputDevices.Add(device);
            }

            SelectedDevice = AudioDevices.FirstOrDefault(d => prevInput is not null && d.Id == prevInput.Id && d.IsLoopback == prevInput.IsLoopback);
            SelectedOutputDevice = OutputDevices.FirstOrDefault(d => prevOutput is not null && d.Id == prevOutput.Id);
        }
        catch (Exception ex)
        {
            // デバイス列挙の例外でフラグが立ちっぱなしになると全コールバックが死ぬため必ず復帰させる
            StatusText = "エラー";
            System.Diagnostics.Debug.WriteLine($"Device enumeration failed: {ex.Message}");
        }
        finally
        {
            _restoring = false;
        }

        // 使用中のデバイスが消えていたら受信を止めてUIと実態を一致させる
        if (SelectedSource == TimecodeSourceType.Ltc && prevInput is not null && SelectedDevice is null)
        {
            _timecodeEngine.Stop();
            IsReceiving = false;
            StatusText = "停止";
        }
    }

    partial void OnSelectedDeviceChanged(AudioDeviceInfo? value)
    {
        if (_restoring) return;
        if (SelectedSource != TimecodeSourceType.Ltc) return;

        MarkDirty();
        _timecodeEngine.Stop();

        if (value is null)
        {
            // 未選択になったら受信も止める（旧デバイスで受信が残り続けるのを防ぐ）
            IsReceiving = false;
            StatusText = "停止";
            return;
        }

        try
        {
            _timecodeEngine.StartLtc(value.Id, value.IsLoopback);
        }
        catch (Exception ex)
        {
            StatusText = "エラー";
            System.Diagnostics.Debug.WriteLine($"Device start failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void StartGenerator()
    {
        if (SelectedSource != TimecodeSourceType.Generator) return;

        try
        {
            if (_generatorInitialized)
            {
                // Resume from paused position
                _timecodeEngine.ResumeGenerator();
            }
            else
            {
                // First start: initialize with settings
                var startTime = ParseTimecodeInput(GeneratorStartTime, GeneratorFrameRate);
                GeneratorStartTime = startTime.ToString(); // 丸め結果を入力欄へ反映（表示と実値の乖離防止）
                var settings = new GeneratorSettings
                {
                    FrameRate = GeneratorFrameRate,
                    StartTime = startTime,
                    OutputDeviceId = SelectedOutputDevice?.Id ?? string.Empty,
                    VolumeLevel = OutputVolumeLevel,
                };
                _cueManager.ResetTracking();
                _timecodeEngine.StartGenerator(settings);
                _generatorInitialized = true;
            }

            IsGeneratorRunning = true;
            IsLtcOutputActive = SelectedOutputDevice is not null;
            StatusText = "生成中";
        }
        catch (Exception ex)
        {
            StatusText = "エラー";
            System.Diagnostics.Debug.WriteLine($"Generator start failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void StopGenerator()
    {
        // 未開始なら「一時停止」表示にしない
        if (!_generatorInitialized) return;

        _timecodeEngine.StopGenerator();
        IsGeneratorRunning = false;
        StatusText = "一時停止";
    }

    [RelayCommand]
    private void ResetGenerator()
    {
        var startTime = ParseTimecodeInput(GeneratorStartTime, GeneratorFrameRate);
        GeneratorStartTime = startTime.ToString(); // 丸め結果を入力欄へ反映（表示と実値の乖離防止）

        // 前方ジャンプで旧位置〜新開始時間の間のキューが一斉発火しないよう追跡状態を仕切り直す
        _cueManager.ResetTracking();

        // フレームレート・出力デバイス・音量が変更されている場合はジェネレーターを再初期化
        if (_generatorSettingsDirty)
        {
            bool wasRunning = IsGeneratorRunning;
            _timecodeEngine.Stop();
            _generatorInitialized = false;

            var settings = new GeneratorSettings
            {
                FrameRate = GeneratorFrameRate,
                StartTime = startTime,
                OutputDeviceId = SelectedOutputDevice?.Id ?? string.Empty,
                VolumeLevel = OutputVolumeLevel,
            };
            _timecodeEngine.StartGenerator(settings);
            _generatorInitialized = true;
            _generatorSettingsDirty = false;

            if (!wasRunning)
            {
                _timecodeEngine.StopGenerator();
            }
        }
        else
        {
            _timecodeEngine.ResetGenerator(startTime);
        }
    }

    private void OnTimecodeUpdated(object? sender, TimecodeUpdatedEventArgs e)
    {
        RunOnUIThread(() =>
        {
            RawTimecodeDisplay = e.RawTimecode.ToString();
            OffsetTimecodeDisplay = e.OffsetTimecode.ToString();

            _lastFrameReceivedAt = DateTime.Now;
            var frameRate = e.RawTimecode.FrameRate;
            DetectedFrameRateText = frameRate.IsDropFrame() ? "29.97DF" : $"{frameRate.FramesPerSecond()}fps";
        });
    }

    private void OnStatusChanged(object? sender, TimecodeStatusChangedEventArgs e)
    {
        RunOnUIThread(() =>
        {
            IsReceiving = e.IsReceiving;

            if (SelectedSource == TimecodeSourceType.Generator)
            {
                if (e.IsReceiving)
                {
                    _hasEverReceived = true;
                    StatusText = "生成中";
                }
                else
                {
                    IsGeneratorRunning = false;
                    IsLtcOutputActive = false;
                    StatusText = "停止";
                }
            }
            else
            {
                switch (e.Status)
                {
                    case TimecodeReceiveStatus.Receiving:
                        _hasEverReceived = true;
                        StatusText = "受信中";
                        break;
                    case TimecodeReceiveStatus.Freerunning:
                        StatusText = "フリーラン";
                        break;
                    case TimecodeReceiveStatus.NotReceiving:
                        StatusText = _hasEverReceived ? "信号喪失" : "停止";
                        break;
                }
            }
        });
    }

    public TimecodeSourceSettings GetSourceSettings()
    {
        return new TimecodeSourceSettings
        {
            SourceType = SelectedSource,
            DeviceId = SelectedDevice?.Id ?? string.Empty,
            GeneratorSettings = new GeneratorSettings
            {
                FrameRate = GeneratorFrameRate,
                StartTime = ParseTimecodeInput(GeneratorStartTime, GeneratorFrameRate),
                OutputDeviceId = SelectedOutputDevice?.Id ?? string.Empty,
                VolumeLevel = OutputVolumeLevel,
            },
            FreerunDurationSeconds = _timecodeEngine.FreerunDurationSeconds,
        };
    }

    public void RestoreSourceSettings(TimecodeSourceSettings settings)
    {
        // Stop current engine before switching
        _timecodeEngine.Stop();
        _hasEverReceived = false;
        _generatorInitialized = false;
        _generatorSettingsDirty = false;
        IsGeneratorRunning = false;
        IsLtcOutputActive = false;

        // 値が現在と同一でも受信を確実に再開できるよう、復元中は変更コールバックの
        // 自動再開・dirty化を抑止し、最後に明示的に開始する
        _restoring = true;
        try
        {
            GeneratorFrameRate = settings.GeneratorSettings.FrameRate;
            GeneratorStartTime = settings.GeneratorSettings.StartTime.ToString();
            OutputVolumeLevel = settings.GeneratorSettings.VolumeLevel;

            SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == settings.GeneratorSettings.OutputDeviceId);
            SelectedDevice = AudioDevices.FirstOrDefault(d => d.Id == settings.DeviceId);

            _timecodeEngine.FreerunDurationSeconds = settings.FreerunDurationSeconds;

            SelectedSource = settings.SourceType;

            _timecodeEngine.FrameRate = GeneratorFrameRate;
        }
        finally
        {
            _restoring = false;
        }

        StatusText = "停止";
        IsReceiving = false;

        // LTCソースでデバイスが選択されていれば受信を開始する
        if (SelectedSource == TimecodeSourceType.Ltc && SelectedDevice is not null)
        {
            try
            {
                _timecodeEngine.StartLtc(SelectedDevice.Id, SelectedDevice.IsLoopback);
            }
            catch (Exception ex)
            {
                StatusText = "エラー";
                System.Diagnostics.Debug.WriteLine($"Device start failed: {ex.Message}");
            }
        }
    }

    public override void Dispose()
    {
        _signalInfoTimer?.Dispose();
        _timecodeEngine.TimecodeUpdated -= OnTimecodeUpdated;
        _timecodeEngine.StatusChanged -= OnStatusChanged;
        base.Dispose();
    }

    private static TimecodeValue ParseTimecodeInput(string input, FrameRate frameRate)
    {
        var parts = input.Replace(";", ":").Split(':');
        if (parts.Length == 4 &&
            int.TryParse(parts[0], out int h) &&
            int.TryParse(parts[1], out int m) &&
            int.TryParse(parts[2], out int s) &&
            int.TryParse(parts[3], out int f))
        {
            // 範囲外の成分はタイムコードとして有効な値へ丸める
            h = Math.Clamp(h, 0, 23);
            m = Math.Clamp(m, 0, 59);
            s = Math.Clamp(s, 0, 59);
            f = Math.Clamp(f, 0, frameRate.FramesPerSecond() - 1);
            return new TimecodeValue(h, m, s, f, frameRate);
        }
        return new TimecodeValue(0, 0, 0, 0, frameRate);
    }
}
