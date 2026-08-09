using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.CoreAudioApi;
using TimecodeBridge.Models;
using TimecodeBridge.Services;
using TimecodeBridge.Services.Interfaces;

namespace TimecodeBridge.ViewModels;

public partial class TimecodeViewModel : DispatcherViewModel
{
    private readonly ITimecodeEngine _timecodeEngine;
    private bool _hasEverReceived;
    private bool _generatorInitialized;
    private bool _generatorSettingsDirty;
    private bool _restoring;

    // 受信前でも空欄にせずゼロ表記を出す（FallbackValueはバインディング失敗時にしか効かない）
    [ObservableProperty] private string _rawTimecodeDisplay = "00:00:00:00";
    [ObservableProperty] private string _offsetTimecodeDisplay = "00:00:00:00";
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
            // （バインディング更新中の同期通知は書き戻されないためDispatcher経由）
            Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(OffsetText)));
        }
    }

    private readonly ICueManager _cueManager;
    private readonly IProjectService _projectService;

    public TimecodeViewModel(ITimecodeEngine timecodeEngine, ICueManager cueManager, IProjectService projectService)
    {
        _timecodeEngine = timecodeEngine;
        _cueManager = cueManager;
        _projectService = projectService;
        _offset = timecodeEngine.Offset;

        _timecodeEngine.TimecodeUpdated += OnTimecodeUpdated;
        _timecodeEngine.StatusChanged += OnStatusChanged;

        RefreshAudioDevices();
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
    // ponytail: 一時停止からの再開では旧設定のまま（位置保持を優先）。リセットで反映
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
            using var enumerator = new MMDeviceEnumerator();

            // Capture devices (input)
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var device in devices)
            {
                AudioDevices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, IsLoopback: false));
            }

            // Render devices (for loopback capture and LTC output)
            var renderDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var device in renderDevices)
            {
                AudioDevices.Add(new AudioDeviceInfo(device.ID, $"{device.FriendlyName} (Loopback)", IsLoopback: true));
                OutputDevices.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, IsLoopback: false));
            }

            SelectedDevice = AudioDevices.FirstOrDefault(d => prevInput is not null && d.Id == prevInput.Id && d.IsLoopback == prevInput.IsLoopback);
            SelectedOutputDevice = OutputDevices.FirstOrDefault(d => prevOutput is not null && d.Id == prevOutput.Id);
        }
        catch (Exception ex)
        {
            // デバイス列挙のCOM例外でフラグが立ちっぱなしになると全コールバックが死ぬため必ず復帰させる
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
        RunOnUiThread(() =>
        {
            RawTimecodeDisplay = e.RawTimecode.ToString();
            OffsetTimecodeDisplay = e.OffsetTimecode.ToString();
        });
    }

    private void OnStatusChanged(object? sender, TimecodeStatusChangedEventArgs e)
    {
        RunOnUiThread(() =>
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
            // Restore generator settings
            GeneratorFrameRate = settings.GeneratorSettings.FrameRate;
            GeneratorStartTime = settings.GeneratorSettings.StartTime.ToString();
            OutputVolumeLevel = settings.GeneratorSettings.VolumeLevel;

            // Restore output device selection
            SelectedOutputDevice = OutputDevices.FirstOrDefault(d => d.Id == settings.GeneratorSettings.OutputDeviceId);

            // Restore input device selection
            SelectedDevice = AudioDevices.FirstOrDefault(d => d.Id == settings.DeviceId);

            // Restore freerun duration
            _timecodeEngine.FreerunDurationSeconds = settings.FreerunDurationSeconds;

            // Restore source type
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
        _timecodeEngine.TimecodeUpdated -= OnTimecodeUpdated;
        _timecodeEngine.StatusChanged -= OnStatusChanged;
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
