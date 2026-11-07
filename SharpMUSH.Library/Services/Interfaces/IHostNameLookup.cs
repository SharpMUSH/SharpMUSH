namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Gives a connection the host name its address resolves to while <c>use_dns</c> is on, as PennMUSH's
/// <c>info_slave</c> does. It runs beside the connection; a login records whatever host the connection has by then,
/// as PennMUSH's <c>check_last</c> does, and never waits for it. One lookup per connection: a second caller waits for
/// the one already running.
/// </summary>
public interface IHostNameLookup
{
	/// <summary>
	/// Completes once connection <paramref name="handle"/>'s host has been looked up, or at once when there is nothing
	/// to look up (<c>use_dns</c> off, a host the socket already named, a lookup already done).
	/// </summary>
	Task LookUpAsync(long handle);
}
