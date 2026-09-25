using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharpMUSH.Configuration;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Server;
using SharpMUSH.Tests.Services;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// <c>float_precision</c> is the number of decimal places every floating-point result is written
/// with, trailing zeros dropped: PennMUSH's <c>unparse_number</c> (<c>src/unparse.c:251</c>) is
/// <c>snprintf("%.*f", FLOAT_PRECISION)</c> and a strip, and every math function writes through it.
/// Here each function family formatted on its own: the decimal aggregates to a fixed 10 places,
/// <c>double</c> results to 15 significant digits, <c>e()</c> and the vector functions unrounded.
/// </summary>
public class FloatPrecisionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser.FromState(ParserState.RootFor(new DBRef(1)));

	private async Task<string> Evaluate(string code, uint? precision = null)
	{
		using var configuration = TestOptionsOverride.Scope(options => precision is { } places
			? options with { Cosmetic = options.Cosmetic with { FloatPrecision = places } }
			: options);
		return (await Parser.FunctionParse(MarkupText.Plain(code)))!.Message!.ToPlainText();
	}

	[Test]
	[Arguments("fdiv(1,3)", "0.333333")]
	[Arguments("fdiv(2,3)", "0.666667")]
	[Arguments("pi()", "3.141593")]
	[Arguments("e()", "2.718282")]
	[Arguments("sqrt(2)", "1.414214")]
	[Arguments("acos(0)", "1.570796")]
	[Arguments("lmath(fdiv,1 3)", "0.333333")]
	[Arguments("lmath(mean,1 2 2)", "1.666667")]
	[Arguments("vunit(1 1)", "0.707107 0.707107")]
	[Arguments("vmul(0.1234567 1,1 1)", "0.123457 1")]
	[Arguments("lnum(0,1,%b,0.3333333)", "0 0.333333 0.666667 1")]
	public async Task EveryFamilyUsesTheConfiguredPlaces(string code, string expected)
		=> await Assert.That(await Evaluate($"[{code}]", precision: 6)).IsEqualTo(expected);

	/// <summary>
	/// PennMUSH's own suite sets <c>float_precision=10</c> around this case (<c>test/testmath.t:65-67</c>),
	/// which is what shows the setting counts decimal places: 10 of them, not 10 significant digits.
	/// </summary>
	[Test]
	public async Task PennMUSHRootCaseAtTenPlaces()
		=> await Assert.That(await Evaluate("[root(125, 5)]", precision: 10)).IsEqualTo("2.6265278044");

	/// <summary>
	/// The default is six places, which is what PennMUSH's <c>conf.c</c> and the <c>mushcnf.dst</c>
	/// this repository ships both say. It was written down twice as 15, and 15 places of a
	/// <c>double</c> is binary noise as soon as the value leaves the unit interval: the test this
	/// replaces only ever looked at values below 10, where the noise falls off the end, so it stayed
	/// green while <c>power(10.1,2)</c> printed <c>102.009999999999991</c>.
	/// </summary>
	[Test]
	[Arguments("power(10.1,2)", "102.01")]
	[Arguments("fdiv(1,3)", "0.333333")]
	[Arguments("pi()", "3.141593")]
	[Arguments("e()", "2.718282")]
	[Arguments("sqrt(2)", "1.414214")]
	public async Task TheDefaultIsSixPlaces(string code, string expected)
		=> await Assert.That(await Evaluate($"[{code}]")).IsEqualTo(expected);

	/// <summary>
	/// Every place the shipped default is written down has to say the same thing: the code default,
	/// the fallbacks the PennMUSH config importer uses for an option a <c>mush.cnf</c> does not set,
	/// the <c>mushcnf.dst</c> this repository ships, and the precision
	/// <see cref="Configurable.FloatPrecision"/> answers outside an engine's evaluation.
	/// </summary>
	[Test]
	public async Task SixPlacesEverywhereTheDefaultIsWrittenDown()
	{
		var shipped = Path.Combine(TestPaths.RepositoryRoot, "SharpMUSH.Configuration", "mushcnf.dst");

		await Assert.That(OptionsService.Default().Cosmetic.FloatPrecision).IsEqualTo(6u);
		await Assert.That(ReadPennMushConfig.Create(EmptyConfigFile()).Cosmetic.FloatPrecision).IsEqualTo(6u);
		await Assert.That(ReadPennMushConfig.Create(shipped).Cosmetic.FloatPrecision).IsEqualTo(6u);
		await Assert.That(Configurable.DefaultFloatPrecision).IsEqualTo(6u);
		await Assert.That(Configurable.FloatPrecision).IsEqualTo(6);
	}

	/// <summary>
	/// Each engine writes numbers at its own <c>float_precision</c>. The precision was a process-wide
	/// static that every host re-pointed at its own options as it started, so the test process formatted
	/// every number at the precision of whichever host started last, and kept doing so after that host
	/// was gone (#1245). Starting a second engine here is what an import world, the readiness tests or
	/// the telnet tests do alongside the shared host.
	/// </summary>
	[Test]
	public async Task AnotherEngineStartingDoesNotChangeThisOnesPrecision()
	{
		var twoPlaces = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		var configured = ReadPennMushConfig.Create(Path.Join(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));
		twoPlaces.CurrentValue.Returns(configured with { Cosmetic = configured.Cosmetic with { FloatPrecision = 2 } });
		await using var other = await IsolatedImportWorld.CreateAsync(services =>
		{
			services.RemoveAll<IOptionsWrapper<SharpMUSHOptions>>();
			services.AddSingleton(twoPlaces);
		});
		using var startup = ActivatorUtilities.CreateInstance<StartupHandler>(other.Services);
		await startup.StartAsync(CancellationToken.None);

		var otherParser = new MUSHCodeParser(
			other.Services.GetRequiredService<ILogger<MUSHCodeParser>>(),
			other.Services.GetRequiredService<LibraryService<string, FunctionDefinition>>(),
			other.Services.GetRequiredService<LibraryService<string, CommandDefinition>>(),
			twoPlaces,
			other.Services,
			ParserState.RootFor(new DBRef(1)));

		await Assert.That(await Evaluate("[fdiv(1,3)]")).IsEqualTo("0.333333");
		await Assert.That((await otherParser.FunctionParse(MarkupText.Plain("[fdiv(1,3)]")))!.Message!.ToPlainText())
			.IsEqualTo("0.33");
	}

	/// <summary>
	/// The precision follows the options a parser evaluates with, so a copy made with other options
	/// writes at their precision rather than at that of the parser it was copied from.
	/// </summary>
	[Test]
	public async Task ACopyWithOtherOptionsWritesAtTheirPrecision()
	{
		var threePlaces = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		var configured = ReadPennMushConfig.Create(Path.Join(AppContext.BaseDirectory, "Configuration", "Testfile", "mushcnf.dst"));
		threePlaces.CurrentValue.Returns(configured with { Cosmetic = configured.Cosmetic with { FloatPrecision = 3 } });
		var copy = (MUSHCodeParser)Parser with { Configuration = threePlaces };

		await Assert.That((await copy.FunctionParse(MarkupText.Plain("[fdiv(1,3)]")))!.Message!.ToPlainText())
			.IsEqualTo("0.333");
		await Assert.That(await Evaluate("[fdiv(1,3)]")).IsEqualTo("0.333333");
	}

	private static string EmptyConfigFile()
	{
		var path = Path.Combine(Path.GetTempPath(), $"sharpmush-float-precision-{Guid.NewGuid():N}.cnf");
		File.WriteAllText(path, string.Empty);
		return path;
	}

	[Test]
	[Arguments("add(0.1,0.2)", "0.3")]
	[Arguments("fdiv(1,4)", "0.25")]
	[Arguments("fdiv(6,3)", "2")]
	// %.*f never switches to exponent notation.
	[Arguments("power(10,20)", "100000000000000000000")]
	[Arguments("fdiv(1,10000000)", "0")]
	// Rounds to zero from below: written as 0, not -0.
	[Arguments("fdiv(-1,10000000)", "0")]
	public async Task TrailingZerosAreDropped(string code, string expected)
		=> await Assert.That(await Evaluate($"[{code}]", precision: 6)).IsEqualTo(expected);

	/// <summary><c>fun_round</c> caps <c>&lt;places&gt;</c> at <c>FLOAT_PRECISION</c> (<c>src/funmath.c:909</c>).</summary>
	[Test]
	[Arguments("round(pi(),10)", "3.141593")]
	[Arguments("round(pi(),2)", "3.14")]
	[Arguments("round(2.5,3,1)", "2.500")]
	[Arguments("round(pi(),10,1)", "3.141593")]
	public async Task RoundIsCappedAtThePrecision(string code, string expected)
		=> await Assert.That(await Evaluate($"[{code}]", precision: 6)).IsEqualTo(expected);

	/// <summary>
	/// <c>fun_round</c> takes an unsigned place count and answers <c>e_int</c> otherwise
	/// (<c>src/funmath.c:898</c>). A negative count reached <c>Math.Round</c> and threw.
	/// </summary>
	[Test]
	public async Task RoundRefusesNegativePlaces()
		=> await Assert.That(await Evaluate("[round(pi(),-1)]")).IsEqualTo("#-1 ARGUMENT MUST BE INTEGER");
}
