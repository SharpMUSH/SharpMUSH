using SharpMUSH.Database.Lightning.Semantics;
using SharpMUSH.Library.Models.Wiki.Semantics;

namespace SharpMUSH.Tools.WikiSemantics.Native;

/// <summary>B1 kind-1 full page/stamp envelope; primitives and fact/schema codecs are provider-owned.</summary>
public static class NativeCodec
{
	public const byte FormatVersion = WikiSemanticCodec.FormatVersion;
	public static byte[] EncodePage(PageSnapshot page) => WikiSemanticCodec.Encode(w => WritePage(w, page), WikiSemanticCodec.RowLimit);
	public static byte[] EncodeFacts(CanonicalFactSet facts) => WikiSemanticCodec.EncodeFacts(facts);
	internal static int MeasurePage(PageSnapshot page) => WikiSemanticCodec.Measure(w => WritePage(w, page), WikiSemanticCodec.RowLimit);
	internal static int MeasureFacts(CanonicalFactSet facts, int limit) => WikiSemanticCodec.MeasureFacts(facts, limit);
	internal static int MeasureSchema(SemanticSchema schema) => WikiSemanticCodec.MeasureSchema(schema);
	internal static byte[] SchemaIdentity(SemanticSchema schema) => WikiSemanticCodec.SchemaIdentity(schema);
	internal static int DescriptorSize(VisibilityDescriptor descriptor) => WikiSemanticCodec.DescriptorSize(descriptor);
	internal static int SummarySize(PageSnapshot page) => WikiSemanticCodec.SummarySize(Projection(page));
	internal static WikiPageProjection Projection(PageSnapshot page)
		=> new(page.Reference, new PortableWikiSource(page.Key), page.Title, page.Collections[0], page.Visibility);
	public static CanonicalFactSet DecodeFacts(ReadOnlySpan<byte> data) => WikiSemanticCodec.DecodeFacts(data);
	public static PageSnapshot DecodePage(ReadOnlySpan<byte> data)
	{
		var r = new WikiSemanticCodec.Reader(data, 1);
		var reference = new PageRef(r.String());
		var key = new PortableKey(r.String(), r.String(), r.String());
		var title = r.String();
		var body = r.String();
		var collections = r.Strings();
		var visibility = r.Visibility();
		var stamp = new ExperimentalStamp(r.String(), r.Int64(), r.Int64(), r.Int64(), r.Int64(), r.Int64(), r.Int64(), r.Int64());
		r.End();
		return new(reference, key, title, body, collections, visibility, stamp);
	}
	private static void WritePage(WikiSemanticCodec.Writer w, PageSnapshot page)
	{
		ArgumentNullException.ThrowIfNull(page);
		w.Byte(FormatVersion);
		w.Byte(1);
		w.String(page.Reference.Id);
		w.String(page.Key.Package);
		w.String(page.Key.Namespace);
		w.String(page.Key.SourceKey);
		w.String(page.Title);
		w.String(page.Body);
		w.Strings(page.Collections);
		WikiSemanticCodec.WriteVisibility(w, page.Visibility);
		ArgumentNullException.ThrowIfNull(page.Stamp);
		w.String(page.Stamp.SemanticCommit);
		w.Int64(page.Stamp.IndexGeneration);
		w.Int64(page.Stamp.SchemaVersion);
		w.Int64(page.Stamp.RequirementsVersion);
		w.Int64(page.Stamp.CollectionVersion);
		w.Int64(page.Stamp.PartitionVersion);
		w.Int64(page.Stamp.PageVersion);
		w.Int64(page.Stamp.TranslationVersion);
	}
}
