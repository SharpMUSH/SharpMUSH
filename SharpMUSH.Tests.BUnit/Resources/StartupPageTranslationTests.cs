using System.Globalization;
using System.Resources;
using SharpMUSH.Client.Resources;
using SharpMUSH.Server;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// The server's startup page (<see cref="PortalStartupPage"/>) is shown before the portal — and its
/// resources — can load, so it carries its own copy of two portal strings. The resx stays the
/// translators' source; this keeps the copy in step with it, locale for locale.
/// </summary>
public class StartupPageTranslationTests
{
	private static readonly ResourceManager Resources = new(typeof(SharedResource));

	[Test]
	public async Task EveryPortalLocale_HasAStartupMessage()
	{
		await Assert.That(PortalStartupPage.Messages.Keys.ToArray()).IsEquivalentTo(PortalLocales.Codes.ToArray());
	}

	[Test]
	public async Task TheStartupMessages_MatchTheResx()
	{
		foreach (var code in PortalLocales.Codes)
		{
			var culture = CultureInfo.GetCultureInfo(code);
			var (title, detail) = PortalStartupPage.Messages[code];

			await Assert.That(title).IsEqualTo(Resources.GetString("WidGameStartingUp", culture)).Because(code);
			await Assert.That(detail).IsEqualTo(Resources.GetString("WidWaitingForServer", culture)).Because(code);
		}
	}
}
