using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The engine's side of the page log (the <c>page_log</c> and <c>page_log_retention_days</c> options, a
/// SharpMUSH extension; PennMUSH keeps none).
/// </summary>
public interface IPageLogService
{
	/// <summary>Whether <c>page_log</c> is on now.</summary>
	bool Enabled { get; }

	/// <summary>
	/// Gives a delivered page its id, from the sequence channel lines take theirs from, and, while
	/// <c>page_log</c> is on, keeps it for the sender (when a player) and for each of
	/// <paramref name="recipients"/> as their own copy. The page is returned either way: its id is what the
	/// <c>PAGE`MESSAGE</c> event passes on, so the pushed <c>comm.message</c> and the logged copy are known to
	/// be one.
	/// </summary>
	/// <param name="recipients">The players the page reached: never one who refused it.</param>
	ValueTask<SharpPage> DeliveredAsync(AnySharpObject sender, string senderName, IReadOnlyList<AnySharpObject> recipients,
		string style, string message, CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes the logged pages older than <c>page_log_retention_days</c>, and returns how many copies went;
	/// nothing when it is -1.
	/// </summary>
	ValueTask<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
}
