using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Database.Lightning.Semantics;

/// <summary>What one read section and one whole request may copy out of native storage.</summary>
internal sealed record SemanticReadLimits(int SectionRows, long SectionBytes, int RequestRows, long RequestBytes)
{
	internal static readonly SemanticReadLimits Default = new(256, 256 * 1024, 4096, 4 * 1024 * 1024);
}

/// <summary>Everything a certified answer was read under: one settled package generation, one content generation and one authorization.</summary>
internal sealed record SemanticReadStamp(Guid Fixture, ulong PackageGeneration, ulong ContentGeneration,
	ulong RequirementsVersion, ulong AuthorizationVersion, WikiReader Reader);

/// <summary>
/// How a semantic read ended. Only <see cref="Certified"/> carries an answer; nothing else carries a
/// partial one. <see cref="Unavailable"/> is one value for a denied and for a missing target alike.
/// </summary>
internal abstract record SemanticReadResult<T>
{
	private SemanticReadResult() { }
	internal sealed record Certified(T Value, SemanticReadStamp Stamp) : SemanticReadResult<T>;
	internal sealed record Incomplete(string Reason) : SemanticReadResult<T>;
	internal sealed record Unavailable : SemanticReadResult<T> { internal static readonly Unavailable Value = new(); }
	internal sealed record DataChanged : SemanticReadResult<T> { internal static readonly DataChanged Value = new(); }
	internal sealed record AuthorizationChanged : SemanticReadResult<T> { internal static readonly AuthorizationChanged Value = new(); }
	internal sealed record WorkLimit : SemanticReadResult<T> { internal static readonly WorkLimit Value = new(); }
	internal sealed record Cancelled : SemanticReadResult<T> { internal static readonly Cancelled Value = new(); }
}

/// <summary>What a plan's section decided: keep reading, the target is unavailable, or the answer.</summary>
internal readonly record struct SemanticSectionStep<T>(SemanticSectionKind Kind, T? Value)
{
	internal static SemanticSectionStep<T> Continue => new(SemanticSectionKind.Continue, default);
	internal static SemanticSectionStep<T> Unavailable => new(SemanticSectionKind.Unavailable, default);
	internal static SemanticSectionStep<T> Answer(T value) => new(SemanticSectionKind.Answer, value);
}

internal enum SemanticSectionKind { Continue, Unavailable, Answer }

/// <summary>
/// One bounded native read section. Every row it copies out counts against the section's and the
/// request's limits; the transaction it wraps ends with the section.
/// </summary>
internal sealed class SemanticReadSection
{
	private readonly ITx _tx;
	private readonly SemanticReadLimits _limits;
	private readonly SemanticReadBudget _request;
	private int _rows;
	private long _bytes;

	internal SemanticReadSection(ITx tx, SemanticReadLimits limits, SemanticReadBudget request, WikiReader reader, int index,
		WikiSemanticTables? tables = null)
	{
		_tx = tx;
		_limits = limits;
		_request = request;
		Reader = reader;
		Index = index;
		Tables = tables ?? AtomicWikiLayout.Tables;
	}

	internal WikiReader Reader { get; }
	internal int Index { get; }
	/// <summary>The tables of the generation this read pinned in section 0.</summary>
	internal WikiSemanticTables Tables { get; }

	/// <summary>
	/// The row under <paramref name="key"/>. Its native length is charged before the copy is allocated, so a
	/// row larger than what this section and the request have left is refused unread.
	/// </summary>
	internal bool TryGet(TableDef table, ReadOnlySpan<byte> key, out byte[] value)
	{
		var allowance = Math.Max(0, Math.Min(_limits.SectionBytes - _bytes, _request.RemainingBytes));
		var budget = new NativeReadBudget(1, Math.Max(1, key.Length + allowance), 16 * 1024 * 1024, CancellationToken.None);
		Found<NativeReadValue> read;
		try { read = ((IBoundedReadTx)_tx).ReadValue(table, key, budget); }
		catch (NativeReadLimitException) { throw new SemanticReadWorkLimitException(); }
		if (read is not NativeReadValue found)
		{
			value = [];
			Charge(0);
			return false;
		}
		using (found) value = found.Value;
		Charge(value.Length);
		return true;
	}

	/// <summary>
	/// At most <paramref name="max"/> rows under <paramref name="prefix"/>, from inclusive <paramref name="start"/>
	/// to exclusive <paramref name="end"/> (empty is unbounded), strictly after <paramref name="after"/>, in key
	/// order. Every row counts against this section's and the request's limits.
	/// </summary>
	internal List<NativeReadEntry> Range(TableDef table, ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> start,
		ReadOnlySpan<byte> end, ReadOnlySpan<byte> after, int max)
	{
		var rows = new List<NativeReadEntry>();
		var resume = after.ToArray();
		var reader = (IBoundedReadTx)_tx;
		while (rows.Count < max)
		{
			var budget = new NativeReadBudget(1024, 8 * 1024 * 1024, 16 * 1024 * 1024, CancellationToken.None, maxRecords: NativeReadBudget.MaxBatchRecords);
			using var batch = reader.ReadRange(table, prefix, start, end, resume, budget);
			foreach (var entry in batch.Entries)
			{
				if (rows.Count == max) break;
				Charge(entry.Key.Length + entry.Value.Length);
				rows.Add(entry);
			}
			if (batch.Exhausted || batch.Entries.Length == 0) break;
			resume = batch.Entries[^1].Key;
		}
		return rows;
	}

	private void Charge(long bytes)
	{
		if (++_rows > _limits.SectionRows || (_bytes += bytes) > _limits.SectionBytes) throw new SemanticReadWorkLimitException();
		_request.Charge(bytes);
	}
}

internal sealed class SemanticReadBudget(SemanticReadLimits limits)
{
	private int _rows;
	private long _bytes;

	internal long RemainingBytes => limits.RequestBytes - _bytes;

	internal void Charge(long bytes)
	{
		if (++_rows > limits.RequestRows || (_bytes += bytes) > limits.RequestBytes) throw new SemanticReadWorkLimitException();
	}
}

internal sealed class SemanticReadWorkLimitException() : Exception("A semantic read exceeded its work limit.");

/// <summary>
/// Runs a semantic read on an issued fixture. Section 0 pins the settled package generation, the content
/// generation and the authorization versions; the reader is resolved after it, and again before a final
/// section that rechecks every pin in one snapshot. A plan always runs its declared number of sections,
/// whatever it found, so a denied target and a missing one take the same path.
/// </summary>
internal static class SemanticWikiReader
{
	internal static async Task<SemanticReadResult<T>> ReadAsync<T>(
		LightningDatabase provider,
		Func<CancellationToken, ValueTask<WikiReader>> resolveReader,
		int sections,
		Func<SemanticReadSection, SemanticSectionStep<T>> plan,
		CancellationToken cancellationToken,
		SemanticReadLimits? limits = null,
		Func<int, CancellationToken, ValueTask>? between = null)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(sections, 1);
		limits ??= SemanticReadLimits.Default;
		if (Stopped(cancellationToken)) return SemanticReadResult<T>.Cancelled.Value;
		using var lease = provider.AdmitSemanticRead(cancellationToken);
		var store = provider.Store;
		var identity = lease.Lease.FixtureId;
		var budget = new SemanticReadBudget(limits);
		try
		{
			var pins = store.Read(tx => Pins.Read(tx, identity));
			if (pins is null) return new SemanticReadResult<T>.Incomplete("The world's package state is not settled.");

			var reader = await resolveReader(cancellationToken);
			SemanticSectionStep<T> outcome = SemanticSectionStep<T>.Continue;
			for (var index = 0; index < sections; index++)
			{
				if (Stopped(cancellationToken)) return SemanticReadResult<T>.Cancelled.Value;
				var step = store.Read(tx =>
				{
					if (Pins.Read(tx, identity) is not { } now) return (Changed: (SemanticReadResult<T>?)SemanticReadResult<T>.DataChanged.Value, Step: default(SemanticSectionStep<T>));
					if (pins.Differs<T>(now) is { } changed) return (changed, default);
					// After an answer or an unavailable target the remaining sections still run, unread.
					return (null, outcome.Kind == SemanticSectionKind.Continue
						? plan(new SemanticReadSection(tx, limits, budget, reader, index, AtomicWikiLayout.ForGeneration(pins.Generation)))
						: outcome);
				});
				if (step.Changed is { } difference) return difference;
				outcome = step.Step;
				if (between is not null && index + 1 < sections) await between(index, cancellationToken);
			}

			if (Stopped(cancellationToken)) return SemanticReadResult<T>.Cancelled.Value;
			var final = await resolveReader(cancellationToken);
			if (Stopped(cancellationToken)) return SemanticReadResult<T>.Cancelled.Value;
			if (!SameReader(reader, final)) return SemanticReadResult<T>.AuthorizationChanged.Value;
			var last = store.Read<SemanticReadResult<T>?>(tx => Pins.Read(tx, identity) is { } now ? pins.Differs<T>(now) : SemanticReadResult<T>.DataChanged.Value);
			if (last is not null) return last;
			if (Stopped(cancellationToken)) return SemanticReadResult<T>.Cancelled.Value;

			return outcome.Kind switch
			{
				SemanticSectionKind.Answer => new SemanticReadResult<T>.Certified(outcome.Value!,
					new SemanticReadStamp(identity, pins.Package, pins.Content, pins.Requirements, pins.Authorization, reader)),
				SemanticSectionKind.Unavailable => SemanticReadResult<T>.Unavailable.Value,
				_ => new SemanticReadResult<T>.Incomplete("The plan did not answer.")
			};
		}
		catch (SemanticReadWorkLimitException) { return SemanticReadResult<T>.WorkLimit.Value; }
		catch (OperationCanceledException) when (Stopped(cancellationToken)) { return SemanticReadResult<T>.Cancelled.Value; }
	}

	private static bool Stopped(CancellationToken cancellationToken)
		=> cancellationToken.IsCancellationRequested || ExecutionBudget.Current is { IsExceeded: true };

	private static bool SameReader(WikiReader first, WikiReader second)
		=> string.Equals(first.Dbref, second.Dbref, StringComparison.Ordinal) && first.Scopes.SetEquals(second.Scopes);

	private sealed record Pins(ulong Package, ulong Content, ulong Requirements, ulong Authorization, ulong Generation)
	{
		/// <summary>
		/// The pins in <paramref name="tx"/>'s snapshot, or null when the package state is not settled for this
		/// fixture. The generation names the tables the read uses throughout; before activation it is the first.
		/// </summary>
		internal static Pins? Read(ITx tx, Guid identity)
		{
			if (!tx.TryGet(Tables.Meta, LightningStore.FixturePackageStateKeyBytes, out var marker)
				|| !LightningStore.IsSettledFixturePackageState(marker, identity))
				return null;
			var generation = tx.TryGet(Tables.Meta, AtomicWikiLayout.MarkerKey, out var activation)
				? AtomicWikiLayout.Decode(activation, identity).Generation : 1;
			return new(System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(marker.AsSpan(24)),
				LightningStore.ReadFixtureVersion(tx, FixtureVersionClass.Content),
				LightningStore.ReadFixtureVersion(tx, FixtureVersionClass.Requirements),
				LightningStore.ReadFixtureVersion(tx, FixtureVersionClass.Authorization), generation);
		}

		internal SemanticReadResult<T>? Differs<T>(Pins now)
			=> Package != now.Package || Content != now.Content || Generation != now.Generation ? SemanticReadResult<T>.DataChanged.Value
				: Requirements != now.Requirements || Authorization != now.Authorization ? SemanticReadResult<T>.AuthorizationChanged.Value
				: null;
	}
}
