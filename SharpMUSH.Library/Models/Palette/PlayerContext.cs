namespace SharpMUSH.Library.Models.Palette;

/// <summary>
/// One context on a character's stack: what the player is doing or what is happening to them, as a
/// name other code agrees on (<c>scene_here</c>, <c>combat</c>, a package's name after its command ran).
/// </summary>
/// <param name="Name">The context's name, lower case. Names are global; the Guide lists the conventions.</param>
/// <param name="Values">What the context knows, filling an entry's slots of the same names. Never evaluated.</param>
/// <param name="PushedAt">When it was last pushed; its weight is measured from here.</param>
/// <param name="Lifetime">How long it lives at most, or null to live until it sinks.</param>
/// <param name="Info">A line of help for whoever reads a suggestion it raised, as given; never evaluated.</param>
public sealed record PlayerContext(
	string Name,
	IReadOnlyDictionary<string, string> Values,
	DateTimeOffset PushedAt,
	TimeSpan? Lifetime,
	string? Info);

/// <summary>A context as read from a stack: the context and its weight at the moment of reading.</summary>
/// <param name="Weight">1 when just pushed, halving every <see cref="ContextWeights.HalfLife"/>.</param>
public sealed record WeightedContext(PlayerContext Context, double Weight)
{
	public string Name => Context.Name;
}

/// <summary>How a context sinks.</summary>
public static class ContextWeights
{
	/// <summary>A context's weight halves every this long since it was pushed.</summary>
	public static readonly TimeSpan HalfLife = TimeSpan.FromMinutes(15);

	/// <summary>Below this weight (a little over an hour) a context falls off the stack.</summary>
	public const double Floor = 0.05;

	/// <summary>How many contexts a stack holds; a push onto a full one drops the bottom.</summary>
	public const int Depth = 20;

	/// <summary>The longest lifetime a push may give, and the longest a name or value may be.</summary>
	public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(1);

	public const int MaxNameLength = 64;
	public const int MaxValues = 16;
	public const int MaxValueLength = 256;
	public const int MaxInfoLength = 512;

	/// <summary>The weight of a context pushed at <paramref name="pushedAt"/>, read at <paramref name="now"/>.</summary>
	public static double At(DateTimeOffset pushedAt, DateTimeOffset now)
		=> Math.Pow(0.5, Math.Max(0, (now - pushedAt).TotalSeconds) / HalfLife.TotalSeconds);
}
