using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Installs the markup layers before any test runs. <see cref="MarkupText"/> resolves emitters and
/// codecs through <see cref="MarkupRegistry.Default"/>, whose getter throws until something assigns
/// it, so every assembly that renders or deserialises markup has to configure it once.
/// </summary>
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
