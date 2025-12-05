namespace SharpMUSH.Library.Attributes;

/// <summary>
/// Removed by <see cref="SharpMUSH.Library.Attributes.ICacheInvalidating"/>
/// Handled by <see cref="SharpMUSH.Library.Behaviors.QueryCachingBehavior{TRequest, TResponse}"/>
/// </summary>
public interface ICacheable
{
	string CacheKey { get; }
	string[] CacheTags { get; }
}