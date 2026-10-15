using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Plugins;

/// <summary>
/// Hears every feed line once it has been delivered and its taps queued, for C# code that cannot be a
/// softcode tap (a bridge to another service). A plugin registers an implementation as a singleton from
/// <see cref="IServiceRegistrar.RegisterServices"/>; the feed service calls each one in turn.
/// </summary>
/// <remarks>
/// The call is made while the line's sender waits, so an implementation only hands the line on (to a queue
/// of its own) and returns. One that throws is logged and the line goes on to the next.
/// </remarks>
public interface IFeedLineListener
{
	/// <summary>A line was sent.</summary>
	ValueTask LineSentAsync(FeedLine line, CancellationToken cancellationToken);
}

/// <summary>A line as a listener hears it.</summary>
/// <param name="Message">The line, as stored.</param>
/// <param name="Recipients">Who it was delivered to.</param>
/// <param name="Stored">Whether it was stored.</param>
/// <param name="Origin">Where it came from when that was outside the game (<c>discord/&lt;message id&gt;</c>), or empty.</param>
public sealed record FeedLine(SharpFeedMessage Message, IReadOnlyList<DBRef> Recipients, bool Stored, string Origin);
