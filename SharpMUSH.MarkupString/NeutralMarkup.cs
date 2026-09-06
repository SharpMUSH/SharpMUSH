namespace MarkupString;

/// <summary>
/// A markup layer that carries no styling of its own. Useful as a placeholder marker.
/// </summary>
public sealed class NeutralMarkup : IMarkup
{
	public static readonly NeutralMarkup Instance = new();
}
