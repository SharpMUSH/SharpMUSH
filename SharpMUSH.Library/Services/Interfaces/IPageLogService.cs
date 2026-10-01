using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The engine's side of the page log (the <c>page_log</c> and <c>page_log_retention_days</c> options, a
/// SharpMUSH extension; PennMUSH keeps none).
/// </summary>
/// <remarks>
/// A page is delivered first and logged after: <see cref="PageAsync"/> gives it its id, the terminal and the
/// <c>PAGE`MESSAGE</c> event carry it, and only then does <see cref="RecordAsync"/> write it. A write that is
/// slow or fails never holds up or loses a delivery.
/// </remarks>
public interface IPageLogService
{
	/// <summary>Whether <c>page_log</c> is on now.</summary>
	bool Enabled { get; }

	/// <summary>
	/// A delivered page, with its id from the sequence channel lines take theirs from. Writes nothing: the
	/// id is what the <c>PAGE`MESSAGE</c> event passes on, so the pushed <c>comm.message</c> and the logged
	/// copy are known to be one.
	/// </summary>
	/// <param name="recipients">The players the page reached: never one who refused it.</param>
	ValueTask<SharpPage> PageAsync(AnySharpObject sender, string senderName, IReadOnlyList<AnySharpObject> recipients,
		string style, string message, CancellationToken cancellationToken = default);

	/// <summary>
	/// While <c>page_log</c> is on, keeps <paramref name="page"/> for each of <paramref name="participants"/>
	/// that is a player, as their own copy. Never throws: a failed write is logged, since the page was
	/// already delivered.
	/// </summary>
	ValueTask RecordAsync(SharpPage page, IReadOnlyList<AnySharpObject> participants,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Deletes the logged pages older than <c>page_log_retention_days</c>, and returns how many copies went;
	/// nothing when it is -1.
	/// </summary>
	ValueTask<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
}
