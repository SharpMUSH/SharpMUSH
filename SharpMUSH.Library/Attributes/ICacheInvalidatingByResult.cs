namespace SharpMUSH.Library.Attributes;

/// <summary>
/// A write whose invalidation set is only known from its result: a create allocates the dbref it
/// must clear. <see cref="SharpMUSH.Library.Behaviors.CacheInvalidationBehavior{TRequest, TResponse}"/>
/// removes these keys after the handler returns, on the same terms as <see cref="ICacheInvalidating.CacheKeys"/>.
/// </summary>
public interface ICacheInvalidatingByResult<in TResponse>
{
	string[] CacheKeysFor(TResponse result);
}
