using Microsoft.Extensions.Configuration;

namespace SharpMUSH.SocketServer.Configuration;

/// <summary>
/// Browser replay retention and read budgets. This is the purpose-built archive a reconnect replays
/// from; it is configured apart from the message bus's transport retention (<c>NatsOptions.MaxAge</c>),
/// which only has to cover a consumer being briefly away.
/// </summary>
public sealed record ReplayOptions
{
	/// <summary>How long a session's frames stay replayable (<c>Replay:RetentionHours</c>). Resume tokens live as long.</summary>
	public TimeSpan Retention { get; init; } = TimeSpan.FromHours(24);

	/// <summary>
	/// Total bytes the replay stream may hold (<c>Replay:MaxBytes</c>). When full, the oldest frames go
	/// first; a reconnect that needs one of them is told its history is incomplete.
	/// </summary>
	public long MaxBytes { get; init; } = 2L * 1024 * 1024 * 1024;

	/// <summary>
	/// The most frames one reconnect may replay (<c>Replay:MaxFrames</c>); a longer history starts a
	/// fresh session instead. 0 removes the limit — memory stays bounded by <see cref="PageSize"/>
	/// either way; this bounds the time and bandwidth one reconnect can take.
	/// </summary>
	public int MaxFrames { get; init; } = 10_000;

	/// <summary>Frames held in memory at once while a replay is sent.</summary>
	public int PageSize { get; init; } = 64;

	public void Validate()
	{
		if (Retention <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(nameof(Retention), Retention, "Replay retention must be positive.");
		if (MaxBytes <= 0)
			throw new ArgumentOutOfRangeException(nameof(MaxBytes), MaxBytes, "The replay stream needs a positive byte budget.");
		if (MaxFrames < 0)
			throw new ArgumentOutOfRangeException(nameof(MaxFrames), MaxFrames, "Use a positive frame budget, or 0 for none.");
		if (PageSize < 1)
			throw new ArgumentOutOfRangeException(nameof(PageSize), PageSize, "A replay page holds at least one frame.");
	}

	public static ReplayOptions FromConfiguration(IConfiguration configuration)
	{
		var defaults = new ReplayOptions();
		var options = new ReplayOptions
		{
			Retention = TimeSpan.FromHours(configuration.GetValue("Replay:RetentionHours", defaults.Retention.TotalHours)),
			MaxBytes = configuration.GetValue("Replay:MaxBytes", defaults.MaxBytes),
			MaxFrames = configuration.GetValue("Replay:MaxFrames", defaults.MaxFrames),
		};
		options.Validate();
		return options;
	}
}
