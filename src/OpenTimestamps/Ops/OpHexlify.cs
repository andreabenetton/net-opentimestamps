using System.Globalization;
using System.Text;
using OpenTimestamps.Serialization;

namespace OpenTimestamps.Ops;

/// <summary>
/// Converts the message to lowercase ASCII hex. Halves the maximum input length
/// (so the resulting hex string still fits within <see cref="Op.MaxResultLength"/>).
/// </summary>
public sealed class OpHexlify : UnaryOp
{
    internal const byte OpTag = 0xF3;

    public override byte Tag => OpTag;

    public override string Name => "hexlify";

    public override int MaxMessageLength => MaxResultLength / 2;

    protected override byte[] DoCall(byte[] message)
    {
        if (message.Length == 0)
        {
            throw new OpMessageException("Cannot hexlify an empty message.");
        }

        string hex = Convert.ToHexString(message).ToLower(CultureInfo.InvariantCulture);
        return Encoding.ASCII.GetBytes(hex);
    }
}
