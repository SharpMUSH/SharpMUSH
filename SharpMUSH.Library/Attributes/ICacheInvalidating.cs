namespace SharpMUSH.Library.Attributes;

public interface ICacheInvalidating
{
	string[] CacheKeys { get; }
	string[] CacheTags { get; }
}