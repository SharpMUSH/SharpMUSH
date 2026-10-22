using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Each connection's byte counts. Only the connection server sees the bytes, so they are asked for when
/// wanted rather than kept in step.
/// </summary>
public interface IConnectionTrafficService
{
	/// <summary>
	/// The counts of every connection, by handle. A connection missing from the answer has no counts to
	/// show: the connection server did not answer in time, or does not know the handle.
	/// </summary>
	ValueTask<IReadOnlyDictionary<long, ConnectionTraffic>> GetAsync(CancellationToken cancellationToken = default);

	/// <summary>Hands an answer to the request <paramref name="requestId"/>, if it is still waiting.</summary>
	void Complete(Guid requestId, IReadOnlyList<ConnectionTraffic> connections);
}
