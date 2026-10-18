using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Tools.WikiSemantics.Native;

internal sealed record NativeSummaryState(NativePageSummary Summary, ulong CollectionId, ulong ClassId, long CommitSequence);

/// <summary>B1-only summary and receipt adapters over the provider's unchanged compact format.</summary>
internal static class NativeStateCodec
{
	internal static byte[] Registry(WikiSemanticRegistryEntry entry) => WikiSemanticStateCodec.Registry(entry);
	internal static WikiSemanticRegistryEntry Registry(byte[] bytes) => WikiSemanticStateCodec.Registry(bytes);
	internal static byte[] Descriptor(VisibilityDescriptor value) => WikiSemanticStateCodec.Descriptor(value);
	internal static byte[] Catalog(WikiSemanticCatalogEntry entry) => WikiSemanticStateCodec.Catalog(entry);
	internal static WikiSemanticCatalogEntry Catalog(byte[] bytes) => WikiSemanticStateCodec.Catalog(bytes);
	internal static WikiSemanticSummaryState Shared(NativeSummaryState state) => new(new(state.Summary.Reference,
		state.Summary.NativeId, state.Summary.Title, state.Summary.Visibility, state.Summary.Collection,
		state.Summary.Deleted, state.Summary.StateVersion), state.CollectionId, state.ClassId, state.CommitSequence);
	internal static NativeSummaryState Legacy(WikiSemanticSummaryState state) => new(new(state.Summary.Reference,
		state.Summary.NativeId, state.Summary.Title, state.Summary.Visibility, state.Summary.Collection,
		state.Summary.Deleted, state.Summary.StateVersion), state.CollectionId, state.ClassId, state.CommitSequence);
	internal static byte[] Summary(NativeSummaryState state) => WikiSemanticStateCodec.Summary(Shared(state));
	internal static NativeSummaryState Summary(byte[] bytes) => Legacy(WikiSemanticStateCodec.Summary(bytes));
	internal static byte[] Mutation(NativeMutationStatus status) => WikiSemanticStateCodec.Encode(w =>
	{
		w.Write(status.MutationId.ToByteArray()); w.Write(status.RequestDigest);
		w.Write(status.Committed.StateVersion); w.Write(status.Committed.CommitSequence);
	});
	internal static NativeMutationStatus Mutation(byte[] bytes) => WikiSemanticStateCodec.Decode(bytes, r =>
	{
		var guid = new Guid(r.ReadBytes(16)); var digest = WikiSemanticStateCodec.String(r);
		return new NativeMutationStatus(guid, digest, new(guid, r.ReadInt64(), r.ReadInt64()));
	});
}
