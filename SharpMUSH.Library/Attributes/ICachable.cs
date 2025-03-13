namespace SharpMUSH.Library.Attributes;

public interface ICacheable
{
	string CacheKey { get; init; }
	string[] CacheTags { get; init; }
}