using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Tags;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// RhostMUSH's tags: <c>@tag</c> and <c>@ltag</c> name objects, and <c>tagref()</c> and <c>#tag</c> find them again.
/// </summary>
public class TagTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	private IConnectionService Connections => Factory.Services.GetRequiredService<IConnectionService>();

	private async Task<string> Read(string expression)
		=> (await Factory.FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	/// <summary>
	/// The lines #1 was told while <paramref name="command"/> ran that hold <paramref name="marker"/>: other tests
	/// running alongside tell #1 things too.
	/// </summary>
	private async Task<string[]> Output(string command, string marker)
	{
		var recipient = Factory.ExecutorDBRef;
		var count = Factory.Notifications.CountFor(recipient);
		await Factory.CommandParser.CommandParse(1, Connections, MarkupText.Plain(command));
		return [.. Factory.Notifications.For(recipient).Skip(count).Where(line => line.Contains(marker))];
	}

	private static string UniqueTag() => $"tag{Guid.NewGuid():N}"[..24];

	private async Task<(string Name, string DbRef)> CreateThingAsync()
	{
		var name = $"Tagged{Guid.NewGuid():N}";
		return (name, (await Read($"create({name})")).Split(':')[0]);
	}

	[Test]
	public async Task AGlobalTagNamesItsObjectWhereverADbrefWould()
	{
		var (name, thing) = await CreateThingAsync();
		var tag = UniqueTag();

		await Assert.That(await Output($"@tag/add {tag}={thing}", tag)).Contains(line => line.StartsWith($"Tag {tag} added"));
		await Assert.That(await Read($"tagref({tag})")).IsEqualTo(thing);
		await Assert.That(await Read($"tagref(#{tag.ToUpperInvariant()})")).IsEqualTo(thing);
		await Assert.That(await Read($"name(#{tag})")).IsEqualTo(name);
		await Assert.That(await Read($"[istag(#{tag})] [istag({tag})] [istag(#nosuchtag{Guid.NewGuid():N})]")).IsEqualTo("1 0 0");
		await Assert.That(await Read($"listtags({thing})")).IsEqualTo(tag);
		await Assert.That((await Read("listtags()")).Split(' ')).Contains($"{tag}|{thing}");
		await Assert.That(await Read($"tagmatch({tag[..10]}*)")).Contains(tag);
		await Assert.That(await Output($"@tag {tag}", thing)).Contains(thing);
		await Assert.That(await Output($"@tag/list {tag}", tag)).IsNotEmpty();

		await Assert.That(await Output($"@tag/add {tag}={thing}", tag)).Contains($"#-1 The tag {tag} already exists.");
		await Assert.That(await Output($"@tag/remove {tag}", tag)).Contains($"Tag {tag} removed.");
		await Assert.That(await Read($"tagref({tag})")).IsEqualTo("#-1");
		await Assert.That(await Output($"@tag/remove {tag}", tag)).Contains($"#-1 No tag {tag}.");
	}

	[Test]
	public async Task APersonalTagGivesWayToAGlobalTagOfTheSameName()
	{
		var (_, mine) = await CreateThingAsync();
		var (_, theirs) = await CreateThingAsync();
		var tag = UniqueTag();

		await Assert.That(await Output($"@ltag/add {tag}={mine}", tag)).Contains(line => line.StartsWith($"Tag {tag} added"));
		await Assert.That(await Read($"tagref({tag})")).IsEqualTo(mine);
		await Assert.That(await Output($"@tag/list {tag}", tag)).Contains($"No tags match {tag}.");

		await Output($"@tag/add {tag}={theirs}", tag);
		await Assert.That(await Read($"tagref({tag})")).IsEqualTo(theirs);
		await Assert.That(await Output($"@ltag/list {tag}", tag)).Contains(line => line.Contains($"{tag} (hidden by a global tag)"));

		await Output($"@tag/remove {tag}", tag);
		await Assert.That(await Read($"tagref({tag})")).IsEqualTo(mine);
		await Output($"@ltag/remove {tag}", tag);
		await Assert.That(await Read($"tagref({tag})")).IsEqualTo("#-1");
	}

	[Test]
	public async Task APersonalTagMayShareAGlobalTagsName()
	{
		var (_, mine) = await CreateThingAsync();
		var (_, theirs) = await CreateThingAsync();
		var tag = UniqueTag();

		await Output($"@tag/add {tag}={theirs}", tag);
		await Assert.That(await Output($"@ltag/add {tag}={mine}", tag)).Contains(line => line.StartsWith($"Tag {tag} added"));
		await Assert.That(await Read($"tagref({tag})")).IsEqualTo(theirs);

		await Output($"@tag/remove {tag}", tag);
		await Assert.That(await Read($"tagref({tag})")).IsEqualTo(mine);
		await Output($"@ltag/remove {tag}", tag);
	}

	[Test]
	[Arguments("12345")]
	[Arguments("has space")]
	[Arguments("a#b")]
	[Arguments("abcdefghijklmnopqrstuvwxyz0123456")]
	public async Task NamesADbrefOrAListCouldNotHoldAreRefused(string tag)
	{
		var (_, thing) = await CreateThingAsync();

		await Assert.That(await Output($"@tag/add {tag}={thing}", $"; {tag} is not.")).Contains(line => line.StartsWith("#-1 A tag name"));
	}

	[Test]
	public async Task AnObjectTakesSixteenTagsAndAPersonalOneOnlyBelowFive()
	{
		var (_, thing) = await CreateThingAsync();
		var tags = Enumerable.Range(0, ObjectTagRegistry.MaxPerObject + 1).Select(_ => UniqueTag()).ToArray();

		for (var i = 0; i < ObjectTagRegistry.MaxPersonalPerObject; i++)
		{
			await Output($"@tag/add {tags[i]}={thing}", tags[i]);
		}

		await Assert.That(await Output($"@ltag/add {tags[^1]}={thing}", "too many for a personal one")).Contains(line => line.Contains($"{thing}:"));

		for (var i = ObjectTagRegistry.MaxPersonalPerObject; i < ObjectTagRegistry.MaxPerObject; i++)
		{
			await Output($"@tag/add {tags[i]}={thing}", tags[i]);
		}

		await Assert.That(await Output($"@tag/add {tags[^1]}={thing}", $"already has {ObjectTagRegistry.MaxPerObject} tags")).Contains(line => line.Contains($"{thing}:"));
		await Assert.That((await Read($"listtags({thing})")).Split(' ')).Count().IsEqualTo(ObjectTagRegistry.MaxPerObject);

		foreach (var tag in tags) await Output($"@tag/remove {tag}", tag);
	}
}
