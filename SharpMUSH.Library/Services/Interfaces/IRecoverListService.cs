using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// The game's rules over the recover list (<see cref="IRecoverListStore"/>): who sees an entry and who may bring it
/// back, what a recovery falls back on when what the object pointed at is gone, how long entries stay, and the
/// audit records for each change.
/// </summary>
public interface IRecoverListService
{
	/// <summary>
	/// How long an entry of <paramref name="type"/> stays before the age pass purges it: <c>recover_keep_players</c>
	/// for a character, <c>recover_keep_objects</c> otherwise. Null keeps it forever, as does a setting that does not
	/// read as one, so a typo keeps more rather than less.
	/// </summary>
	TimeSpan? RetentionFor(string type);

	/// <summary>Whether <paramref name="viewer"/> sees every entry, rather than only those for objects it owned.</summary>
	ValueTask<bool> MaySeeAllAsync(AnySharpObject viewer);

	/// <summary>The entries <paramref name="viewer"/> may see, by number.</summary>
	IAsyncEnumerable<RecoverEntry> VisibleAsync(AnySharpObject viewer, CancellationToken ct = default);

	/// <summary>
	/// Whoever could have @undestroyed the object may recover, keep or unkeep its entry: its former owner, someone
	/// who controls that owner, or a wizard once the owner is gone too.
	/// </summary>
	ValueTask<bool> MayRecoverAsync(AnySharpObject executor, RecoverEntry entry);

	/// <summary>What recovering <paramref name="entry"/> as <paramref name="recoverer"/> would put back, and each fallback.</summary>
	ValueTask<RecoverPlan> PlanAsync(RecoverEntry entry, AnySharpObject recoverer, CancellationToken ct = default);

	/// <summary>
	/// Puts the entry back under its own objid with the relations <see cref="PlanAsync"/> settles on, HALTed, and
	/// records it in the audit log. Does not check <see cref="MayRecoverAsync"/>.
	/// </summary>
	ValueTask<Result<RecoverOutcome>> RecoverAsync(AnySharpObject executor, RecoverEntry entry, CancellationToken ct = default);

	/// <summary>Marks the entry kept with <paramref name="reason"/>, or clears the mark when it is null.</summary>
	ValueTask<bool> SetKeepAsync(AnySharpObject executor, RecoverEntry entry, string? reason, CancellationToken ct = default);

	/// <summary>
	/// Whether the age pass and every bulk purge leave the entry alone: it carries a keep mark, or a package record
	/// names it, so a package rollback can still find it.
	/// </summary>
	ValueTask<bool> IsKeptAsync(RecoverEntry entry, CancellationToken ct = default);

	/// <summary>
	/// Removes the entry for good and lets everything still naming its number by number alone let go of it
	/// (<see cref="IFreedObjectCleanup"/>). <paramref name="actor"/> is null for the age pass. An entry kept by the time
	/// the removal is written is left alone unless <paramref name="evenIfKept"/>.
	/// </summary>
	ValueTask<bool> PurgeAsync(AnySharpObject? actor, RecoverEntry entry, bool evenIfKept = false, CancellationToken ct = default);

	/// <summary>Purges every entry that is not kept and has outlived <see cref="RetentionFor"/> as of <paramref name="now"/>.</summary>
	ValueTask<int> ExpireAsync(DateTimeOffset now, CancellationToken ct = default);
}

/// <summary>
/// What a recovery would do: the relations it puts back, a line for each fallback it took and for what it does not
/// undo, or why it cannot happen at all.
/// </summary>
public sealed record RecoverPlan(RecoverRestore Restore, IReadOnlyList<string> Notes, string? Refusal);

/// <summary>A recovered object and the notes its plan carried.</summary>
public sealed record RecoverOutcome(DBRef Objid, IReadOnlyList<string> Notes);
