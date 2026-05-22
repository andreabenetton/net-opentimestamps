using OpenTimestamps.Serialization;

namespace OpenTimestamps.Ops;

/// <summary>
/// An operation parameterised by an inline byte-string argument written immediately after the op tag.
/// </summary>
public abstract class BinaryOp : Op
{
    private readonly byte[] _argument;

    protected BinaryOp(byte[] argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.Length == 0)
        {
            throw new ArgumentException($"{GetType().Name} argument must be non-empty.", nameof(argument));
        }

        if (argument.Length > MaxResultLength)
        {
            throw new ArgumentException(
                $"{GetType().Name} argument too long: {argument.Length} > {MaxResultLength}.",
                nameof(argument));
        }

        _argument = argument;
    }

    /// <summary>The bytes appended/prepended/etc. by this operation.</summary>
    public ReadOnlySpan<byte> Argument => _argument;

    /// <summary>A copy of the argument bytes.</summary>
    public byte[] ArgumentArray() => (byte[])_argument.Clone();

    protected override ReadOnlySpan<byte> ArgumentBytes => _argument;

    public override void Serialize(OtsWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        base.Serialize(writer);
        writer.WriteVarBytes(_argument);
    }

    public override string ToString() => $"{Name} {Convert.ToHexString(_argument).ToLowerInvariant()}";
}
