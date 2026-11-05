using System.Collections;
using System.Globalization;
using System.Resources;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// MudBlazor's own text is served from <see cref="SharedResource"/> by
/// <see cref="PortalMudLocalizationInterceptor"/>. A key MudBlazor adds in an upgrade and the resx
/// lacks would render as the bare key, so the resx must hold every key MudBlazor ships.
/// </summary>
public class MudBlazorTextTests
{
	private static IEnumerable<string> MudBlazorKeys()
	{
		var manager = new ResourceManager("MudBlazor.Resources.LanguageResource", typeof(MudBlazor.MudButton).Assembly);
		var set = manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)
			?? throw new InvalidOperationException("MudBlazor ships no LanguageResource");
		return set.Cast<DictionaryEntry>().Select(e => (string)e.Key);
	}

	[Test]
	public async Task The_resx_holds_every_key_MudBlazor_ships()
	{
		var neutral = SharedResources.Manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false)!;
		var missing = MudBlazorKeys().Where(k => neutral.GetString(k) is null).Order(StringComparer.Ordinal).ToList();

		await Assert.That(missing).IsEmpty();
	}

	[Test]
	public async Task The_interceptor_answers_in_the_reader_language()
	{
		using var culture = CultureScope.For("de");
		var interceptor = new PortalMudLocalizationInterceptor(PortalLocalizer.Create());

		var text = interceptor.Handle("MudDataGridPager_InfoFormat", 1, 10, 42);

		await Assert.That(text.ResourceNotFound).IsFalse();
		await Assert.That(text.Value).Contains("42");
		await Assert.That(text.Value).IsNotEqualTo("1-10 of 42");
	}
}
