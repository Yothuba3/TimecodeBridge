using CommunityToolkit.Mvvm.ComponentModel;

namespace TimecodeBridge.macOS.ViewModels;

/// <summary>
/// ダイアログ内の送信先ホスト選択用の項目
/// </summary>
public class HostSelection : ObservableObject
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
