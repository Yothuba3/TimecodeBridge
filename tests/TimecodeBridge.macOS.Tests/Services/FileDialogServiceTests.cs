using TimecodeBridge.Core.Services.Interfaces;
using TimecodeBridge.macOS.Services;

namespace TimecodeBridge.macOS.Tests.Services;

/// <summary>
/// macOS版FileDialogServiceの実装テスト
/// </summary>
public class FileDialogServiceTests
{
    [Fact]
    public void FileDialogService_IFileDialogServiceを実装している()
    {
        // Arrange
        var service = new FileDialogService();

        // Act & Assert
        Assert.IsAssignableFrom<IFileDialogService>(service);
    }

    [Fact]
    public void FileDialogService_コンストラクタで例外を投げない()
    {
        // Act & Assert
        var exception = Record.Exception(() => new FileDialogService());
        Assert.Null(exception);
    }

    [Fact]
    public void ShowOpenFileDialog_nullフィルタで例外を投げない()
    {
        // Arrange
        var service = new FileDialogService();

        // Act & Assert
        // UIスレッドが存在しない場合はnullを返すべき（ヘッドレス環境）
        var exception = Record.Exception(() => service.ShowOpenFileDialog(null!));
        Assert.Null(exception);
    }

    [Fact]
    public void ShowSaveFileDialog_nullフィルタで例外を投げない()
    {
        // Arrange
        var service = new FileDialogService();

        // Act & Assert
        // UIスレッドが存在しない場合はnullを返すべき（ヘッドレス環境）
        var exception = Record.Exception(() => service.ShowSaveFileDialog(null!));
        Assert.Null(exception);
    }

    // 注: 実際のUI統合テストは、Avalonia.Headlessを使用した統合テストまたは
    // 手動テストで実施する必要がある。ここでは基本的な契約と例外処理のみをテスト。
}
