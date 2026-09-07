using MarkupString.Ansi;
using MarkupString.Html;

namespace SharpMUSH.Tests.Markup;

/// <summary>
/// Guards <see cref="MarkupRegistryBootstrap"/>. Its <c>[Before(TestSession)]</c> hook is the only
/// thing that installs <see cref="MarkupRegistry.Default"/> for this assembly, and every render and
/// deserialisation in the suite resolves through it — a hook that stops running would otherwise
/// surface as an unrelated <see cref="InvalidOperationException"/> deep inside whichever test
/// happened to render first.
/// </summary>
public class MarkupRegistryBootstrapTests
{
	[Test]
	public async Task Default_IsConfiguredBeforeAnyTestRuns()
		=> await Assert.That(MarkupRegistry.IsConfigured).IsTrue();

	[Test]
	[Arguments("ansi", typeof(AnsiMarkup))]
	[Arguments("html", typeof(HtmlMarkup))]
	public async Task Default_ResolvesTheCodecForEachWireKind(string kind, Type markupType)
	{
		var byKind = MarkupRegistry.Default.FindCodec(kind);

		await Assert.That(byKind).IsNotNull();
		await Assert.That(byKind!.MarkupType).IsEqualTo(markupType);
		await Assert.That(MarkupRegistry.Default.FindCodec(markupType)).IsEqualTo(byKind);
	}
}
