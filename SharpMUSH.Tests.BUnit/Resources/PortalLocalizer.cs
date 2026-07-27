using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// Pins both <see cref="CultureInfo.CurrentUICulture"/> and <see cref="CultureInfo.CurrentCulture"/>
/// for the duration of a test.
/// <para>
/// Any test asserting a specific resource <em>value</em> must use this. Without it the assertion
/// silently depends on the machine's locale: a developer or CI runner set to <c>fr</c> resolves the
/// French satellite and an assertion on the English value fails for a reason that has nothing to do
/// with the code under test.
/// </para>
/// <para>
/// Both cultures are pinned, not just the UI one. <c>CurrentUICulture</c> alone selects which
/// satellite resolves, but a resource value containing <c>{0}</c> is rendered through
/// <c>string.Format</c> against <c>CurrentCulture</c> — so a runner whose two cultures differ can
/// still produce a machine-dependent number, date or currency inside an otherwise-pinned string.
/// </para>
/// </summary>
internal sealed class CultureScope : IDisposable
{
	private readonly CultureInfo _previousUi;
	private readonly CultureInfo _previousFormatting;

	private CultureScope(string tag)
	{
		_previousUi = CultureInfo.CurrentUICulture;
		_previousFormatting = CultureInfo.CurrentCulture;

		var culture = new CultureInfo(tag);
		CultureInfo.CurrentUICulture = culture;
		CultureInfo.CurrentCulture = culture;
	}

	public static CultureScope For(string tag) => new(tag);

	public void Dispose()
	{
		CultureInfo.CurrentUICulture = _previousUi;
		CultureInfo.CurrentCulture = _previousFormatting;
	}
}

/// <summary>
/// A real <see cref="IStringLocalizer{T}"/> over the embedded resx, built through the production
/// registration. Tests that care about what a label actually says must use this rather than the
/// key-echoing stubs the component tests install.
/// </summary>
internal static class PortalLocalizer
{
	// One provider for the whole suite. Building a ServiceProvider per call is not free, and each
	// one roots its disposable singletons until the process exits. Sharing is safe because the
	// localizer is stateless with respect to culture: ResourceManagerStringLocalizer reads
	// CultureInfo.CurrentUICulture at lookup time, not at construction, so a CultureScope in one
	// test cannot leak into another through this instance.
	private static readonly Lazy<IStringLocalizer<SharedResource>> Instance = new(() =>
		new ServiceCollection()
			.AddLogging()
			.AddSharedResourceLocalization()
			.BuildServiceProvider()
			.GetRequiredService<IStringLocalizer<SharedResource>>());

	public static IStringLocalizer<SharedResource> Create() => Instance.Value;
}
