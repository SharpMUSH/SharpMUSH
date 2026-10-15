using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Builds attribute indexes in the background, a batch of objects per write, so a build never holds the writer for
/// long and resumes where it stopped after a restart (the entry keeps the point it reached).
/// </summary>
public interface IAttributeIndexBuilder
{
	/// <summary>Objects one batch looks at, in one write.</summary>
	const int BatchSize = 5_000;

	/// <summary>
	/// Starts building the index on <paramref name="name"/>, or joins the build already running. The build goes on
	/// whether or not the caller waits for it.
	/// </summary>
	AttributeIndexBuild Build(string name);

	/// <summary>Starts every index the table holds that is still building, as at startup.</summary>
	ValueTask ResumeAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>A running index build: how far it has come, and when it ends.</summary>
public sealed class AttributeIndexBuild(string name)
{
	private long _scanned;
	private long _indexed;
	private long _total;

	/// <summary>The attribute, upper case.</summary>
	public string Name { get; } = name;

	/// <summary>Objects looked at so far by this run (a resumed build counts from where it resumed).</summary>
	public long Scanned => Interlocked.Read(ref _scanned);

	/// <summary>Copies listed so far by this run.</summary>
	public long Indexed => Interlocked.Read(ref _indexed);

	/// <summary>Objects the whole build looks at, as the last batch counted them.</summary>
	public long Total => Interlocked.Read(ref _total);

	/// <summary>
	/// Ends with the last batch's step when the index is ready, or not found when the index was taken off (or its
	/// entry deleted) while it built.
	/// </summary>
	public required Task<Found<AttributeIndexBuildStep>> Completion { get; init; }

	/// <summary>Set when a caller joined the build: the loop takes one more batch before it ends.</summary>
	internal bool Again { get; set; }

	internal void Add(AttributeIndexBuildStep step)
	{
		Interlocked.Add(ref _scanned, step.Scanned);
		Interlocked.Add(ref _indexed, step.Indexed);
		Interlocked.Exchange(ref _total, step.Total);
	}
}
