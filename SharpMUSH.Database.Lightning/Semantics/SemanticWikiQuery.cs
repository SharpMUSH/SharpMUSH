using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.Models.Wiki.Semantics;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Wiki.Semantics;
using SharpMUSH.Database.Lightning.Records;

namespace SharpMUSH.Database.Lightning.Semantics;

/// <summary>One predicate of a semantic query; every one names a registered schema property.</summary>
internal abstract record SemanticPredicate(string Property)
{
	internal sealed record PageEquals(string Property, string TargetPageId) : SemanticPredicate(Property);
	internal sealed record KeywordEquals(string Property, string Keyword) : SemanticPredicate(Property);
	internal sealed record BooleanEquals(string Property, bool Value) : SemanticPredicate(Property);
	internal sealed record IntegerEquals(string Property, long Value) : SemanticPredicate(Property);
	internal sealed record IntegerRange(string Property, long Low, long High) : SemanticPredicate(Property);
}

/// <summary>
/// A supported query: one explicit collection, a conjunction of indexed predicates, requested scalar
/// columns and a page size. Results are ordered by subject for an equality driver, and by the driving
/// integer property then subject for a range driver. <c>OrderBy</c> names an integer property whose range
/// predicate must then drive.
/// </summary>
internal sealed record SemanticWikiQuery(string Collection, IReadOnlyList<SemanticPredicate> Predicates,
	IReadOnlyList<string> Columns, int PageSize, string Locale = "", string? OrderBy = null);

/// <summary>A named, versioned query definition, evaluated at read time like any other query.</summary>
internal sealed record SemanticListDefinition(string Name, int Version, SemanticWikiQuery Query);

/// <summary>The named generated lists; a definition changes only with a new version.</summary>
internal static class SemanticLists
{
	internal static readonly SemanticListDefinition ArcaneByLevel = new("arcane-by-level", 1,
		new("game", [new SemanticPredicate.IntegerRange("pf2e:level", 1, 10), new SemanticPredicate.KeywordEquals("pf2e:tradition", "arcane")],
			["pf2e:level", "pf2e:tradition"], 7, OrderBy: "pf2e:level"));
}

/// <summary>One authorized row: the page, its title, and each requested column's visible values.</summary>
internal sealed record SemanticQueryRow(string PageId, string Title, IReadOnlyDictionary<string, IReadOnlyList<string>> Columns);

/// <summary>The work one query may do before it must ask to be refined; exceeding either is never a partial answer.</summary>
internal sealed record SemanticQueryBudgets(int CandidateProbes, int DeniedSkips)
{
	internal static readonly SemanticQueryBudgets Default = new(4096, 8192);
}

/// <summary>How a semantic query ended. No outcome reports a count or a partial page.</summary>
internal abstract record SemanticQueryResult
{
	private SemanticQueryResult() { }
	/// <summary>Rows in order; a continuation only when one more authorized row was seen.</summary>
	internal sealed record Page(IReadOnlyList<SemanticQueryRow> Rows, string? Continuation, SemanticReadStamp Stamp) : SemanticQueryResult;
	/// <summary>The query's work budget ran out before the page filled; it names the budget, not the data.</summary>
	internal sealed record RefineRequired(string Budget) : SemanticQueryResult;
	/// <summary>The continuation does not belong to this query, reader, world state or lease; start over.</summary>
	internal sealed record Restart : SemanticQueryResult { internal static readonly Restart Value = new(); }
	/// <summary>An unsupported shape or bound, refused before any read.</summary>
	internal sealed record Rejected(string Reason) : SemanticQueryResult;
	/// <summary>Any non-certified read outcome: incomplete, unavailable, changed, limited or cancelled.</summary>
	internal sealed record NotCertified(object Outcome) : SemanticQueryResult;
}

/// <summary>
/// Runs supported semantic queries over an issued fixture's projection through <see cref="SemanticWikiReader"/>:
/// seeks only postings of visibility classes the reader may see, rechecks every candidate's summary, probes the
/// remaining predicates by forward rows, and never falls back to scanning bodies.
/// </summary>
internal static class SemanticWikiQueries
{
	internal const int MaxPredicates = 4;
	internal const int MaxColumns = 8;
	internal const int MaxPageSize = 50;
	internal const long MaxRangeSpan = 1L << 31;
	internal const int MaxQueryBytes = 4096;
	private const int Sections = 3;
	private const int TokenPayloadBytes = 192;
	private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(10);
	private static readonly SemanticReadLimits Limits = new(16_384, 8 * 1024 * 1024, 32_768, 16 * 1024 * 1024);

	internal static async Task<SemanticQueryResult> RunAsync(LightningDatabase provider,
		Func<CancellationToken, ValueTask<WikiReader>> resolveReader, SemanticWikiQuery query, string? continuation,
		CancellationToken cancellationToken, TimeProvider? clock = null, SemanticQueryBudgets? budgets = null,
		Func<int, CancellationToken, ValueTask>? between = null)
	{
		clock ??= TimeProvider.System;
		budgets ??= SemanticQueryBudgets.Default;
		if (Validate(query) is { } rejection) return new SemanticQueryResult.Rejected(rejection);
		var digest = QueryDigest(query);
		Cursor? cursor = null;
		var lease = provider.SemanticFixtureLeaseForQueries;
		if (continuation is not null)
		{
			cursor = OpenToken(lease, continuation, digest, clock);
			if (cursor is null) return SemanticQueryResult.Restart.Value;
		}

		var run = new Run(query, cursor, budgets);
		var read = await SemanticWikiReader.ReadAsync(provider, resolveReader, Sections, run.Section, cancellationToken, Limits, between);
		if (run.Restart) return SemanticQueryResult.Restart.Value;
		if (run.Refine is { } budget) return new SemanticQueryResult.RefineRequired(budget);
		if (read is not SemanticReadResult<Answer>.Certified certified) return new SemanticQueryResult.NotCertified(read);
		var stamp = certified.Stamp;
		if (cursor is not null && (!cursor.Reader.AsSpan().SequenceEqual(ReaderHash(stamp.Reader)) || !cursor.Matches(stamp)
			|| !cursor.Partitions.AsSpan().SequenceEqual(run.PartitionDigest) || cursor.Locale != LocaleHash(query.Locale)))
			return SemanticQueryResult.Restart.Value;
		var answer = certified.Value;
		string? next = answer.Next is { } position
			? SealToken(lease, new Cursor(digest, ReaderHash(stamp.Reader), stamp.PackageGeneration, stamp.ContentGeneration,
				stamp.RequirementsVersion, stamp.AuthorizationVersion, run.PartitionDigest, position.Order, position.Subject,
				clock.GetUtcNow().Add(TokenLifetime).UtcTicks, LocaleHash(query.Locale)))
			: null;
		return new SemanticQueryResult.Page(answer.Rows, next, stamp);
	}

	// ── Validation: every unsupported shape is refused in full before any read ──

	internal static string? Validate(SemanticWikiQuery query)
	{
		if (string.IsNullOrWhiteSpace(query.Collection)) return "A query names one collection.";
		if (query.PageSize is < 1 or > MaxPageSize) return $"A page holds 1 to {MaxPageSize} rows.";
		if (query.Predicates.Count is < 1 or > MaxPredicates) return $"A query has 1 to {MaxPredicates} predicates.";
		if (query.Columns.Count > MaxColumns) return $"A query requests at most {MaxColumns} columns.";
		if (query.OrderBy is { } order && !query.Predicates.Any(p => p is SemanticPredicate.IntegerRange && p.Property == order))
			return $"Ordering by {order} needs a range on {order}.";
		if (Encoding.UTF8.GetByteCount(QueryText(query)) > MaxQueryBytes) return "The query is too large.";
		foreach (var column in query.Columns)
			if (Property(column) is null) return $"Unknown property {column}.";
		foreach (var predicate in query.Predicates)
		{
			if (Property(predicate.Property) is not { } definition) return $"Unknown property {predicate.Property}.";
			var fits = (predicate, definition.Type) switch
			{
				(SemanticPredicate.PageEquals, SemanticType.PageRef) => true,
				(SemanticPredicate.KeywordEquals keyword, SemanticType.Keyword) => CanonicalFacts.TryKeyword(new(keyword.Keyword), out _),
				(SemanticPredicate.BooleanEquals, SemanticType.Boolean) => true,
				(SemanticPredicate.IntegerEquals, SemanticType.Integer) => true,
				(SemanticPredicate.IntegerRange range, SemanticType.Integer) => range.Low <= range.High && (decimal)range.High - range.Low <= MaxRangeSpan,
				_ => false
			};
			if (!fits) return $"{predicate.Property} does not support that predicate.";
		}
		return null;
	}

	private static PropertyDefinition? Property(string id) => AtomicWikiLayout.Schema.Properties.FirstOrDefault(p => p.Id == id);

	/// <summary>The driving predicate's index: the most selective index by a fixed heuristic; never shown to readers.</summary>
	private static int Driver(SemanticWikiQuery query) => query.OrderBy is { } order
		? Enumerable.Range(0, query.Predicates.Count).First(i => query.Predicates[i] is SemanticPredicate.IntegerRange && query.Predicates[i].Property == order)
		: Enumerable.Range(0, query.Predicates.Count)
		.OrderBy(i => query.Predicates[i] switch
		{
			SemanticPredicate.PageEquals => 0,
			SemanticPredicate.KeywordEquals => 1,
			SemanticPredicate.IntegerEquals or SemanticPredicate.BooleanEquals => 2,
			_ => 3
		}).ThenBy(i => i).First();

	private static string QueryText(SemanticWikiQuery query)
		=> $"{query.Collection}\n{string.Join("\n", query.Predicates)}\n{string.Join(",", query.Columns)}\n{query.PageSize}\n{query.Locale}\n{query.OrderBy}";

	private static byte[] QueryDigest(SemanticWikiQuery query) => SHA256.HashData(Encoding.UTF8.GetBytes(QueryText(query)));

	private static byte[] ReaderHash(WikiReader reader)
		=> SHA256.HashData(Encoding.UTF8.GetBytes((reader.Dbref ?? "") + "\n" + string.Join("\n",
			reader.Scopes.Select(s => s.ToLowerInvariant()).Order(StringComparer.Ordinal))));

	private static ulong LocaleHash(string locale) => BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(locale)));

	// ── Execution: three sections, always all three ──

	private sealed record Position(byte[] Order, ulong Subject);
	private sealed record Answer(IReadOnlyList<SemanticQueryRow> Rows, Position? Next);
	private sealed record Candidate(ulong Subject, byte[] Order, string PageId, string Title);

	private sealed class Run(SemanticWikiQuery query, Cursor? cursor, SemanticQueryBudgets budgets)
	{
		internal bool Restart { get; private set; }
		internal string? Refine { get; private set; }
		internal byte[] PartitionDigest { get; private set; } = [];
		private Resolved? _resolved;
		private List<Candidate>? _candidates;

		internal SemanticSectionStep<Answer> Section(SemanticReadSection section) => section.Index switch
		{
			0 => Resolve(section),
			1 => Seek(section),
			_ => Project(section)
		};

		private SemanticSectionStep<Answer> Resolve(SemanticReadSection section)
		{
			var collection = RegistryId(section, WikiSemanticRegistryKind.Collection, query.Collection);
			var requirements = Requirements(section);
			var admitted = new List<ulong>();
			if (collection != 0)
				foreach (var row in section.Range(AtomicWikiLayout.Catalog, Id(collection), [], [], [], (int)WikiSemanticVisibilityCatalog.MaximumClasses))
				{
					var classId = BinaryPrimitives.ReadUInt64BigEndian(row.Key.AsSpan(8));
					var descriptor = WikiSemanticStateCodec.DescriptorValue(WikiSemanticStateCodec.Catalog(row.Value).Descriptor);
					if (MaySee(section.Reader, requirements, descriptor)) admitted.Add(classId);
				}
			using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			foreach (var classId in admitted)
			{
				section.TryGet(AtomicWikiLayout.Partitions, Pair(collection, classId), out var version);
				hash.AppendData(Id(classId));
				hash.AppendData(version);
			}
			PartitionDigest = hash.GetHashAndReset();
			if (cursor is not null && !cursor.Partitions.AsSpan().SequenceEqual(PartitionDigest))
			{
				Restart = true;
				return SemanticSectionStep<Answer>.Unavailable;
			}

			var predicates = new List<ResolvedPredicate>();
			foreach (var predicate in query.Predicates)
			{
				var property = RegistryId(section, WikiSemanticRegistryKind.Property, predicate.Property);
				if (property == 0) return Empty(collection, requirements);
				switch (predicate)
				{
					case SemanticPredicate.PageEquals page:
						var target = RegistryId(section, WikiSemanticRegistryKind.Page, page.TargetPageId);
						// A target the reader cannot see, and one that does not exist, end the same way.
						if (target == 0 || Visible(section, requirements, target) is null) return SemanticSectionStep<Answer>.Unavailable;
						predicates.Add(new(predicate, property, WikiSemanticScalarTag.PageRef, Id(target), Id(target)));
						break;
					case SemanticPredicate.KeywordEquals keyword:
						CanonicalFacts.TryKeyword(new(keyword.Keyword), out var normalized);
						var keywordId = RegistryId(section, WikiSemanticRegistryKind.Keyword, normalized.Value);
						if (keywordId == 0) return Empty(collection, requirements);
						predicates.Add(new(predicate, property, WikiSemanticScalarTag.Keyword, Id(keywordId), Id(keywordId)));
						break;
					case SemanticPredicate.BooleanEquals boolean:
						var flag = Id(boolean.Value ? 1UL : 0UL, allowZero: true);
						predicates.Add(new(predicate, property, WikiSemanticScalarTag.Boolean, flag, flag));
						break;
					case SemanticPredicate.IntegerEquals integer:
						var exact = WikiSemanticKeys.EncodeInteger(integer.Value);
						predicates.Add(new(predicate, property, WikiSemanticScalarTag.Integer, exact, exact));
						break;
					case SemanticPredicate.IntegerRange range:
						predicates.Add(new(predicate, property, WikiSemanticScalarTag.Integer,
							WikiSemanticKeys.EncodeInteger(range.Low), WikiSemanticKeys.EncodeInteger(range.High)));
						break;
				}
			}
			var columns = new List<(string Name, ulong Property)>();
			foreach (var column in query.Columns) columns.Add((column, RegistryId(section, WikiSemanticRegistryKind.Property, column)));
			_resolved = new(collection, admitted, requirements, predicates, predicates[Driver(query)], columns);
			return SemanticSectionStep<Answer>.Continue;
		}

		private SemanticSectionStep<Answer> Empty(ulong collection, WikiRequirements requirements)
		{
			_resolved = new(collection, [], requirements, [], null, []);
			return SemanticSectionStep<Answer>.Continue;
		}

		private SemanticSectionStep<Answer> Seek(SemanticReadSection section)
		{
			var resolved = _resolved!;
			_candidates = [];
			if (resolved.Driver is not { } driver) return SemanticSectionStep<Answer>.Continue;
			var wanted = query.PageSize + 1;
			var probes = 0;
			var denied = 0;
			var streams = new List<Candidate>();
			foreach (var classId in resolved.Admitted)
			{
				var prefix = new byte[25];
				BinaryPrimitives.WriteUInt64BigEndian(prefix, resolved.Collection);
				BinaryPrimitives.WriteUInt64BigEndian(prefix.AsSpan(8), classId);
				BinaryPrimitives.WriteUInt64BigEndian(prefix.AsSpan(16), driver.Property);
				prefix[24] = (byte)driver.Tag;
				var start = Concat(prefix, driver.Low);
				var end = Successor(Concat(prefix, driver.High));
				var after = cursor is null ? []
					: Concat(prefix, driver.Source is SemanticPredicate.IntegerRange ? cursor.Order : driver.Low, Id(cursor.Subject));
				var taken = 0;
				var resume = after;
				// Read in small bounded steps until this class has supplied enough authorized candidates.
				while (taken < wanted)
				{
					var rows = section.Range(AtomicWikiLayout.Postings, prefix, start, end, resume, wanted);
					if (rows.Count == 0) break;
					foreach (var row in rows)
					{
						resume = row.Key;
						if (++probes > budgets.CandidateProbes) { Refine = "candidate probes"; return SemanticSectionStep<Answer>.Unavailable; }
						var subject = BinaryPrimitives.ReadUInt64BigEndian(row.Key.AsSpan(33));
						var order = row.Key.AsSpan(25, 8).ToArray();
						if (Visible(section, resolved.Requirements, subject) is not { } summary || summary.CollectionId != resolved.Collection
							|| !resolved.Predicates.Where(p => !ReferenceEquals(p, driver)).All(p => Holds(section, subject, p)))
						{
							if (++denied > budgets.DeniedSkips) { Refine = "denied candidates"; return SemanticSectionStep<Answer>.Unavailable; }
							continue;
						}
						streams.Add(new(subject, driver.Source is SemanticPredicate.IntegerRange ? order : [], summary.Summary.Reference.Id, summary.Summary.Title));
						taken++;
						if (taken == wanted) break;
					}
					if (rows.Count < wanted) break;
				}
			}
			_candidates = [.. streams.OrderBy(c => c.Order, ByteOrder.Instance).ThenBy(c => c.Subject).DistinctBy(c => c.Subject).Take(wanted)];
			return SemanticSectionStep<Answer>.Continue;
		}

		private SemanticSectionStep<Answer> Project(SemanticReadSection section)
		{
			var resolved = _resolved!;
			var candidates = _candidates ?? [];
			var page = candidates.Take(query.PageSize).ToList();
			var rows = new List<SemanticQueryRow>(page.Count);
			foreach (var candidate in page)
			{
				var columns = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
				foreach (var (name, property) in resolved.Columns)
					columns[name] = property == 0 ? [] : ColumnValues(section, resolved.Requirements, candidate.Subject, property);
				rows.Add(new(candidate.PageId, candidate.Title, columns));
			}
			var next = candidates.Count > query.PageSize ? new Position(page[^1].Order.Length == 0 ? new byte[8] : page[^1].Order, page[^1].Subject) : null;
			return SemanticSectionStep<Answer>.Answer(new Answer(rows, next));
		}

		private static bool Holds(SemanticReadSection section, ulong subject, ResolvedPredicate predicate)
		{
			var prefix = new byte[17];
			BinaryPrimitives.WriteUInt64BigEndian(prefix, subject);
			BinaryPrimitives.WriteUInt64BigEndian(prefix.AsSpan(8), predicate.Property);
			prefix[16] = (byte)predicate.Tag;
			if (predicate.Low.AsSpan().SequenceEqual(predicate.High))
				return section.TryGet(AtomicWikiLayout.Forward, Concat(prefix, predicate.Low), out _);
			return section.Range(AtomicWikiLayout.Forward, prefix, Concat(prefix, predicate.Low), Successor(Concat(prefix, predicate.High)), [], 1).Count > 0;
		}

		private static IReadOnlyList<string> ColumnValues(SemanticReadSection section, WikiRequirements requirements, ulong subject, ulong property)
		{
			var prefix = new byte[16];
			BinaryPrimitives.WriteUInt64BigEndian(prefix, subject);
			BinaryPrimitives.WriteUInt64BigEndian(prefix.AsSpan(8), property);
			var values = new List<string>();
			foreach (var row in section.Range(AtomicWikiLayout.Forward, prefix, [], [], [], WikiSemanticLimits.MaxFacts))
			{
				var tag = (WikiSemanticScalarTag)row.Key[16];
				var value = row.Key.AsSpan(17, 8);
				switch (tag)
				{
					case WikiSemanticScalarTag.Integer: values.Add(WikiSemanticKeys.DecodeInteger(value).ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
					case WikiSemanticScalarTag.Boolean: values.Add(BinaryPrimitives.ReadUInt64BigEndian(value) == 1 ? "true" : "false"); break;
					case WikiSemanticScalarTag.Keyword:
						if (Reverse(section, WikiSemanticRegistryKind.Keyword, BinaryPrimitives.ReadUInt64BigEndian(value)) is { } keyword) values.Add(keyword);
						break;
					case WikiSemanticScalarTag.PageRef:
						// A related page's label is projected only when the reader may see that page too.
						if (Visible(section, requirements, BinaryPrimitives.ReadUInt64BigEndian(value)) is { } target) values.Add(target.Summary.Reference.Id);
						break;
				}
			}
			return values;
		}
	}

	private sealed record ResolvedPredicate(SemanticPredicate Source, ulong Property, WikiSemanticScalarTag Tag, byte[] Low, byte[] High);
	private sealed record Resolved(ulong Collection, IReadOnlyList<ulong> Admitted, WikiRequirements Requirements,
		IReadOnlyList<ResolvedPredicate> Predicates, ResolvedPredicate? Driver, IReadOnlyList<(string Name, ulong Property)> Columns);

	// ── Visibility: the same decision WikiAccessService makes, on the projection's own descriptor ──

	private static WikiSemanticSummaryState? Visible(SemanticReadSection section, WikiRequirements requirements, ulong subject)
	{
		if (!section.TryGet(AtomicWikiLayout.Summaries, WikiSemanticKeys.Subject(subject), out var bytes)) return null;
		var state = WikiSemanticStateCodec.ProjectionState(bytes).State;
		return !state.Summary.Deleted && MaySee(section.Reader, requirements, state.Summary.Visibility, state.Summary.Reference.Id) ? state : null;
	}

	private static bool MaySee(WikiReader reader, WikiRequirements requirements, VisibilityDescriptor visibility, string? pageId = null)
	{
		var draftVisible = visibility.Published || reader.Has(Library.Authorization.PortalPermission.WikiDrafts)
			|| reader.Dbref is { Length: > 0 } me && string.Equals(visibility.DraftAuthor, me, StringComparison.Ordinal);
		// Class admission passes no page id, so page rules cannot exclude a class; every candidate is then
		// rechecked with its own page id, which applies them exactly.
		return draftVisible && WikiAccessService.Decide(reader, requirements, visibility.Namespace, visibility.Categories, pageId, WikiAction.Read).Allowed;
	}

	private static WikiRequirements Requirements(SemanticReadSection section)
		=> new(section.Range(Tables.WikiRequirement, [], [], [], [], 4096)
			.Select(row => LightningDatabase.MapWikiRequirementForQueries(Codec.Deserialize<WikiRequirementRecord>(row.Value)))
			.OfType<WikiRequirementSet>());

	private static ulong RegistryId(SemanticReadSection section, WikiSemanticRegistryKind kind, string identity)
	{
		if (!section.TryGet(AtomicWikiLayout.Registry, WikiSemanticKeys.RegistryLookup(kind, identity), out var bytes)) return 0;
		var entry = WikiSemanticStateCodec.Registry(bytes);
		return entry.Identity == identity ? entry.Id : 0;
	}

	private static string? Reverse(SemanticReadSection section, WikiSemanticRegistryKind kind, ulong id)
		=> section.TryGet(AtomicWikiLayout.Registry, WikiSemanticKeys.RegistryReverse(kind, id), out var bytes)
			? WikiSemanticStateCodec.Registry(bytes).Identity : null;

	// ── Keys ──

	private static byte[] Id(ulong value, bool allowZero = false)
	{
		if (value == 0 && !allowZero) throw new ArgumentOutOfRangeException(nameof(value));
		var bytes = new byte[8];
		BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
		return bytes;
	}

	private static byte[] Pair(ulong first, ulong second) => Concat(Id(first), Id(second));

	private static byte[] Concat(params byte[][] parts)
	{
		var bytes = new byte[parts.Sum(p => p.Length)];
		var offset = 0;
		foreach (var part in parts) { part.CopyTo(bytes, offset); offset += part.Length; }
		return bytes;
	}

	/// <summary>The smallest key greater than every key that starts with <paramref name="key"/>.</summary>
	private static byte[] Successor(byte[] key)
	{
		var bytes = (byte[])key.Clone();
		for (var i = bytes.Length - 1; i >= 0; i--)
		{
			if (bytes[i] != 0xFF) { bytes[i]++; return bytes[..(i + 1)]; }
		}
		return [];
	}

	private sealed class ByteOrder : IComparer<byte[]>
	{
		internal static readonly ByteOrder Instance = new();
		public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
	}

	// ── Continuation: AES-GCM under the lease's in-memory key; fixture and lease epoch are associated data ──

	private sealed record Cursor(byte[] Query, byte[] Reader, ulong Package, ulong Content, ulong Requirements, ulong Authorization,
		byte[] Partitions, byte[] Order, ulong Subject, long ExpiresTicks, ulong Locale)
	{
		internal bool Matches(SemanticReadStamp stamp) => Package == stamp.PackageGeneration && Content == stamp.ContentGeneration
			&& Requirements == stamp.RequirementsVersion && Authorization == stamp.AuthorizationVersion;
	}

	private static byte[] Associated(SemanticFixtureLease lease)
	{
		var bytes = new byte[24];
		lease.FixtureId.TryWriteBytes(bytes, bigEndian: true, out _);
		BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(16), lease.Epoch);
		return bytes;
	}

	private static string SealToken(SemanticFixtureLease lease, Cursor cursor)
	{
		var payload = new byte[TokenPayloadBytes];
		var span = payload.AsSpan();
		span[0] = 1;
		cursor.Query.CopyTo(span[1..33]);
		cursor.Reader.CopyTo(span[33..65]);
		BinaryPrimitives.WriteUInt64BigEndian(span[65..], cursor.Package);
		BinaryPrimitives.WriteUInt64BigEndian(span[73..], cursor.Content);
		BinaryPrimitives.WriteUInt64BigEndian(span[81..], cursor.Requirements);
		BinaryPrimitives.WriteUInt64BigEndian(span[89..], cursor.Authorization);
		cursor.Partitions.CopyTo(span[97..129]);
		cursor.Order.CopyTo(span[129..137]);
		BinaryPrimitives.WriteUInt64BigEndian(span[137..], cursor.Subject);
		BinaryPrimitives.WriteInt64BigEndian(span[145..], cursor.ExpiresTicks);
		BinaryPrimitives.WriteUInt64BigEndian(span[153..], cursor.Locale);
		var nonce = RandomNumberGenerator.GetBytes(12);
		var sealedBytes = new byte[12 + TokenPayloadBytes + 16];
		nonce.CopyTo(sealedBytes, 0);
		using var aes = new AesGcm(lease.ContinuationKey, 16);
		aes.Encrypt(nonce, payload, sealedBytes.AsSpan(12, TokenPayloadBytes), sealedBytes.AsSpan(12 + TokenPayloadBytes), Associated(lease));
		return Convert.ToBase64String(sealedBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
	}

	private static Cursor? OpenToken(SemanticFixtureLease lease, string token, byte[] query, TimeProvider clock)
	{
		byte[] sealedBytes;
		try
		{
			var padded = token.Replace('-', '+').Replace('_', '/');
			sealedBytes = Convert.FromBase64String(padded + new string('=', (4 - padded.Length % 4) % 4));
		}
		catch (FormatException) { return null; }
		if (sealedBytes.Length != 12 + TokenPayloadBytes + 16) return null;
		var payload = new byte[TokenPayloadBytes];
		try
		{
			using var aes = new AesGcm(lease.ContinuationKey, 16);
			aes.Decrypt(sealedBytes.AsSpan(0, 12), sealedBytes.AsSpan(12, TokenPayloadBytes), sealedBytes.AsSpan(12 + TokenPayloadBytes),
				payload, Associated(lease));
		}
		catch (AuthenticationTagMismatchException) { return null; }
		var span = payload.AsSpan();
		if (span[0] != 1 || !span[1..33].SequenceEqual(query)) return null;
		var cursor = new Cursor(span[1..33].ToArray(), span[33..65].ToArray(), BinaryPrimitives.ReadUInt64BigEndian(span[65..]),
			BinaryPrimitives.ReadUInt64BigEndian(span[73..]), BinaryPrimitives.ReadUInt64BigEndian(span[81..]),
			BinaryPrimitives.ReadUInt64BigEndian(span[89..]), span[97..129].ToArray(), span[129..137].ToArray(),
			BinaryPrimitives.ReadUInt64BigEndian(span[137..]), BinaryPrimitives.ReadInt64BigEndian(span[145..]),
			BinaryPrimitives.ReadUInt64BigEndian(span[153..]));
		return cursor.ExpiresTicks > clock.GetUtcNow().UtcTicks ? cursor : null;
	}
}
