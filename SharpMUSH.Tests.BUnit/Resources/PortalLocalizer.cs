using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using SharpMUSH.Client.Resources;

namespace SharpMUSH.Tests.BUnit.Resources;

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
