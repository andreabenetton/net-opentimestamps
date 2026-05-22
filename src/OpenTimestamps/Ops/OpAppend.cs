namespace OpenTimestamps.Ops;

/// <summary>
/// Append a fixed suffix to the message.
/// </summary>
public sealed class OpAppend : BinaryOp
{
    internal const byte OpTag = 0xF0;

    public OpAppend(byte[] argument) : base(argument)
    {
    }

    public override byte Tag => OpTag;

    public override string Name => "append";

    protected override byte[] DoCall(byte[] message)
    {
        byte[] result = new byte[message.Length + Argument.Length];
        message.AsSpan().CopyTo(result);
        Argument.CopyTo(result.AsSpan(message.Length));
        return result;
    }
}
