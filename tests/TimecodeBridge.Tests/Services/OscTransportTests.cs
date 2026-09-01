namespace TimecodeBridge.Tests.Services;

using TimecodeBridge.Models;
using TimecodeBridge.Services;

public class OscTransportTests
{
    [Fact]
    public void EncodeMessage_NoArguments_AddressAndEmptyTypeTag()
    {
        var bytes = OscTransport.EncodeMessage("/ping", []);

        // "/ping" + 3 nulls (8バイト境界) + "," + 3 nulls
        Assert.Equal(
            "/ping\0\0\0,\0\0\0"u8.ToArray(),
            bytes);
    }

    [Fact]
    public void EncodeMessage_MultipleArguments_SingleMessageWithAllArgs()
    {
        var bytes = OscTransport.EncodeMessage("/a",
        [
            new OscInt32Argument(1),
            new OscFloat32Argument(0.5f),
            new OscStringArgument("hi"),
        ]);

        var expected = new List<byte>();
        expected.AddRange("/a\0\0"u8.ToArray());       // address padded to 4
        expected.AddRange(",ifs\0\0\0\0"u8.ToArray()); // type tags ",ifs" + null + pad to 8
        expected.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x01 }); // int32 1 (big endian)
        expected.AddRange(new byte[] { 0x3F, 0x00, 0x00, 0x00 }); // float32 0.5 (big endian)
        expected.AddRange("hi\0\0"u8.ToArray());       // string padded to 4

        Assert.Equal(expected.ToArray(), bytes);
    }

    [Fact]
    public void EncodeMessage_StringLengthMultipleOf4_StillNullTerminated()
    {
        var bytes = OscTransport.EncodeMessage("/abc", [new OscStringArgument("test")]);

        // "/abc" は4バイトちょうどでも null 終端のため4バイト追加パディング
        Assert.Equal(
            "/abc\0\0\0\0,s\0\0test\0\0\0\0"u8.ToArray(),
            bytes);
    }
}
