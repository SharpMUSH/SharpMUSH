using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@attribute/access/retroactive</c>. PennMUSH's <c>do_attribute_access</c>
/// (<c>src/atr_tab.c:758</c>) writes the new permissions into the attribute table and then, when
/// retroactive, walks every object and gives each one's own copy of the attribute exactly those
/// permissions and the executor as its creator (<c>src/atr_tab.c:816-826</c>). Without
/// <c>/retroactive</c> only the table changes: copies already set keep what they had.
/// </summary>
public class AttributeRetroactiveTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private static string AttributeName() => $"RETRO_{Guid.NewGuid():N}"[..20].ToUpperInvariant();

	private async Task<List<string>> AsGod(string command)
	{
		var god = WebAppFactoryArg.ExecutorDBRef;
		var before = WebAppFactoryArg.Notifications.CountFor(god);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain(command));
		return [.. WebAppFactoryArg.Notifications.For(god).Skip(before)];
	}

	private async Task<SharpAttribute> CopyOn(DBRef obj, string name)
		=> await Mediator.CreateStream(new GetAttributeQuery(obj, [name])).LastAsync();

	private async Task<string[]> FlagsOn(DBRef obj, string name)
		=> [.. (await CopyOn(obj, name)).Flags.Select(f => f.Name.ToUpperInvariant()).Order()];

	private async Task<DBRef> ThingWith(string name, string prefix)
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, prefix);
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{name} {thing}=value"));
		return thing;
	}

	[Test]
	public async ValueTask Retroactive_GivesEveryExistingCopyTheNewPermissions()
	{
		var name = AttributeName();
		var first = await ThingWith(name, "RetroA");
		var second = await ThingWith(name, "RetroB");

		var messages = await AsGod($"@attribute/access/retroactive {name}=no_command");

		await Assert.That(await FlagsOn(first, name)).IsEquivalentTo(["NO_COMMAND"]);
		await Assert.That(await FlagsOn(second, name)).IsEquivalentTo(["NO_COMMAND"]);
		await Assert.That(messages).Contains(string.Format(ErrorMessages.Notifications.AttributeCommandRetroactiveUpdatedFormat, 2, name));
	}

	/// <summary><c>AL_FLAGS(ap2) = flags</c>: the copy's permissions are replaced, not added to.</summary>
	[Test]
	public async ValueTask Retroactive_ReplacesTheCopysPermissionsRatherThanAddingToThem()
	{
		var name = AttributeName();
		var thing = await ThingWith(name, "RetroReplace");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {thing}/{name}=visual"));
		await Assert.That(await FlagsOn(thing, name)).Contains("VISUAL").Because("precondition");

		await AsGod($"@attribute/access/retroactive {name}=no_command");

		await Assert.That(await FlagsOn(thing, name)).IsEquivalentTo(["NO_COMMAND"]);
	}

	/// <summary><c>none</c> is no permissions at all (<c>strcasecmp(perms, "none")</c>), not a flag name.</summary>
	[Test]
	public async ValueTask Retroactive_NoneClearsEveryCopysPermissions()
	{
		var name = AttributeName();
		var thing = await ThingWith(name, "RetroNone");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {thing}/{name}=visual"));

		await AsGod($"@attribute/access/retroactive {name}=none");

		await Assert.That(await FlagsOn(thing, name)).IsEmpty();
	}

	/// <summary><c>if (AF_Root(ap2)) AL_FLAGS(ap2) = flags | AF_ROOT</c>: a copy with branches keeps its <c>branch</c> flag.</summary>
	[Test]
	public async ValueTask Retroactive_KeepsTheBranchFlagOfACopyWithBranches()
	{
		var name = AttributeName();
		var thing = await ThingWith(name, "RetroBranch");
		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{name}`LEAF {thing}=leaf"));
		await Assert.That(await FlagsOn(thing, name)).Contains("BRANCH").Because("precondition");

		await AsGod($"@attribute/access/retroactive {name}=no_command");

		await Assert.That(await FlagsOn(thing, name)).IsEquivalentTo(["BRANCH", "NO_COMMAND"]);
	}

	/// <summary><c>AL_CREATOR(ap2) = player</c>: each copy is now the executor's.</summary>
	[Test]
	public async ValueTask Retroactive_MakesTheExecutorEachCopysCreator()
	{
		var name = AttributeName();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RetroOwner");
		var created = await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain("@create RetroOwned"));
		var thing = DBRef.Parse(created.Message!.ToPlainText());
		await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"&{name} {thing}=value"));
		var ownerBefore = await (await CopyOn(thing, name)).Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerBefore!.Object.DBRef.Number).IsEqualTo(mortal.DbRef.Number).Because("precondition");

		await AsGod($"@attribute/access/retroactive {name}=no_command");

		var ownerAfter = await (await CopyOn(thing, name)).Owner.WithCancellation(CancellationToken.None);
		await Assert.That(ownerAfter!.Object.DBRef.Number).IsEqualTo(WebAppFactoryArg.ExecutorDBRef.Number);
	}

	/// <summary>Without <c>/retroactive</c> only the table changes; the copies already set keep their permissions.</summary>
	[Test]
	public async ValueTask WithoutRetroactive_ExistingCopiesKeepTheirPermissions()
	{
		var name = AttributeName();
		var thing = await ThingWith(name, "RetroFuture");

		await AsGod($"@attribute/access {name}=no_command");

		await Assert.That(await FlagsOn(thing, name)).IsEmpty();
		await Assert.That((await Mediator.Send(new GetAttributeEntryQuery(name)))!.DefaultFlags).IsEquivalentTo(["NO_COMMAND"]);
	}

	/// <summary><c>@attribute/access</c> is for wizards; a mortal's retroactive request changes nothing.</summary>
	[Test]
	public async ValueTask Mortal_CannotRewriteCopies()
	{
		var name = AttributeName();
		var thing = await ThingWith(name, "RetroMortal");
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "RetroMortal");

		await Parser.CommandParse(mortal.Handle, ConnectionService, MarkupText.Plain($"@attribute/access/retroactive {name}=no_command"));

		await Assert.That(await FlagsOn(thing, name)).IsEmpty();
		await Assert.That(await Mediator.Send(new GetAttributeEntryQuery(name))).IsNull();
	}
}
