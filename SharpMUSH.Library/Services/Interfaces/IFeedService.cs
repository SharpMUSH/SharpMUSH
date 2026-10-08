using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The rules of feeds (<c>help @feed</c>) that <c>@feed</c> and the feed functions share: who may run a kind,
/// its locks, and sending a line through its tie-in attributes and taps.
/// </summary>
public interface IFeedService
{
	/// <summary>The name of a kind's tie-in attribute on its owner: <c>FEED`RADIO`ROUTE</c>.</summary>
	static string TieIn(string kind, string part) => $"FEED`{kind.ToUpperInvariant()}`{part}";

	/// <summary>The kind, or an error naming it for the person who asked.</summary>
	ValueTask<Result<SharpFeedKind>> GetKindAsync(string kind);

	/// <summary>
	/// Whether <paramref name="executor"/> may run <paramref name="kind"/>'s feeds: it controls the kind's owner,
	/// or holds <c>feed.admin</c>.
	/// </summary>
	ValueTask<bool> CanRunAsync(AnySharpObject executor, SharpFeedKind kind);

	/// <summary>
	/// Whether <paramref name="unlocker"/> passes both the kind's and the feed's lock of one type
	/// (<c>read</c> or <c>send</c>), each evaluated against the kind's owner. An empty lock passes.
	/// </summary>
	ValueTask<bool> PassesAsync(SharpFeedKind kind, SharpFeed feed, string lockType, AnySharpObject unlocker);

	/// <summary>
	/// Sends a line: stores it within the feed's limits (when it is logged), routes it, delivers it through the
	/// kind's <c>DELIVER</c> or <c>FORMAT</c> attribute (or the default line), then queues every tap. Refused
	/// when it is longer than the feed's <c>max_length</c>.
	/// </summary>
	ValueTask<Result<FeedDelivery>> SendAsync(IMUSHCodeParser parser, FeedSend send);
}

/// <summary>A line to send.</summary>
/// <param name="Kind">The feed's kind.</param>
/// <param name="Feed">The feed, or <see cref="SharpFeed.New"/> for one with no members or lines yet.</param>
/// <param name="Speaker">Who the line is attributed to: the enactor of <c>@feed/send</c>.</param>
/// <param name="Executor">The object whose code sent it.</param>
/// <param name="Style">One of <see cref="FeedStyles.All"/>.</param>
/// <param name="Text">The message as written.</param>
/// <param name="To">The <c>/to</c> list, which <c>ROUTE</c> gets as <c>%6</c>; empty when none was given.</param>
public sealed record FeedSend(
	SharpFeedKind Kind,
	SharpFeed Feed,
	AnySharpObject Speaker,
	AnySharpObject Executor,
	string Style,
	MString Text,
	IReadOnlyList<DBRef> To);

/// <summary>What became of a line.</summary>
/// <param name="Id">Its id.</param>
/// <param name="Recipients">Who it was delivered to (for <c>DELIVER</c>, who it was handed to deliver to).</param>
/// <param name="Stored">Whether it was stored.</param>
public sealed record FeedDelivery(long Id, IReadOnlyList<DBRef> Recipients, bool Stored);
