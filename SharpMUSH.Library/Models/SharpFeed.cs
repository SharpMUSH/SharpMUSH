namespace SharpMUSH.Library.Models;

/// <summary>
/// A feed's limits and defaults. On a <see cref="SharpFeedKind"/> a null is the engine default
/// (<see cref="Defaults"/>); on a <see cref="SharpFeed"/> a null is its kind's value.
/// </summary>
/// <param name="MaxMessages">Lines kept; the oldest goes first. 0 keeps every line.</param>
/// <param name="MaxBytes">Total size of the lines kept, in bytes of stored text; the oldest goes first. 0 is no limit.</param>
/// <param name="MaxLength">The longest message accepted, in characters; a longer one is refused, not cut. 0 is no limit.</param>
/// <param name="MaxAge">Lines older than this go when the feed is next written to. Null on <see cref="Defaults"/> is no limit.</param>
/// <param name="Logged">Whether lines are stored at all. A line that is not stored is still delivered and tapped.</param>
/// <param name="Style">The style a line has when the sender names none: <c>say</c>, <c>pose</c>, <c>semipose</c>, <c>emit</c> or <c>announce</c>.</param>
public sealed record FeedSettings(
	int? MaxMessages = null,
	long? MaxBytes = null,
	int? MaxLength = null,
	TimeSpan? MaxAge = null,
	bool? Logged = null,
	string? Style = null)
{
	/// <summary>What a kind that sets nothing has.</summary>
	public static readonly FeedSettings Defaults = new(500, 0, 0, null, true, "say");

	public static readonly FeedSettings None = new();

	/// <summary>These settings, with each one left unset taken from <paramref name="fallback"/>.</summary>
	public FeedSettings Over(FeedSettings fallback) => new(
		MaxMessages ?? fallback.MaxMessages,
		MaxBytes ?? fallback.MaxBytes,
		MaxLength ?? fallback.MaxLength,
		MaxAge ?? fallback.MaxAge,
		Logged ?? fallback.Logged,
		Style ?? fallback.Style);
}

/// <summary>
/// A kind of feed: the system that owns it and what its feeds share. Players never see a kind; its owner's
/// softcode is the only thing that sends into it or changes its members.
/// </summary>
/// <param name="Name">The kind's name, lower case: <c>radio</c>.</param>
/// <param name="Owner">The object whose code runs the kind, by objid. Its <c>FEED`&lt;KIND&gt;`ROUTE</c>,
/// <c>`DELIVER</c> and <c>`FORMAT</c> attributes are the kind's tie-ins.</param>
/// <param name="Description">What it is for, for listings.</param>
/// <param name="Settings">The kind's limits and defaults; unset ones are <see cref="FeedSettings.Defaults"/>.</param>
/// <param name="Locks">
/// Its locks by name (<see cref="FeedLocks"/>): <c>read</c> decides who may be joined to its feeds and
/// <c>send</c> who may speak on them. A lock that is not set passes everyone.
/// </param>
public sealed record SharpFeedKind(
	string Name,
	DBRef Owner,
	string Description,
	FeedSettings Settings,
	IReadOnlyDictionary<string, string> Locks)
{
	public FeedSettings Effective => Settings.Over(FeedSettings.Defaults);

	public string Lock(string name) => Locks.GetValueOrDefault(name, "");
}

/// <summary>
/// One feed: a stream of lines of a kind, <c>&lt;kind&gt;/&lt;key&gt;</c>. A feed starts on its first line or
/// member; this is what it holds besides them.
/// </summary>
/// <param name="Kind">Its kind's name.</param>
/// <param name="Key">Which stream within the kind, lower case: <c>101.5</c>.</param>
/// <param name="Settings">Overrides of the kind's settings; unset ones are the kind's.</param>
/// <param name="Locks">Its locks by name; each applies on top of the kind's lock of the same name.</param>
/// <param name="Messages">How many lines it holds.</param>
/// <param name="Bytes">Their stored size.</param>
/// <param name="LastId">The newest line's id, or 0.</param>
public sealed record SharpFeed(
	string Kind,
	string Key,
	FeedSettings Settings,
	IReadOnlyDictionary<string, string> Locks,
	int Messages,
	long Bytes,
	long LastId)
{
	public string Name => $"{Kind}/{Key}";

	public string Lock(string name) => Locks.GetValueOrDefault(name, "");

	public static SharpFeed New(string kind, string key) => new(kind, key, FeedSettings.None, FeedLocks.None, 0, 0, 0);
}

/// <summary>A member of a feed and where they stand in it.</summary>
/// <param name="Member">The member, by objid.</param>
/// <param name="JoinedAt">The newest line's id when they last joined, so recall can start after it.</param>
/// <param name="Gag">Still a member, but receives nothing.</param>
/// <param name="LastSeen">The newest line they have read, for unread counts, or 0.</param>
public sealed record SharpFeedMember(
	DBRef Member,
	long JoinedAt,
	bool Gag,
	long LastSeen);

/// <summary>
/// One line on a feed, as stored: facts only. How it looks is decided for each reader when it is shown.
/// Every object is kept with its name as it was, so a line still reads whole after the object is destroyed.
/// </summary>
/// <param name="Id">From the same sequence as channel lines and pages, so it rises with time.</param>
/// <param name="Kind">The feed's kind.</param>
/// <param name="Key">The feed's key.</param>
/// <param name="At">When it was sent.</param>
/// <param name="Speaker">Who it is attributed to, by objid.</param>
/// <param name="SpeakerName">Their name when it was sent.</param>
/// <param name="Executor">The object whose code sent it, by objid.</param>
/// <param name="ExecutorName">Its name when it was sent.</param>
/// <param name="Location">The speaker's location when it was sent, by objid, or null.</param>
/// <param name="LocationName">The location's name when it was sent, or empty.</param>
/// <param name="Style"><c>say</c>, <c>pose</c>, <c>semipose</c>, <c>emit</c> or <c>announce</c>.</param>
/// <param name="Text">The message as written, markup kept.</param>
/// <param name="DisplayName">The name the speaker chose to appear under for this line (a persona, a callsign), or empty.</param>
public sealed record SharpFeedMessage(
	long Id,
	string Kind,
	string Key,
	DateTimeOffset At,
	DBRef Speaker,
	string SpeakerName,
	DBRef Executor,
	string ExecutorName,
	DBRef? Location,
	string LocationName,
	string Style,
	MString Text,
	string DisplayName = "")
{
	public string Feed => $"{Kind}/{Key}";
}

/// <summary>An attribute run for every line of a kind (or of every kind, <c>*</c>).</summary>
/// <param name="Kind">The kind tapped, or <c>*</c>.</param>
/// <param name="Object">The object holding the attribute, by objid.</param>
/// <param name="Attribute">The attribute's name, upper case.</param>
public sealed record SharpFeedTap(string Kind, DBRef Object, string Attribute);

/// <summary>A feed's two locks. Any other rule about who may do what belongs to the system that runs the kind.</summary>
public static class FeedLocks
{
	/// <summary>Checked against an object being joined.</summary>
	public const string Read = "read";

	/// <summary>Checked against the speaker of a line.</summary>
	public const string Send = "send";

	public static readonly IReadOnlyDictionary<string, string> None = new Dictionary<string, string>();

	public static bool IsName(string name) => name is Read or Send;

	/// <summary><paramref name="locks"/> with <paramref name="name"/> set, or removed when <paramref name="value"/> is empty.</summary>
	public static IReadOnlyDictionary<string, string> With(IReadOnlyDictionary<string, string> locks, string name, string value)
	{
		var changed = new Dictionary<string, string>(locks);
		if (value.Length == 0) changed.Remove(name);
		else changed[name] = value;
		return changed;
	}
}

/// <summary>The styles a feed line can have, and the names a feed's settings accept for them.</summary>
public static class FeedStyles
{
	public const string Say = "say";
	public const string Pose = "pose";
	public const string SemiPose = "semipose";
	public const string Emit = "emit";
	public const string Announce = "announce";

	public static readonly IReadOnlyList<string> All = [Say, Pose, SemiPose, Emit, Announce];
}
