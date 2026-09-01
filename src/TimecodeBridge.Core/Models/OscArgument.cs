namespace TimecodeBridge.Core.Models;

public enum OscArgumentType
{
    Int32,
    Float32,
    String,
}

public abstract record OscArgument
{
    public abstract OscArgumentType Type { get; }
}

public sealed record OscInt32Argument(int Value) : OscArgument
{
    public override OscArgumentType Type => OscArgumentType.Int32;
}

public sealed record OscFloat32Argument(float Value) : OscArgument
{
    public override OscArgumentType Type => OscArgumentType.Float32;
}

public sealed record OscStringArgument(string Value) : OscArgument
{
    public override OscArgumentType Type => OscArgumentType.String;
}
