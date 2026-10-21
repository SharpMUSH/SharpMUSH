using System.Buffers.Binary;
using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Database.Lightning.Semantics;

internal sealed record WikiProjectionPlanned(ulong NativeId, long StateVersion, long CommitSequence);
internal sealed record WikiProjectionConflict(long CurrentStateVersion);
internal union WikiProjectionPlanResult(WikiProjectionPlanned, WikiProjectionConflict);
internal readonly record struct AllocateNextSequence;
internal union ProjectionSequence(AllocateNextSequence, ExistingBootstrapSequence);

/// <summary>Only the activation owner allocates a bootstrap sequence, once for all seed contributions.</summary>
internal sealed class ExistingBootstrapSequence
{
	private ExistingBootstrapSequence(long sequence) => Sequence = sequence;
	internal long Sequence { get; }
	internal static ExistingBootstrapSequence AllocateForActivation(WikiSemanticWork work)
		=> new(WikiSemanticProjectionPlanner.AllocateSequence(work));
}

/// <summary>Contributes only projection rows to the owner's unapplied delta. Never opens tables or writes a receipt.</summary>
internal static class WikiSemanticProjectionPlanner
{
	/// <param name="allowSourceMove">Only an ownership write may move a page between local and package sources, in its own job.</param>
	internal static WikiProjectionPlanResult PlanProjection(WikiSemanticWork work, PreparedWikiProjection edit,
		long expectedStateVersion, ProjectionSequence sequenceMode, bool allowSourceMove = false)
	{
		var ct = work.Budget.CancellationToken;
		ct.ThrowIfCancellationRequested();
		ArgumentOutOfRangeException.ThrowIfNegative(expectedStateVersion);
		var subject = work.Registry(WikiSemanticRegistryKind.Page, edit.Page.Reference.Id, false);
		WikiSemanticSummaryState? old = null;
		if (subject != 0)
		{
			var key = work.Bytes(8, () => WikiSemanticKeys.Subject(subject));
			if (work.Tables.LegacyB1Summary) old = work.Read(work.Tables.Summaries, key, WikiSemanticStateCodec.Summary);
			else
			{
				var previous = work.Read(work.Tables.Summaries, key, WikiSemanticStateCodec.ProjectionState);
				if (previous is not null && !allowSourceMove && !SameSource(previous.Source, edit.Page.Source))
					return new WikiProjectionConflict(previous.State.Summary.StateVersion);
				old = previous?.State;
			}
		}
		var currentVersion = old?.Summary.StateVersion ?? 0;
		if (currentVersion != expectedStateVersion) return new WikiProjectionConflict(currentVersion);
		if (old?.Summary.Deleted == true) return new WikiProjectionConflict(currentVersion);
		subject = subject == 0 ? work.Registry(WikiSemanticRegistryKind.Page, edit.Page.Reference.Id) : subject;
		var subjectKey = work.Bytes(8, () => WikiSemanticKeys.Subject(subject));
		var oldFacts = old is null ? null : work.Read(work.Tables.Facts, subjectKey, bytes => WikiSemanticCodec.DecodeFacts(bytes), 8, 1024);
		if (old is not null && oldFacts is null) throw new InvalidDataException("Missing canonical fact bundle.");
		var collection = work.Registry(WikiSemanticRegistryKind.Collection, edit.Page.Collection);
		var classId = WikiSemanticVisibilityCatalog.Move(work, old, edit.Page, collection, !edit.Facts.Facts.IsEmpty && !edit.Deleted);
		if (oldFacts is not null)
			foreach (var fact in oldFacts.Facts) { ct.ThrowIfCancellationRequested(); Index(work, subject, old!.CollectionId, old.ClassId, fact, true); }
		foreach (var fact in edit.Facts.Facts) { ct.ThrowIfCancellationRequested(); Index(work, subject, collection, classId, fact, false); }
		var version = checked(currentVersion + 1);
		var sequence = Sequence(work, sequenceMode);
		var summary = new WikiSemanticSummary(edit.Page.Reference, subject, edit.Page.Title, edit.Page.Visibility, edit.Page.Collection, edit.Deleted, version);
		var state = new WikiSemanticSummaryState(summary, collection, classId, sequence);
		var factBytes = work.Bytes(WikiSemanticCodec.MeasureFacts(edit.Facts, WikiSemanticLimits.MaxFactBytes), () => WikiSemanticCodec.EncodeFacts(edit.Facts));
		var summaryBytes = work.Bytes(WikiSemanticCodec.SummarySize(edit.Page) + (work.Tables.LegacyB1Summary ? 0 : WikiSemanticCodec.MeasureProjection(edit.Page)) + 128L,
			() => work.Tables.LegacyB1Summary ? WikiSemanticStateCodec.Summary(state)
				: WikiSemanticStateCodec.ProjectionState(new WikiSemanticProjectionState(state, edit.Page.Source)));
		if (summaryBytes.Length > WikiSemanticLimits.MaxSummaryBytes) throw new WikiSemanticCapacityException("summary-bytes");
		work.Delta.Put(work.Tables.Facts, subjectKey, factBytes);
		work.Delta.Put(work.Tables.Summaries, subjectKey, summaryBytes);
		if (old is { ClassId: > 0 }) BumpPartition(work, old.CollectionId, old.ClassId);
		if (classId > 0 && (old is null || old.ClassId != classId || old.CollectionId != collection)) BumpPartition(work, collection, classId);
		return new WikiProjectionPlanned(subject, version, sequence);
	}
	private static void Index(WikiSemanticWork work, ulong subject, ulong collection, ulong classId, SemanticFact fact, bool remove)
	{
		if (classId == 0) throw new InvalidDataException("Fact without source class.");
		var property = work.Registry(WikiSemanticRegistryKind.Property, fact.Property, !remove);
		if (property == 0) throw new InvalidDataException("Missing property identity.");
		WikiSemanticScalarTag tag;
		byte[] value;
		switch (fact.Value)
		{
			case PageRef page:
				tag = WikiSemanticScalarTag.PageRef;
				var target = work.Registry(WikiSemanticRegistryKind.Page, page.Id, !remove);
				if (target == 0) throw new InvalidDataException("Missing target identity.");
				value = work.Number(target); break;
			case long integer: tag = WikiSemanticScalarTag.Integer; value = work.Bytes(8, () => WikiSemanticKeys.EncodeInteger(integer)); break;
			case bool boolean: tag = WikiSemanticScalarTag.Boolean; value = work.Number(boolean ? 1UL : 0UL); break;
			case Keyword keyword:
				tag = WikiSemanticScalarTag.Keyword;
				var keywordId = work.Registry(WikiSemanticRegistryKind.Keyword, keyword.Value, !remove);
				if (keywordId == 0) throw new InvalidDataException("Missing keyword identity.");
				value = work.Number(keywordId); break;
			default: throw new InvalidDataException("Unknown semantic scalar.");
		}
		var forward = work.Bytes(25, () => WikiSemanticKeys.Forward(subject, property, tag, value));
		var posting = work.Bytes(41, () => WikiSemanticKeys.Posting(collection, classId, property, tag, value, subject));
		if (remove) { work.Delta.Delete(work.Tables.Forward, forward); work.Delta.Delete(work.Tables.Postings, posting); }
		else { work.Delta.Put(work.Tables.Forward, forward, []); work.Delta.Put(work.Tables.Postings, posting, []); }
	}
	private static void BumpPartition(WikiSemanticWork work, ulong collection, ulong classId)
	{
		var key = work.Pair(collection, classId);
		var old = work.ReadBytes(work.Tables.Partitions, key);
		if (old is not null && old.Length != 8) throw new InvalidDataException("Malformed partition version.");
		var version = old is null ? 0 : BinaryPrimitives.ReadUInt64BigEndian(old);
		work.Delta.Put(work.Tables.Partitions, key, work.Number(checked(version + 1)));
	}

	private static bool SameSource(WikiSemanticSource left, WikiSemanticSource right) => (left, right) switch
	{
		(LocalWikiSource, LocalWikiSource) => true,
		(PortableWikiSource a, PortableWikiSource b) => a == b,
		_ => false
	};

	internal static long AllocateSequence(WikiSemanticWork work)
	{
		var key = work.Key(2);
		var value = work.Counter(key);
		if (value >= long.MaxValue) throw new WikiSemanticCapacityException("commit-sequence");
		var sequence = checked((long)value + 1);
		work.Delta.Put(work.Tables.Metadata, key, work.Number((ulong)sequence));
		return sequence;
	}
	private static long Sequence(WikiSemanticWork work, ProjectionSequence sequence) => sequence switch
	{
		AllocateNextSequence => AllocateSequence(work),
		ExistingBootstrapSequence existing when existing.Sequence > 0 && work.Counter(work.Key(2)) == (ulong)existing.Sequence => existing.Sequence,
		_ => throw new InvalidDataException("Invalid or unallocated bootstrap sequence.")
	};
}
