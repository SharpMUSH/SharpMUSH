using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Listings that name an attribute in a tree name it by its whole path (Penn's AL_NAME),
/// FUN`FOOTER`DISPLAY and not DISPLAY, so what they print can be fed back in.
/// </summary>
public class AttributeTreeNameTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();

	private async Task<string> Read(string expression)
		=> (await Factory.FunctionParser.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();

	/// <summary>What #1 was told while <paramref name="command"/> ran; callers pick their lines by a prefix only they use.</summary>
	private async Task<string[]> Output(string command)
	{
		var recipient = Factory.ExecutorDBRef;
		var count = Factory.Notifications.CountFor(recipient);
		await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(command));
		return Factory.Notifications.For(recipient).Skip(count).ToArray();
	}

	private async Task<string> CreateTreeAsync()
	{
		var thing = await Read($"create(TreeNames{Guid.NewGuid():N})");
		await Output($"&FUN {thing}=root");
		await Output($"&FUN`FOOTER {thing}=footer");
		await Output($"&FUN`FOOTER`DISPLAY {thing}=display");
		await Output($"&FUN`FOOTER`DISPLAY`CENTER {thing}=center");
		await Output($"@set {thing}/FUN`FOOTER`DISPLAY`CENTER=funsyntax visual");
		return thing;
	}

	[Test]
	public async Task DecompileNamesTreeAttributesByPath()
	{
		var thing = await CreateTreeAsync();
		var prefix = $"P{Guid.NewGuid():N}:";

		var output = (await Output($"@decompile/db {thing}/FUN`FOOTER**={prefix}"))
			.Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
			.Select(line => line[prefix.Length..])
			.ToArray();

		await Assert.That(output).Contains($"&FUN`FOOTER {thing}=footer");
		await Assert.That(output).Contains($"&FUN`FOOTER`DISPLAY {thing}=display");
		await Assert.That(output).Contains($"&FUN`FOOTER`DISPLAY`CENTER {thing}=center");
		// All flags on one line, as Penn's privs_to_string writes them, and never branch (AF_ROOT).
		await Assert.That(output.Where(line => line.StartsWith($"@set {thing}/FUN`FOOTER`DISPLAY`CENTER=", StringComparison.Ordinal)))
			.HasSingleItem();
		var flagLine = output.Single(line => line.StartsWith($"@set {thing}/FUN`FOOTER`DISPLAY`CENTER=", StringComparison.Ordinal));
		await Assert.That(flagLine.Split('=')[1].Split(' ').Order(StringComparer.OrdinalIgnoreCase))
			.IsEquivalentTo(new[] { "funsyntax", "visual" });
		await Assert.That(output.Any(line => line.Contains("branch", StringComparison.OrdinalIgnoreCase))).IsFalse();
		await Assert.That(output.Any(line => line.StartsWith("&DISPLAY ", StringComparison.Ordinal)
			|| line.StartsWith("&CENTER ", StringComparison.Ordinal))).IsFalse();
	}

	[Test]
	public async Task DecompileWithoutPatternReachesEveryLevel()
	{
		var thing = await CreateTreeAsync();
		var prefix = $"P{Guid.NewGuid():N}:";

		var output = (await Output($"@decompile/db {thing}={prefix}"))
			.Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
			.Select(line => line[prefix.Length..])
			.ToArray();

		await Assert.That(output).Contains($"&FUN {thing}=root");
		await Assert.That(output).Contains($"&FUN`FOOTER`DISPLAY`CENTER {thing}=center");
	}

	[Test]
	public async Task TextsearchMatchesNestedAttributes()
	{
		var thing = await CreateTreeAsync();
		var marker = $"deep{Guid.NewGuid():N}";
		await Output($"&FUN`FOOTER`DISPLAY`CENTER {thing}={marker}");

		var found = (await Read($"textsearch(all,{marker},FOOTER`DISPLAY`CENTER)"))
			.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		await Assert.That(found.Any(objid => objid == thing || objid.StartsWith($"{thing}:", StringComparison.Ordinal))).IsTrue();
	}

	[Test]
	public async Task GrepAndWildgrepNameTreeAttributesByPath()
	{
		var thing = await CreateTreeAsync();

		await Assert.That(await Read($"wildgrep({thing},FUN**,cen*)")).IsEqualTo("FUN`FOOTER`DISPLAY`CENTER");

		var listed = await Output($"@grep/list {thing}/FUN**=cent");
		await Assert.That(listed).Contains("FUN`FOOTER`DISPLAY`CENTER");
	}
}
