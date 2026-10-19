using SharpMUSH.Library.Models.Wiki.Semantics;
namespace SharpMUSH.Database.Lightning.Semantics;

internal static class WikiSemanticLimits
{
	internal const int MaxFacts = WikiSemanticPolicy.MaxFacts;
	internal const int MaxFactBytes = WikiSemanticPolicy.MaxFactBytes;
	internal const int MaxReadBytes = 128 * 1024;
	internal const int MaxDeltaBytes = 256 * 1024;
	internal const int MaxSummaryBytes = WikiSemanticPolicy.MaxSummaryBytes;
	internal const int MaxDescriptorBytes = WikiSemanticPolicy.MaxDescriptorBytes;
	internal const ulong MaximumClasses = 1024;
	internal const ulong MaximumCatalogBytes = 1 << 20;
}
internal enum WikiSemanticScalarTag : byte { PageRef = 1, Integer = 2, Boolean = 3, Keyword = 4 }
internal enum WikiSemanticRegistryKind : byte { Page = 1, Property = 2, Keyword = 3, Collection = 4, Class = 5 }
