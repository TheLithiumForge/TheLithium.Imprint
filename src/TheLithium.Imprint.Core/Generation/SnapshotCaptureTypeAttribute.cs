namespace TheLithium.Imprint.Generation;

/// <summary>
/// Marks a type that has its own capture method. The generator rejects it in <c>AssertSnapshot</c> with IMP002,
/// because a generated writer would serialize its properties instead of its content.
/// </summary>
/// <param name="assertion">The method to call instead, e.g. <c>AssertImage</c>.</param>
/// <remarks>Matched by name in the generator. Keep the full name stable.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
internal sealed class SnapshotCaptureTypeAttribute(string assertion) : Attribute
{
    public string Assertion { get; } = assertion;
}
