using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The RhostMUSH compatibility options, each off and on. Expected values with an option on are a live
/// RhostMUSH 5.11.2's answers. Options are scoped to the test's own async flow
/// (<see cref="TestOptionsOverride"/>), so the tests running beside these see the defaults.
/// </summary>
public class RhostCompatibilityOptionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private async Task<string> EvaluateAsync(string code, Func<CompatibilityOptions, CompatibilityOptions> options)
	{
		using var scope = TestOptionsOverride.Scope(configured => configured with { Compatibility = options(configured.Compatibility) });
		return (await Parser.EvaluateAsync(MarkupText.Plain(code))).ToPlainText();
	}

	[Test]
	[Arguments("edit(abcabc,b,X,1)", "aXcaXc", "aXcabc")]
	[Arguments("edit(abcabc,b,X,1x)", "aXcaXc", "aXcabc")]
	[Arguments("edit(abcabc,b,X,-1)", "aXcaXc", "aXcabc")]
	[Arguments("edit(abcabc,b,X,0)", "aXcaXc", "aXcaXc")]
	[Arguments("edit(abcabc,b,X,c,Y)", "aXYaXY", "aXcaXc")]
	[Arguments("edit(abcabc,b,X,,2)", "2a2X2c2a2X2c2", "aXcaXc")]
	[Arguments("edit(abcabc,$,X,1)", "abcabcX", "abcabcX")]
	[Arguments("edit(abcabc,b,X)", "aXcaXc", "aXcaXc")]
	[Arguments("edit(abcabc,b,X,c,Y,a,Z)", "ZXYZXY", "#-1 FUNCTION (EDIT) EXPECTS AT MOST 5 ARGUMENTS BUT GOT 7")]
	public async Task RhostEdit(string code, string off, string on)
	{
		await Assert.That(await EvaluateAsync(code, options => options with { RhostEdit = false })).IsEqualTo(off);
		await Assert.That(await EvaluateAsync(code, options => options with { RhostEdit = true })).IsEqualTo(on);
	}

	[Test]
	public async Task RhostEditKeepsTheMarkupAroundTheEdit()
	{
		var result = await EvaluateAsync("edit(ab[ansi(r,c)]abc,c,X,1)", options => options with { RhostEdit = true });
		await Assert.That(result).IsEqualTo("abXabc");
	}

	[Test]
	[Arguments("map(test/MAP_ARGS,a b)", "<a|1|> <b|2|>", "<a||> <b||>")]
	[Arguments("map(test/MAP_ARGS,a b,,,x)", "<a|x|> <b|x|>", "<a|x|> <b|x|>")]
	[Arguments(@"map(#lambda/<\%0|\%1>,a b)", "<a|1> <b|2>", "<a|> <b|>")]
	public async Task RhostMapArgs(string code, string off, string on)
	{
		var thing = await CreateThingAsync("RhostMapArgs");
		await CommandAsync($"&MAP_ARGS {thing}=<%0|%1|%2>");
		code = code.Replace("test", thing.ToString());

		await Assert.That(await EvaluateAsync(code, options => options with { RhostMapArgs = false })).IsEqualTo(off);
		await Assert.That(await EvaluateAsync(code, options => options with { RhostMapArgs = true })).IsEqualTo(on);
	}

	[Test]
	public async Task RhostLattrOffKeepsTheDelimiterAndRefusesMore()
	{
		var thing = await LattrThingAsync();

		await Assert.That(await EvaluateAsync($"lattr({thing}/V*,|)", options => options with { RhostLattr = false }))
			.IsEqualTo("VA|VB|VC");
		await Assert.That(await EvaluateAsync($"lattr({thing}/V*,,$)", options => options with { RhostLattr = false }))
			.IsEqualTo("#-1 FUNCTION (LATTR) EXPECTS AT MOST 2 ARGUMENTS BUT GOT 3");
	}

	[Test]
	[Arguments("lattr(test/V*,|)", "VA VB VC")]
	[Arguments("lattr(test/V*,,$)", "VA")]
	[Arguments("lattr(test/V*,,^)", "VB")]
	[Arguments("lattr(test/V*,,>)", "VC")]
	[Arguments("lattr(test/V*,,<)", "VA")]
	[Arguments("lattr(test/V*,,l)", "3 1")]
	[Arguments("lattr(test/V*,,1)", "VA VB VC")]
	[Arguments("lattr(test/V*,,2)", "")]
	[Arguments("lattr(test/V*,,~2-2)", "VB VC")]
	[Arguments("lattr(test/V*,,~0-2)", "VA")]
	[Arguments("lattr(test/V*,,|,m)", "VC")]
	[Arguments("lattr(test/V*,,&,)", "")]
	[Arguments("lattr(test/V*,,+)", "")]
	[Arguments("lattr(test/^V.$,,,,1)", "VA VB VC")]
	[Arguments("lattr(test/T*)", "TREE TREE`LEAF")]
	[Arguments("lattr(test/T*,,,,,1)", "TREE")]
	public async Task RhostLattrLayout(string code, string expected)
	{
		var thing = await LattrThingAsync();
		await Assert.That(await EvaluateAsync(code.Replace("test", thing.ToString()), options => options with { RhostLattr = true }))
			.IsEqualTo(expected);
	}

	[Test]
	[Arguments("printf(|$-5s|,abc)", "#-1 PRINTF ARGUMENT COUNT MISMATCH", "|abc  |")]
	[Arguments("printf(|%%-5s|,abc)", "|abc  |", "|%-5s|")]
	[Arguments("printf(<$&-3s>,a%rb)", "#-1 PRINTF ARGUMENT COUNT MISMATCH", "<a  >\n b  ")]
	[Arguments("printf(abc)", "abc", "#-1 FUNCTION (PRINTF) EXPECTS AT LEAST 2 ARGUMENTS BUT GOT 1")]
	public async Task RhostPrintf(string code, string off, string on)
	{
		await Assert.That(await EvaluateAsync(code, options => options with { RhostPrintf = false })).IsEqualTo(off);
		await Assert.That((await EvaluateAsync(code, options => options with { RhostPrintf = true })).Replace("\r\n", "\n"))
			.IsEqualTo(on);
	}

	/// <summary>A configuration file sets the options by their names, and leaves them off when it does not name them.</summary>
	[Test]
	public async Task OptionsAreReadFromAConfigurationFile()
	{
		var path = Path.Combine(Path.GetTempPath(), $"sharpmush-rhost-options-{Guid.NewGuid():N}.cnf");
		try
		{
			await File.WriteAllTextAsync(path, "rhost_edit yes\nrhost_lattr yes\nrhost_printf yes\nrhost_map_args yes\n");
			var on = SharpMUSH.Configuration.ReadPennMushConfig.Create(path).Compatibility;
			await File.WriteAllTextAsync(path, string.Empty);
			var off = SharpMUSH.Configuration.ReadPennMushConfig.Create(path).Compatibility;

			await Assert.That(new[] { on.RhostEdit, on.RhostLattr, on.RhostPrintf, on.RhostMapArgs })
				.IsEquivalentTo(new[] { true, true, true, true });
			await Assert.That(new[] { off.RhostEdit, off.RhostLattr, off.RhostPrintf, off.RhostMapArgs })
				.IsEquivalentTo(new[] { false, false, false, false });
		}
		finally
		{
			File.Delete(path);
		}
	}

	private async Task<DBRef> LattrThingAsync()
	{
		var thing = await CreateThingAsync("RhostLattr");
		await CommandAsync($"&VA {thing}=$whee:think hi");
		await CommandAsync($"&VB {thing}=^*wow*:think x");
		await CommandAsync($"&VC {thing}=plain");
		await CommandAsync($"@set {thing}/VC=mortal_dark");
		await CommandAsync($"&TREE {thing}=branch");
		await CommandAsync($"&TREE`LEAF {thing}=leaf");
		return thing;
	}

	private async Task<DBRef> CreateThingAsync(string name)
	{
		var result = await TestIsolationHelpers.CreateObjectCommandAsync(CommandParser, ConnectionService,
			TestIsolationHelpers.GenerateUniqueName(name));
		return DBRef.Parse(result.Message.ToPlainText().Trim());
	}

	private ValueTask<CallState> CommandAsync(string command)
		=> CommandParser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
}
