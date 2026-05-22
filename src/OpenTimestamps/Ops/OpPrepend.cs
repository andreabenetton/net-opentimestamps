namespace OpenTimestamps.Ops;

/// <summary>
/// Prepend a fixed prefix to the message.
/// </summary>
public sealed class OpPrepend : BinaryOp
{
    internal const byte OpTag = 0xF1;

    public OpPrepend(byte[] argument) : base(argument)
    {
    }

    public override byte Tag => OpTag;

    public override string Name => "prepend";

    protected override byte[] DoCall(byte[] message)
    {
        byte[] result = new byte[Argument.Length + message.Length];
        Argument.CopyTo(result);
        message.AsSpan().CopyTo(result.AsSpan(Argument.Length));
        return result;
    }
}
