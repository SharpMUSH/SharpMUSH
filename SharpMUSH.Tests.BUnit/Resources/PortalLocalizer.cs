using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Tests.BUnit.Resources;

/// <summary>
/// Pins <see cref="CultureInfo.CurrentUICulture"/> for the duration of a test.
/// <para>
/// Any test asserting a specific resource <em>value</em> must use this. Without it the assertion
/// silently depends on the machine's locale: a developer or CI runner set to <c>fr</c> resolves the
/// French satellite and an assertion on the English value fails for a reason that has nothing to do
/// with the code under test.
/// </para>
/// </summary>
internal sealed class CultureScope : IDisposable
{
	private readonly CultureInfo _previous;

	private CultureScope(string tag)
	{
		_previous = CultureInfo.CurrentUICulture;
		CultureInfo.CurrentUICulture = new CultureInfo(tag);
	}

	public static CultureScope For(string tag) => new(tag);

	public void Dispose() => CultureInfo.CurrentUICulture = _previous;
}

/// <summary>
/// A real <see cref="IStringLocalizer{T}"/> over the embedded resx, built through the production
/// registration. Tests that care about what a label actually says must use this rather than the
/// key-echoing stubs the component tests install.
/// </summary>
internal static class PortalLocalizer
{
	public static IStringLocalizer<SharedResource> Create()
		=> new ServiceCollection()
			.AddLogging()
			.AddSharedResourceLocalization()
			.BuildServiceProvider()
			.GetRequiredService<IStringLocalizer<SharedResource>>();
}
