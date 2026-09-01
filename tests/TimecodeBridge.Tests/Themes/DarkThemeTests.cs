using TimecodeBridge.Core.Models;
using TimecodeBridge.Core.Services;
using TimecodeBridge.Core.Services.Interfaces;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace TimecodeBridge.Tests.Themes;

public class DarkThemeTests
{
    private readonly ResourceDictionary _theme;

    public DarkThemeTests()
    {
        // pack:// URI scheme requires the application to be initialized
        if (Application.Current == null)
        {
            _ = new Application();
        }

        _theme = new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/TimecodeBridge;component/Themes/DarkTheme.xaml",
                UriKind.Absolute)
        };
    }

    [StaFact]
    public void DarkTheme_CanBeLoaded()
    {
        Assert.NotNull(_theme);
        Assert.True(_theme.Count > 0);
    }

    [StaFact]
    public void DarkTheme_ContainsBackgroundColorResources()
    {
        Assert.True(_theme.Contains("PrimaryBackgroundBrush"));
        Assert.True(_theme.Contains("SecondaryBackgroundBrush"));
        Assert.True(_theme.Contains("TertiaryBackgroundBrush"));

        Assert.IsType<SolidColorBrush>(_theme["PrimaryBackgroundBrush"]);
        Assert.IsType<SolidColorBrush>(_theme["SecondaryBackgroundBrush"]);
        Assert.IsType<SolidColorBrush>(_theme["TertiaryBackgroundBrush"]);
    }

    [StaFact]
    public void DarkTheme_ContainsForegroundColorResources()
    {
        Assert.True(_theme.Contains("PrimaryForegroundBrush"));
        Assert.True(_theme.Contains("SecondaryForegroundBrush"));

        Assert.IsType<SolidColorBrush>(_theme["PrimaryForegroundBrush"]);
        Assert.IsType<SolidColorBrush>(_theme["SecondaryForegroundBrush"]);
    }

    [StaFact]
    public void DarkTheme_ContainsAccentColorResources()
    {
        Assert.True(_theme.Contains("AccentBrush"));
        Assert.True(_theme.Contains("AccentHoverBrush"));

        Assert.IsType<SolidColorBrush>(_theme["AccentBrush"]);
    }

    [StaFact]
    public void DarkTheme_ContainsErrorColorResource()
    {
        Assert.True(_theme.Contains("ErrorBrush"));
        Assert.IsType<SolidColorBrush>(_theme["ErrorBrush"]);
    }

    [StaFact]
    public void DarkTheme_ContainsBorderBrushResource()
    {
        Assert.True(_theme.Contains("BorderBrush"));
        Assert.IsType<SolidColorBrush>(_theme["BorderBrush"]);
    }

    [StaFact]
    public void DarkTheme_BackgroundColors_AreDark()
    {
        var primaryBg = (SolidColorBrush)_theme["PrimaryBackgroundBrush"]!;
        // Dark theme: background luminance should be low (< 0.3)
        var color = primaryBg.Color;
        var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        Assert.True(luminance < 0.3, $"Primary background luminance {luminance} is not dark enough");
    }

    [StaFact]
    public void DarkTheme_ForegroundColors_AreLight()
    {
        var primaryFg = (SolidColorBrush)_theme["PrimaryForegroundBrush"]!;
        var color = primaryFg.Color;
        var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        Assert.True(luminance > 0.7, $"Primary foreground luminance {luminance} is not light enough");
    }

    [StaFact]
    public void DarkTheme_ContainsButtonStyle()
    {
        Assert.True(_theme.Contains(typeof(System.Windows.Controls.Button)));
    }

    [StaFact]
    public void DarkTheme_ContainsTextBoxStyle()
    {
        Assert.True(_theme.Contains(typeof(System.Windows.Controls.TextBox)));
    }

    [StaFact]
    public void DarkTheme_ContainsListViewStyle()
    {
        Assert.True(_theme.Contains(typeof(System.Windows.Controls.ListView)));
    }

    [StaFact]
    public void DarkTheme_ContainsCheckBoxStyle()
    {
        Assert.True(_theme.Contains(typeof(System.Windows.Controls.CheckBox)));
    }
}
