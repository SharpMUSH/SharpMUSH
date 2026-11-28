using DotNext.Threading;
using SharpMUSH.Library.DiscriminatedUnions;
using System.Text.Json.Serialization;

namespace SharpMUSH.Library.Models;

public class SharpMail
{
	[JsonIgnore] public string? Id { get; set; }
	public required DateTimeOffset DateSent { get; set; }
	public required bool Fresh { get; set; }
	public required bool Read { get; set; }
	public required bool Tagged { get; set; }
	public required bool Urgent { get; set; }
	public required bool Forwarded { get; set; }
	public required bool Cleared { get; set; }
	public required string Folder { get; set; }
	public required MString Content { get; set; }
	public required MString Subject { get; set; }

	/// <summary>
	/// Sent anonymously, as a RhostMUSH game could: the message keeps its sender (who still finds it among
	/// the mail they sent), but reading it answers no sender, so the recipient is never shown who it was.
	/// </summary>
	public bool Anonymous { get; set; }
	[JsonIgnore] public required AsyncLazy<AnyOptionalSharpObject> From { get; set; }

	/// <summary>
	/// The mailbox the message is in. A sender's list of sent mail reads it to say who each message went to; it
	/// is <see langword="null"/> where the store does not say.
	/// </summary>
	[JsonIgnore] public AsyncLazy<AnyOptionalSharpObject>? To { get; set; }

	/// <summary>
	/// The send this copy belongs to. A message sent to several players is one send stored once in each mailbox,
	/// and every copy carries the same id. The store gives a message that arrives without one an id of its own.
	/// </summary>
	public Guid? Message { get; set; }

	/// <summary>
	/// The conversation this message is part of: the <see cref="Message"/> of the send that started it. A reply
	/// carries the thread of the message it answers; a new send starts its own.
	/// </summary>
	public Guid? Thread { get; set; }

	/// <summary>The <see cref="Message"/> this one replies to, or <see langword="null"/> when it is no reply.</summary>
	public Guid? ReplyTo { get; set; }

	/// <summary>
	/// Who the send was addressed to, as its sender named them: <c>#&lt;dbref&gt;</c> for a player and
	/// <c>+&lt;alias&gt;</c> for a mail alias. Empty for mail sent before the store kept it.
	/// </summary>
	public IReadOnlyList<string> Addressed { get; set; } = [];
}