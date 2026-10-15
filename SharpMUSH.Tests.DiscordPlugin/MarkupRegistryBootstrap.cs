using MarkupString;
using MarkupString.Ansi;
using MarkupString.Html;

namespace SharpMUSH.Tests.DiscordPlugin;

/// <summary>Installs the markup layers the server installs, since a line's text is rendered for Discord.</summary>
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
