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
}
