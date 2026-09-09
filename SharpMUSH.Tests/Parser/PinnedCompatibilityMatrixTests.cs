using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Implementation;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Parser;

public class PinnedCompatibilityMatrixTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	public sealed record OracleCase(string File, int Index, string Expression, string Expected,
		bool TinyBooleans, bool TinyMath, bool NullEqualsZero, bool TinyTrim)
	{
		public override string ToString() => $"{File}:{Index} {Expression}";
	}

	public static IEnumerable<OracleCase> CapturedCases()
	{
		foreach (var name in new[] { "compatibility-penn-95ad3511.json", "numeric-penn-95ad3511.json" })
		{
			var file = Path.Combine(AppContext.BaseDirectory, "OracleFixtures", name);
			using var document = JsonDocument.Parse(File.ReadAllText(file));
			if (document.RootElement.GetProperty("commit").GetString() != "95ad3511d0410b9e132f140fabc4ca7afec433da")
				throw new InvalidDataException($"Unexpected PennMUSH reference in {file}");
			var index = 0;
			foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
			{
				var configuration = item.GetProperty("configuration");
				var expected = item.TryGetProperty("sharpmush_expected", out var sharpExpected)
					? sharpExpected.GetString()! : item.GetProperty("expected").GetString()!;
				yield return new(Path.GetFileName(file), ++index, item.GetProperty("expression").GetString()!, expected,
					configuration.GetProperty("tiny_booleans").GetBoolean(), configuration.GetProperty("tiny_math").GetBoolean(),
					configuration.GetProperty("null_eq_zero").GetBoolean(), configuration.GetProperty("tiny_trim_fun").GetBoolean());
			}
		}
	}

	[Test]
	[MethodDataSource(nameof(CapturedCases))]
	public async Task CapturedBehaviorRemainsExplicit(OracleCase item)
	{
		var baseline = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		options.CurrentValue.Returns(baseline with
		{
			Compatibility = baseline.Compatibility with
			{
				TinyBooleans = item.TinyBooleans,
				TinyMath = item.TinyMath,
				NullEqualsZero = item.NullEqualsZero,
				TinyTrimFun = item.TinyTrim
			}
		});
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var parser = original with { ServiceProvider = new OptionsProvider(original.ServiceProvider, options) };
		var actual = (await parser.FunctionParse(MarkupText.Plain(item.Expression)))!.Message!.ToPlainText();
		await Assert.That(actual).IsEqualTo(item.Expected).Because(item.ToString());
	}

	[Test]
	public async Task TrimOptionChangesLiveArgumentOrderWithoutChangingExplicitDialects()
	{
		var baseline = Factory.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue;
		var options = Substitute.For<IOptionsWrapper<SharpMUSHOptions>>();
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var parser = original with { ServiceProvider = new OptionsProvider(original.ServiceProvider, options) };
		foreach (var tiny in new[] { false, true, false })
		{
			options.CurrentValue.Returns(baseline with { Compatibility = baseline.Compatibility with { TinyTrimFun = tiny } });
			var expression = tiny ? "trim(xxabcxx,l,x)" : "trim(xxabcxx,x,l)";
			await Assert.That((await parser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText()).IsEqualTo("abcxx");
			await Assert.That((await parser.FunctionParse(MarkupText.Plain("trimpenn(xxabcxx,x,r)")))!.Message!.ToPlainText()).IsEqualTo("xxabc");
			await Assert.That((await parser.FunctionParse(MarkupText.Plain("trimtiny(xxabcxx,r,x)")))!.Message!.ToPlainText()).IsEqualTo("xxabc");
		}
	}

	private sealed class OptionsProvider(IServiceProvider inner, IOptionsWrapper<SharpMUSHOptions> options) : IServiceProvider
	{
		public object? GetService(Type serviceType) => serviceType == typeof(IOptionsWrapper<SharpMUSHOptions>)
			? options : inner.GetService(serviceType);
	}
}
