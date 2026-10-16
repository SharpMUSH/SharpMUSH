using SharpMUSH.Implementation.Handlers.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Tests.Services;

public class UnreadableDiscoveryCompilationTests
{
	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task UnusableDiscoveryRowsNeverCompileEvenWithPatternShapedFallback(bool metadata)
	{
		var attribute = new SharpAttribute("id", "key", "BLOCK", [], null, "BLOCK", null!, null!, null!)
		{
			IsUnreadable = true,
			IsMetadataUnreadable = metadata,
			Value = MarkupText.Plain("$unsafe:think must not run")
		};
		await Assert.That(CommandAttributeScanner.Compile(attribute) is NotFound).IsTrue();
		await Assert.That(ListenAttributeSearch.Compile([attribute with { Value = MarkupText.Plain("^unsafe:think must not run") }])).IsEmpty();
	}
}
