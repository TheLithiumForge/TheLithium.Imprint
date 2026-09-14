namespace Xunit;

internal sealed class FactAttribute : Attribute
{
    public string? DisplayName
    {
        get; set;
    }
}
