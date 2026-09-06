namespace MarkupString;

/// <summary>
/// What an emitter is told about the run it is emitting. <see cref="Previous"/> and
/// <see cref="Next"/> are the markup sets of the immediately adjacent runs, so a stateful emitter
/// (SGR, say) can diff against them; either is <see langword="null"/> when a plain-text gap or the
/// end of the text sits there instead.
/// </summary>
public readonly ref struct EmitContext
{
	/// <summary>The format being rendered.</summary>
	public MarkupFormat Format { get; init; }

	/// <summary>The registry the render is running against — how a set emitter delegates layers it does not own.</summary>
	public MarkupRegistry Registry { get; init; }

	/// <summary>The previous run's markups, or <see langword="null"/> when a gap or the start of the text precedes this run.</summary>
	public MarkupSet? Previous { get; init; }

	/// <summary>The next run's markups, or <see langword="null"/> when a gap or the end of the text follows this run.</summary>
	public MarkupSet? Next { get; init; }

	/// <summary>Whether this is the first run of the text.</summary>
	public bool IsFirstRun { get; init; }

	/// <summary>Whether this is the last run of the text.</summary>
	public bool IsLastRun { get; init; }
}
