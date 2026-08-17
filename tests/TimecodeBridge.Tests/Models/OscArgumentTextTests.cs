namespace TimecodeBridge.Tests.Models;

using TimecodeBridge.Models;

public class OscArgumentTextTests
{
    [Fact]
    public void Format_EmptyList_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, OscArgumentText.Format([]));
    }

    [Fact]
    public void Parse_EmptyOrWhitespace_ReturnsEmptyList()
    {
        Assert.Empty(OscArgumentText.Parse(""));
        Assert.Empty(OscArgumentText.Parse("   "));
    }

    [Fact]
    public void RoundTrip_MixedTypes_Preserved()
    {
        List<OscArgument> args =
        [
            new OscInt32Argument(42),
            new OscFloat32Argument(0.5f),
            new OscStringArgument("go"),
        ];

        var text = OscArgumentText.Format(args);
        var parsed = OscArgumentText.Parse(text);

        Assert.Equal(3, parsed.Count);
        Assert.Equal(42, Assert.IsType<OscInt32Argument>(parsed[0]).Value);
        Assert.Equal(0.5f, Assert.IsType<OscFloat32Argument>(parsed[1]).Value);
        Assert.Equal("go", Assert.IsType<OscStringArgument>(parsed[2]).Value);
    }

    [Fact]
    public void Parse_InvalidTokens_AreIgnored()
    {
        // garbage: コロンなし / x:9: 不明な型 / f:abc: 数値変換失敗 → すべて無視
        var parsed = OscArgumentText.Parse("i:1 garbage x:9 f:abc s:ok");

        Assert.Equal(2, parsed.Count);
        Assert.Equal(1, Assert.IsType<OscInt32Argument>(parsed[0]).Value);
        Assert.Equal("ok", Assert.IsType<OscStringArgument>(parsed[1]).Value);
    }

    [Fact]
    public void Parse_String_PreservesValue()
    {
        var parsed = OscArgumentText.Parse("s:hello");
        Assert.Equal("hello", Assert.IsType<OscStringArgument>(parsed[0]).Value);
    }

    [Fact]
    public void RoundTrip_StringWithSpaces_Preserved()
    {
        List<OscArgument> args = [new OscStringArgument("hello world"), new OscInt32Argument(1)];

        var text = OscArgumentText.Format(args);
        var parsed = OscArgumentText.Parse(text);

        Assert.Equal(2, parsed.Count);
        Assert.Equal("hello world", Assert.IsType<OscStringArgument>(parsed[0]).Value);
        Assert.Equal(1, Assert.IsType<OscInt32Argument>(parsed[1]).Value);
    }

    [Fact]
    public void RoundTrip_StringWithQuotesAndBackslash_Preserved()
    {
        List<OscArgument> args = [new OscStringArgument("say \"hi\" C:\\path")];

        var parsed = OscArgumentText.Parse(OscArgumentText.Format(args));

        Assert.Equal("say \"hi\" C:\\path", Assert.IsType<OscStringArgument>(parsed[0]).Value);
    }

    [Fact]
    public void RoundTrip_EmptyString_Preserved()
    {
        List<OscArgument> args = [new OscStringArgument("")];

        var text = OscArgumentText.Format(args);
        Assert.Equal("s:\"\"", text);

        var parsed = OscArgumentText.Parse(text);
        Assert.Equal("", Assert.IsType<OscStringArgument>(parsed[0]).Value);
    }

    [Fact]
    public void Parse_QuotedString_AllowsSpaces()
    {
        var parsed = OscArgumentText.Parse("s:\"a b c\" i:2");

        Assert.Equal(2, parsed.Count);
        Assert.Equal("a b c", Assert.IsType<OscStringArgument>(parsed[0]).Value);
        Assert.Equal(2, Assert.IsType<OscInt32Argument>(parsed[1]).Value);
    }

    // --- TryParse（検証付き。不正トークンを黙って捨てない） ---

    [Fact]
    public void TryParse_ValidInput_ReturnsArgs()
    {
        Assert.True(OscArgumentText.TryParse("i:1 f:0.5 s:go", out var args, out var invalid));
        Assert.Null(invalid);
        Assert.Equal(3, args.Count);
    }

    [Fact]
    public void TryParse_Empty_ReturnsTrueWithNoArgs()
    {
        Assert.True(OscArgumentText.TryParse("", out var args, out _));
        Assert.Empty(args);
    }

    [Fact]
    public void TryParse_MissingTypePrefix_ReportsToken()
    {
        Assert.False(OscArgumentText.TryParse("i:1 42", out _, out var invalid));
        Assert.Equal("42", invalid);
    }

    [Fact]
    public void TryParse_UnknownType_ReportsToken()
    {
        Assert.False(OscArgumentText.TryParse("x:9", out _, out var invalid));
        Assert.Equal("x:9", invalid);
    }

    [Fact]
    public void TryParse_BadFloatValue_ReportsToken()
    {
        Assert.False(OscArgumentText.TryParse("f:abc", out _, out var invalid));
        Assert.Equal("f:abc", invalid);
    }

    [Fact]
    public void TryParse_FullWidthInput_NormalizedAndAccepted()
    {
        // IMEの全角入力（ｉ：１ ｆ：０．５）も受け付ける
        Assert.True(OscArgumentText.TryParse("ｉ：１ ｆ：０．５", out var args, out _));
        Assert.Equal(2, args.Count);
        Assert.Equal(1, Assert.IsType<OscInt32Argument>(args[0]).Value);
        Assert.Equal(0.5f, Assert.IsType<OscFloat32Argument>(args[1]).Value);
    }

    [Fact]
    public void TryParse_ColonWithSpaceAfter_ReportsToken()
    {
        // "i: 1" は値が空になるためエラー（黙って捨てない）
        Assert.False(OscArgumentText.TryParse("i: 1", out _, out var invalid));
        Assert.Equal("i:", invalid);
    }
}
