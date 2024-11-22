namespace SharpMUSH.Library.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class CacheableQueryAttribute(int seconds = 300) : Attribute
{
    public TimeSpan? Duration { get; } = TimeSpan.FromSeconds(seconds);
}