using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Tests.Configuration;

public class ColorTests
{
	/// <summary>The shipped colors.json, read through the factory the server registers for it.</summary>
	private static readonly ColorsOptions Colors =
		new ReadColorsOptionsFactory(NullLogger<ReadColorsOptionsFactory>.Instance,
			Path.Join(AppContext.BaseDirectory, "colors.json")).Create(string.Empty);

	[Test]
	public async Task BasicLookupSuccess()
	{
		var config = Colors;

		await Assert.That(config.Colors).IsNotNull();
		await Assert.That(config.ColorsByName).IsNotNull();
		await Assert.That(config.Colors.Length).IsGreaterThan(0);
	}

	[Test]
	public async Task NameLookupSuccess()
	{
		var found = Colors.ColorsByName.TryGetValue("antiquewhite4", out var color);

		await Assert.That(found).IsTrue();
		await Assert.That(color!.ansi).IsEqualTo(256);
	}

	[Test]
	public async Task AnsiLookupSuccess()
	{
		var found = Colors.ColorsByAnsi.TryGetValue("256", out var color);

		await Assert.That(found).IsTrue();
		await Assert.That(color!.First(x => x.name == "antiquewhite4").ansi).IsEqualTo(256);
	}

	[Test]
	public async Task RgbLookupSuccess()
	{
		var found = Colors.ColorsByRgb.TryGetValue("0x8b8378", out var color);

		await Assert.That(found).IsTrue();
		await Assert.That(color!.First(x => x.name == "antiquewhite4").ansi).IsEqualTo(256);
	}

	[Test]
	public async Task XTermLookupSuccess()
	{
		var found = Colors.ColorsByXterm.TryGetValue("8", out var color);

		await Assert.That(found).IsTrue();
		await Assert.That(color!.First(x => x.name == "antiquewhite4").ansi).IsEqualTo(256);
	}
}
