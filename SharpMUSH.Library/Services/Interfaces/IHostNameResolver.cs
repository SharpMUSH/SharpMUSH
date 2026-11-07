namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Looks up the host name of a connection's address, as PennMUSH's <c>info_slave</c> does when <c>use_dns</c> is on.
/// </summary>
public interface IHostNameResolver
{
	/// <summary>The name <paramref name="address"/> resolves to, or null when it has none or the lookup fails.</summary>
	ValueTask<string?> ResolveAsync(string address, CancellationToken cancellationToken = default);
}
