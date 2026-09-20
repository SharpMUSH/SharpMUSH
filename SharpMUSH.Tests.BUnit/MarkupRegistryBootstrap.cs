using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;

namespace SharpMUSH.Tests.BUnit;

/// <summary>
/// Installs the markup layers before any test runs. Components render <see cref="MarkupText"/>
/// through <see cref="MarkupRegistry.Default"/>, whose getter throws until something assigns it;
/// the client's own bootstrap lives in its <c>Program.cs</c>, which a component test never runs.
/// </summary>
/// <remarks>
/// Without this, the renderers that catch a failed render (<c>SchemaViewRenderer</c>,
/// <c>SceneMarkupRenderer</c>) would swallow the "not configured" exception and fall back to plain
/// text, so every markup assertion in this assembly would pass against unstyled output.
/// </remarks>
public static class MarkupRegistryBootstrap
{
	[Before(TestSession)]
	public static void Configure()
	{
		if (!MarkupRegistry.IsConfigured)
		{
			MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml();
		}
	}
}

/// <summary>
/// Guards <see cref="MarkupRegistryBootstrap"/>: its <c>[Before(TestSession)]</c> hook is the only
/// thing that configures the registry for this assembly, and a hook that stopped running would
/// surface as silently unstyled markup rather than as a failure.
/// </summary>
public class MarkupRegistryBootstrapTests
{
	[Test]
	public async Task Default_IsConfiguredBeforeAnyTestRuns()
		=> await Assert.That(MarkupRegistry.IsConfigured).IsTrue();
}
