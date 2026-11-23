using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The lines @decompile writes recreate the object: <c>@create</c> gives a thing <c>thing_flags</c>
/// (NO_COMMAND) and <c>&amp;</c> gives an attribute its attribute table's default flags, so a default flag
/// the original no longer has is written as <c>!flag</c>, or the copy gets it back.
/// </summary>
public class DecompileDefaultFlagReplayTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();

	private async Task<string> Read(string expression)
		=> (await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	/// <summary>What #1 was told while <paramref name="command"/> ran, picked out by the prefix only this test uses.</summary>
	private async Task<string[]> Output(string command, string prefix)
	{
		var recipient = Factory.ExecutorDBRef;
		var count = Factory.Notifications.CountFor(recipient);
		await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(command));
		return Factory.Notifications.For(recipient).Skip(count)
			.Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
			.Select(line => line[prefix.Length..])
			.ToArray();
	}

	private Task Run(string command) => Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(command)).AsTask();

	[Test]
	public async Task RemovedDefaultFlagsAreWrittenAndReplayed()
	{
		var original = TestIsolationHelpers.GenerateUniqueName("DecompDefA");
		var copy = TestIsolationHelpers.GenerateUniqueName("DecompDefB");
		await Run($"@create {original}");
		await Run($"@set {original}=!no_command");
		await Run($"&ADESCRIBE {original}=think described");
		await Run($"@set {original}/ADESCRIBE=!no_command");
		await Assert.That(await Read($"hasflag({original},no_command)")).IsEqualTo("0");
		await Assert.That(await Read($"hasflag({original}/ADESCRIBE,no_command)")).IsEqualTo("0");
		await Assert.That(await Read($"hasflag({original}/ADESCRIBE,prefixmatch)")).IsEqualTo("1");

		var prefix = $"P{Guid.NewGuid():N}:";
		var lines = await Output($"@decompile {original}={prefix}", prefix);
		await Assert.That(lines).Contains($"@set {original}=!NO_COMMAND", StringComparer.OrdinalIgnoreCase);
		await Assert.That(lines).Contains($"@set {original}/ADESCRIBE=prefixmatch !no_command", StringComparer.OrdinalIgnoreCase);

		foreach (var line in lines) await Run(line.Replace(original, copy, StringComparison.Ordinal));

		await Assert.That(await Read($"hasflag({copy},no_command)")).IsEqualTo("0");
		await Assert.That(await Read($"hasflag({copy}/ADESCRIBE,no_command)")).IsEqualTo("0");
		await Assert.That(await Read($"hasflag({copy}/ADESCRIBE,prefixmatch)")).IsEqualTo("1");
	}

	/// <summary>An object and attribute that keep their defaults get no <c>!</c> line.</summary>
	[Test]
	public async Task KeptDefaultFlagsWriteNoRemoval()
	{
		var thing = TestIsolationHelpers.GenerateUniqueName("DecompDefKept");
		await Run($"@create {thing}");
		await Run($"&ADESCRIBE {thing}=think described");

		var prefix = $"P{Guid.NewGuid():N}:";
		var lines = await Output($"@decompile {thing}={prefix}", prefix);
		await Assert.That(lines.Any(line => line.Contains('!'))).IsFalse();
		await Assert.That(lines.Any(line => line.StartsWith($"@set {thing}/ADESCRIBE=", StringComparison.OrdinalIgnoreCase))).IsTrue();

		var skipped = await Output($"@decompile/skipdefaults {thing}={prefix}", prefix);
		await Assert.That(skipped.Any(line => line.StartsWith($"@set {thing}/ADESCRIBE=", StringComparison.OrdinalIgnoreCase))).IsFalse();
	}
}
