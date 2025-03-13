namespace SharpMUSH.Library.Attributes;

public interface ICacheable
{
	string CacheKey { get; }
	string[] CacheTags { get; }
}