using OpenTimestamps.Serialization;

namespace OpenTimestamps.Ops;

/// <summary>
/// Reverses the message bytes. Retained for backwards compatibility with old proofs;
/// upstream marks this op as pending removal (PendingDeprecationWarning).
/// New stamps should not emit this op.
/// </summary>
public sealed class OpReverse : UnaryOp
{
    internal const byte OpTag = 0xF2;

    public override byte Tag => OpTag;

    public override string Name => "reverse";

    protected override byte[] DoCall(byte[] message)
    {
        if (message.Length == 0)
        {
            throw new OpMessageException("Cannot reverse an empty message.");
        }

        byte[] result = new byte[message.Length];
        for (int i = 0; i < message.Length; i++)
        {
            result[i] = message[message.Length - 1 - i];
        }

        return result;
    }
}
