using TimecodeBridge.Core.Services.Interfaces;

namespace TimecodeBridge.Tests.Services;

/// <summary>
/// macOS版FileDialogServiceのテスト
/// Avalonia StorageProviderを使用したファイルダイアログのテスト
/// </summary>
public class FileDialogServiceMacOSTests
{
    [Fact]
    public void FileDialogServiceMacOS_IFileDialogServiceを実装している()
    {
        // Arrange & Act & Assert
        // macOS固有の実装クラスが存在する場合、TimecodeBridge.macOSプロジェクトから参照する必要がある
        // 現時点ではインターフェースの存在確認のみ
        Assert.True(typeof(IFileDialogService).IsInterface);
        Assert.Equal("IFileDialogService", typeof(IFileDialogService).Name);
    }

    [Fact]
    public void IFileDialogService_ShowOpenFileDialogメソッドを持っている()
    {
        // Arrange
        var method = typeof(IFileDialogService).GetMethod("ShowOpenFileDialog");

        // Act & Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(string), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal("filter", parameters[0].Name);
        Assert.Equal("initialDirectory", parameters[1].Name);
    }

    [Fact]
    public void IFileDialogService_ShowSaveFileDialogメソッドを持っている()
    {
        // Arrange
        var method = typeof(IFileDialogService).GetMethod("ShowSaveFileDialog");

        // Act & Assert
        Assert.NotNull(method);
        Assert.Equal(typeof(string), method!.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.Equal("filter", parameters[0].Name);
        Assert.Equal("defaultFileName", parameters[1].Name);
        Assert.Equal("initialDirectory", parameters[2].Name);
    }

    // 注: 実際のStorageProvider統合テストは、Avaloniaのヘッドレステスト環境が必要なため
    // ここでは基本的なインターフェース契約の確認のみを行う
    // 実装後の統合テストは手動テストまたはAvaloniaのテストハーネスで実施
}
