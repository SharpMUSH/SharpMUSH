using System.Text;

namespace SharpMUSH.Plugins.Discord.Outbox;

/// <summary>A feed line on its way to one Discord channel, already rendered as Discord text.</summary>
/// <param name="At">When the game sent it; the outbox drops it once it is older than <see cref="DiscordOutboxOptions.MaxAge"/>.</param>
public sealed record OutboundLine(string Speaker, string? AvatarUrl, string Text, DateTimeOffset At);

/// <summary>One webhook post: one or more lines of one speaker.</summary>
/// <param name="Lines">How many queued lines it carries, so a dropped post is counted as the lines it held.</param>
public sealed record DiscordPost(string Speaker, string? AvatarUrl, string Content, int Lines);

public enum DiscordLinkState
{
	Ok,
	/// <summary>More lines are waiting than one post carries; Discord is slowing the link down.</summary>
	Backlogged,
	/// <summary>Stopped until an admin acts; see <see cref="DiscordOutbox.Fault"/>.</summary>
	Faulted,
}

/// <param name="MaxLines">The most lines that wait; past it the oldest are dropped.</param>
/// <param name="MaxAge">The oldest a line may be when it goes out; a flood of stale chat is worse than a gap.</param>
/// <param name="MaxContent">The most characters in one post. Discord takes 2000.</param>
/// <param name="MaxParts">The most posts one long line becomes before the rest is cut.</param>
/// <param name="MaxAttempts">How often a post is tried before it is dropped.</param>
/// <param name="BackoffBase">The first retry waits up to this long, and each further one up to twice the last.</param>
/// <param name="BackoffCap">The longest wait between tries.</param>
/// <param name="Gather">How long the sender waits after a quiet spell, to join a burst into one post.</param>
/// <param name="BacklogLines">Above this many waiting lines the link reports itself backlogged.</param>
/// <param name="RejectionsBeforeFault">This many refused messages within a minute mean a bug, and fault the link.</param>
/// <param name="NoticeSpeaker">The name the "not relayed" notice is posted under.</param>
public sealed record DiscordOutboxOptions(
	int MaxLines = 200,
	TimeSpan? MaxAge = null,
	int MaxContent = 1900,
	int MaxParts = 3,
	int MaxAttempts = 5,
	TimeSpan? BackoffBase = null,
	TimeSpan? BackoffCap = null,
	TimeSpan? Gather = null,
	int BacklogLines = 5,
	int RejectionsBeforeFault = 5,
	string NoticeSpeaker = "Game")
{
	public TimeSpan LineMaxAge => MaxAge ?? TimeSpan.FromSeconds(60);
	public TimeSpan FirstBackoff => BackoffBase ?? TimeSpan.FromSeconds(1);
	public TimeSpan LongestBackoff => BackoffCap ?? TimeSpan.FromSeconds(30);
	public TimeSpan GatherWindow => Gather ?? TimeSpan.FromMilliseconds(750);
}

/// <summary>
/// What one linked channel sends next, and what it does with Discord's answer. One post is in flight at a time,
/// so lines arrive in the order they were said. The caller serializes access.
/// </summary>
/// <remarks>
/// Discord.Net waits out rate limits itself; the outbox handles everything else. It bounds the queue by count and
/// by age, joins a burst into as few posts as it can, retries transient failures with full-jitter back-off, and
/// stops on a failure only an admin can fix. Lines it had to drop are owed as one notice line.
/// </remarks>
public sealed class DiscordOutbox(DiscordOutboxOptions options, Random random)
{
	private const string Truncated = "… (truncated)";

	private readonly LinkedList<OutboundLine> _queue = new();
	private readonly Queue<DateTimeOffset> _rejections = new();
	private DiscordPost? _inFlight;
	private bool _inFlightIsNotice;
	private int _attempts;
	private int _notRelayed;

	/// <summary>How many dropped lines the notice in flight reports.</summary>
	private int _noticed;

	public LinkBroken? Fault { get; private set; }

	public int Depth => _queue.Count;

	public DiscordLinkState State => Fault is not null
		? DiscordLinkState.Faulted
		: _queue.Count > options.BacklogLines ? DiscordLinkState.Backlogged : DiscordLinkState.Ok;

	/// <summary>Lines dropped since the last notice went out.</summary>
	public int NotRelayed => _notRelayed;

	public void Enqueue(OutboundLine line)
	{
		_queue.AddLast(line);
		Trim(line.At);
	}

	/// <summary>The post to send now: the one that failed last, the notice of dropped lines, or the next lines.</summary>
	public DiscordPost? Next(DateTimeOffset now)
	{
		if (Fault is not null)
		{
			return null;
		}

		if (_inFlight is not null)
		{
			return _inFlight;
		}

		Trim(now);
		_inFlightIsNotice = _notRelayed > 0;
		_inFlight = _inFlightIsNotice ? Notice() : Gather();
		return _inFlight;
	}

	/// <summary>Takes Discord's answer to <see cref="Next"/>'s post.</summary>
	/// <returns>How long to wait before the next post.</returns>
	public TimeSpan Report(PostOutcome outcome, DateTimeOffset now)
	{
		switch (outcome)
		{
			case Posted:
				Done();
				return TimeSpan.Zero;
			case Transient when ++_attempts < options.MaxAttempts:
				return Backoff(_attempts);
			case Transient:
				Drop();
				return TimeSpan.Zero;
			case Rejected:
				Drop();
				Rejection(now);
				return TimeSpan.Zero;
			case LinkBroken broken:
				Fault = broken;
				return TimeSpan.Zero;
			default:
				return TimeSpan.Zero;
		}
	}

	/// <summary>Stops the link as if a post had failed with <paramref name="broken"/>: a link that was faulted when the server stopped.</summary>
	public void Halt(LinkBroken broken) => Fault = broken;

	/// <summary>An admin fixed the link. The post that failed is tried again, if it is not too old by now.</summary>
	public void Resume()
	{
		Fault = null;
		_attempts = 0;
		_rejections.Clear();
	}

	/// <summary>
	/// Splits text longer than <paramref name="max"/> at the last space before each cut, into at most
	/// <paramref name="maxParts"/> parts; the last part is cut short and marked when text remains.
	/// </summary>
	public static IReadOnlyList<string> Split(string text, int max, int maxParts)
	{
		List<string> parts = [];
		var rest = text;
		while (rest.Length > max && parts.Count < maxParts - 1)
		{
			var cut = rest.LastIndexOf(' ', max);
			if (cut <= 0)
			{
				cut = max;
			}

			parts.Add(rest[..cut].TrimEnd());
			rest = rest[cut..].TrimStart();
		}

		parts.Add(rest.Length > max ? rest[..(max - Truncated.Length)].TrimEnd() + Truncated : rest);
		return parts;
	}

	private void Trim(DateTimeOffset now)
	{
		while (_queue.Count > 0 && (_queue.Count > options.MaxLines || now - _queue.First!.Value.At > options.LineMaxAge))
		{
			_queue.RemoveFirst();
			_notRelayed++;
		}
	}

	private DiscordPost Notice()
	{
		_noticed = _notRelayed;
		return new DiscordPost(options.NoticeSpeaker, null,
			_noticed == 1 ? "[1 line was not relayed.]" : $"[{_noticed} lines were not relayed.]", 0);
	}

	private DiscordPost? Gather()
	{
		if (_queue.First is not { Value: var first })
		{
			return null;
		}

		_queue.RemoveFirst();
		if (first.Text.Length > options.MaxContent)
		{
			// The parts go back in front, so they leave before any later line, each as a line of its own.
			var parts = Split(first.Text, options.MaxContent, options.MaxParts);
			foreach (var part in parts.Skip(1).Reverse())
			{
				_queue.AddFirst(first with { Text = part });
			}

			return new DiscordPost(first.Speaker, first.AvatarUrl, parts[0], 1);
		}

		var content = new StringBuilder(first.Text);
		var lines = 1;
		while (_queue.First is { Value: var next }
			&& next.Speaker == first.Speaker && next.AvatarUrl == first.AvatarUrl
			&& content.Length + 1 + next.Text.Length <= options.MaxContent)
		{
			content.Append('\n').Append(next.Text);
			lines++;
			_queue.RemoveFirst();
		}

		return new DiscordPost(first.Speaker, first.AvatarUrl, content.ToString(), lines);
	}

	/// <summary>A notice that went out settles the count; lines dropped while it was in flight are owed again.</summary>
	private void Done()
	{
		if (_inFlightIsNotice)
		{
			_notRelayed -= _noticed;
		}

		_inFlight = null;
		_attempts = 0;
	}

	/// <summary>A dropped notice stays owed; dropped lines are added to it.</summary>
	private void Drop()
	{
		if (_inFlight is { } post && !_inFlightIsNotice)
		{
			_notRelayed += post.Lines;
		}

		_inFlight = null;
		_attempts = 0;
	}

	private void Rejection(DateTimeOffset now)
	{
		_rejections.Enqueue(now);
		while (_rejections.Count > 0 && now - _rejections.Peek() > TimeSpan.FromMinutes(1))
		{
			_rejections.Dequeue();
		}

		if (_rejections.Count >= options.RejectionsBeforeFault)
		{
			Fault = new LinkBroken(0, $"Discord refused {_rejections.Count} messages within a minute.");
		}
	}

	/// <summary>Full jitter: anywhere from nothing to the doubled base, so many links don't retry in step.</summary>
	private TimeSpan Backoff(int attempt)
	{
		var ceiling = Math.Min(options.LongestBackoff.TotalMilliseconds, options.FirstBackoff.TotalMilliseconds * Math.Pow(2, attempt - 1));
		return TimeSpan.FromMilliseconds(random.NextDouble() * ceiling);
	}
}
