using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Configuration;

public class ColorTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }
	private IOptionsWrapper<ColorsOptions> Configuration => WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<ColorsOptions>>();

	[Test]
	public async Task BasicLookupSuccess()
	{
		var config = Configuration.CurrentValue;

		await Assert.That(config.Colors).IsNotNull();
		await Assert.That(config.ColorsByName).IsNotNull();
		await Assert.That(config.Colors.Length).IsGreaterThan(0);
	}

	[Test]
	public async Task NameLookupSuccess()
	{
		var found = Configuration.CurrentValue.ColorsByName.TryGetValue("antiquewhite4", out var color);

		await Assert.That(found).IsTrue();
		await Assert.That(color!.ansi).IsEqualTo(256);
	}

	[Test]
	public async Task AnsiLookupSuccess()
	{
		var found = Configuration.CurrentValue.ColorsByAnsi.TryGetValue("256", out var color);

		await Assert.That(found).IsTrue();
		await Assert.That(color!.First(x => x.name == "antiquewhite4").ansi).IsEqualTo(256);
	}

	[Test]
	public async Task RgbLookupSuccess()
	{
		var found = Configuration.CurrentValue.ColorsByRgb.TryGetValue("0x8b8378", out var color);

		await Assert.That(found).IsTrue();
		await Assert.That(color!.First(x => x.name == "antiquewhite4").ansi).IsEqualTo(256);
	}

	[Test]
	public async Task XTermLookupSuccess()
	{
		var found = Configuration.CurrentValue.ColorsByXterm.TryGetValue("8", out var color);

		await Assert.That(found).IsTrue();
		await Assert.That(color!.First(x => x.name == "antiquewhite4").ansi).IsEqualTo(256);
	}
}
