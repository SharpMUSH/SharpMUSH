using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// @mail conversations: the stored copies of the messages that share a <see cref="SharpMail.Thread"/>.
/// </summary>
public interface IMailThreadStore
{
	/// <summary>
	/// The copies in <paramref name="thread"/> that <paramref name="reader"/> sent or holds, in the order they were
	/// stored. A copy someone else sent to someone else is never returned.
	/// </summary>
	IAsyncEnumerable<MailThreadCopy> GetMailThreadAsync(SharpObject reader, Guid thread,
		CancellationToken cancellationToken = default);
}
