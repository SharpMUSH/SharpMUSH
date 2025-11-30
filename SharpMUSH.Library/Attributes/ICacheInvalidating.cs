namespace SharpMUSH.Library.Attributes;

/// <summary>
/// Removed by <see cref="SharpMUSH.Library.Attributes.ICacheable"/>
/// Handled by <see cref="SharpMUSH.Library.Behaviors.CacheInvalidationBehavior{TRequest, TResponse}"/>
/// </summary>
public interface ICacheInvalidating
{
	string[] CacheKeys { get; }
	string[] CacheTags { get; }
}