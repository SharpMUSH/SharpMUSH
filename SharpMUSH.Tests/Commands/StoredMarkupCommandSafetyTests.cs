using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Database.Lightning;
using SharpMUSH.Database.Lightning.Store;
using SharpMUSH.Library;
using SharpMUSH.Library.Plugins.Storage.Lightning;
using SharpMUSH.Library.Services;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class StoredMarkupCommandSafetyTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }
	private T Get<T>() where T : notnull => Factory.Services.GetRequiredService<T>();
	private const string Future = "{ \"v\":999,\"t\":\"foo future\",\"extra\":42 }";

	private async Task<CallState> Command(string command)
	{
		using var budget = new ExecutionBudget(TimeSpan.FromSeconds(30));
		using var scope = budget.Enter();
		return await Factory.CommandParser.CommandParse(1, Get<IConnectionService>(), MarkupText.Plain(command));
	}

	private async Task<DBRef> Thing()
		=> DBRef.Parse((await Command("@create markup-safety-" + Guid.NewGuid().ToString("N"))).Message.ToPlainText());

	private async Task Seed(DBRef target)
	{
		var god = (await Get<IMediator>().Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var stored = StoredMarkup.Inspect(Future);
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["SAFETY_FUTURE"], stored.Value, god, StoredValue: stored));
		await Command($"&SAFETY_PLAIN {target}=foo plain");
	}

	private async Task<SharpAttribute> Attribute(DBRef target, string name)
		=> await Get<IMediator>().CreateStream(new GetAttributeQuery(target, name.Split('`'))).LastAsync();

	[Test]
	public async Task CorruptAccessMetadataReturnsOneWriteRefusalAndNoStartupExecution()
	{
		var target = await Thing();
		await Command($"&STARTUP {target}=&SHOULD_NOT_RUN me=yes");
		var database = (LightningDatabase)Get<IAttributeStore>();
		await database.Store.WriteAsync(tx => tx.Put(Tables.AttrMeta, Keys.Attr(target.Number, "STARTUP"), Keys.Str("not JSON")));
		var god = (await Get<IMediator>().Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var obj = (await Get<IMediator>().Send(new GetObjectNodeQuery(target))).Expect<AnySharpObject>();
		await Assert.That(await Get<IAttributeService>().SetAttributeAsync(god, obj, "STARTUP", MarkupText.Plain("replacement")) is Error<string>).IsTrue();
		await Assert.That(await StartupAttributeRunner.RunObjectAttributeAsync(Factory.CommandParser, Get<IAttributeService>(), obj, "STARTUP", god)).IsFalse();
		await Assert.That(await Get<IMediator>().CreateStream(new GetAttributeQuery(target, ["SHOULD_NOT_RUN"])).AnyAsync()).IsFalse();
	}

	[Test]
	[Arguments("safe", false)]
	[Arguments("wizard", false)]
	[Arguments("internal", false)]
	[Arguments("locked", false)]
	[Arguments("wizard", true)]
	public async Task CorruptValueCannotHideProtectedMetadata(string flagName, bool ancestor)
	{
		var mediator = Get<IMediator>();
		var owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(Factory.Services, mediator, Get<IConnectionService>(), "CorruptProtected");
		var actor = (await mediator.Send(new GetObjectNodeQuery(owner.DbRef))).Expect<AnySharpObject>();
		var god = (await mediator.Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var path = ancestor ? new[] { "PROTECTED", "CHILD" } : new[] { "PROTECTED" };
		await mediator.Send(new SetAttributeCommand(owner.DbRef, path, MarkupText.Plain("original"), god));
		var root = await mediator.CreateStream(new GetAttributeQuery(owner.DbRef, ["PROTECTED"])).LastAsync();
		var flag = await mediator.CreateStream(new GetAttributeFlagsQuery()).FirstAsync(f => f.Name.Equals(flagName, StringComparison.OrdinalIgnoreCase));
		await mediator.Send(new SetAttributeFlagCommand(owner.DbRef, root, flag));
		var service = Get<IAttributeService>();
		await Assert.That(await service.SetAttributeAsync(actor, actor, string.Join('`', path), MarkupText.Plain("replacement")) is Error<string>).IsTrue();
		await ((LightningDatabase)Get<IAttributeStore>()).Store.WriteAsync(tx => tx.Put(Tables.AttrVal, Keys.Attr(owner.DbRef.Number, "PROTECTED"), Keys.Str("not JSON")));
		// Expire the cached read without replacing the corrupt body or any of its metadata.
		await mediator.Send(new SetAttributeFlagCommand(owner.DbRef, root, flag));
		await Assert.That(await service.SetAttributeAsync(actor, actor, string.Join('`', path), MarkupText.Plain("replacement")) is Error<string>).IsTrue();
		await Assert.That(await service.SetAttributeAsync(god, actor, string.Join('`', path), MarkupText.Plain("authorized replacement")) is Success).IsTrue();
		await Assert.That((await Attribute(owner.DbRef, string.Join('`', path))).Value.ToPlainText()).IsEqualTo("authorized replacement");
	}

	[Test]
	[Arguments(false)]
	[Arguments(true)]
	public async Task CommandAndListenDiscoverySkipMalformedNeighbours(bool metadata)
	{
		var target = await Thing();
		await Command($"&DISC_A {target}=$first:think first");
		await Command($"&DISC_BAD {target}=broken");
		await Command($"&DISC_BAD`HIDDEN_CMD {target}=$hidden:think hidden");
		await Command($"&DISC_BAD`HIDDEN_LISTEN {target}=^hidden:think hidden");
		await Command($"&DISC_Z {target}=$last:think last");
		await Command($"&DISC_LISTEN {target}=^last:think heard");
		var database = (LightningDatabase)Get<IAttributeStore>();
		await database.Store.WriteAsync(tx => tx.Put(metadata ? Tables.AttrMeta : Tables.AttrVal, Keys.Attr(target.Number, "DISC_BAD"), Keys.Str("not JSON")));
		var obj = (await Get<IMediator>().Send(new GetObjectNodeQuery(target))).Expect<AnySharpObject>();
		var commands = await Get<IMediator>().Send(new GetCommandAttributesQuery(obj));
		await Assert.That(commands.Any(c => c.Attribute.Name == "DISC_Z")).IsTrue();
		await Assert.That(commands.Any(c => c.Attribute.LongName.StartsWith("DISC_BAD`", StringComparison.Ordinal))).IsFalse();
		var listens = await Get<IMediator>().Send(new GetListenAttributesQuery(obj));
		await Assert.That(listens.Any(c => c.Attribute.Name == "DISC_LISTEN")).IsTrue();
		await Assert.That(listens.Any(c => c.Attribute.LongName.StartsWith("DISC_BAD`", StringComparison.Ordinal))).IsFalse();
	}

	[Test]
	public async Task SemaphoreTimeoutRefusesUnsafeCounterAndCanRetryAfterExplicitReplacement()
	{
		const string raw = "{\"v\":999,\"t\":\"1\",\"futureCounter\":42}";
		var target = await Thing();
		await Command($"@wait {target}/SEMAPHORE=think waiting");
		var scheduler = Get<SharpMUSH.Library.Services.TaskScheduler>();
		var entry = await scheduler.GetSemaphoreTasks(new DbRefAttribute(target, ["SEMAPHORE"])).SingleAsync();
		var god = (await Get<IMediator>().Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var stored = StoredMarkup.Inspect(raw);
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["SEMAPHORE"], stored.Value, god, StoredValue: stored));
		try
		{
			try { await scheduler.ReleaseScheduledWork(entry.Pid, semaphoreTimeout: true); }
			catch (InvalidOperationException error)
			{ await Assert.That(error.Message).IsEqualTo(SemaphoreAttributes.UnsafeMarkupMessage); }
			await Assert.That((await Attribute(target, "SEMAPHORE")).StoredValue!.Serialized).IsEqualTo(raw);
			await Command($"&SEMAPHORE {target}=1");
			await scheduler.ReleaseScheduledWork(entry.Pid, semaphoreTimeout: true);
			await Assert.That((await Attribute(target, "SEMAPHORE")).Value.ToPlainText()).IsEqualTo("0");
		}
		finally { await Command($"@drain {target}/SEMAPHORE"); }
	}

	[Test]
	[Arguments("wait")]
	[Arguments("notify")]
	[Arguments("drain")]
	public async Task SemaphoreMutationsPreserveUnsafeNumericStoredValue(string command)
	{
		const string raw = "{\"v\":999,\"t\":\"1\",\"futureCounter\":42}";
		var target = await Thing();
		await Command($"@wait {target}/SEMAPHORE=think waiting");
		var god = (await Get<IMediator>().Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<SharpPlayer>();
		var stored = StoredMarkup.Inspect(raw);
		await Get<IMediator>().Send(new SetAttributeCommand(target, ["SEMAPHORE"], stored.Value, god, StoredValue: stored));
		try
		{
			await Command(command == "wait" ? $"@wait {target}/SEMAPHORE=think refused" : $"@{command} {target}/SEMAPHORE");
			await Assert.That((await Attribute(target, "SEMAPHORE")).StoredValue!.Serialized).IsEqualTo(raw);
		}
		finally
		{
			await Command($"&SEMAPHORE {target}=1");
			await Command($"@drain {target}/SEMAPHORE");
		}
	}

	[Test]
	public async Task MalformedStartupIsSkippedAndFollowingStartupStillRuns()
	{
		var bad = await Thing();
		var good = await Thing();
		await Command($"&STARTUP {bad}=bad placeholder");
		await Command($"&STARTUP {good}=&BOOT_DONE me=yes");
		var database = (LightningDatabase)Get<IAttributeStore>();
		await database.Store.WriteAsync(tx => tx.Put(Tables.AttrVal, Keys.Attr(bad.Number, "STARTUP"), Keys.Str("not JSON")));
		var god = (await Get<IMediator>().Send(new GetObjectNodeQuery(new DBRef(1)))).Expect<AnySharpObject>();
		var broken = (await Get<IMediator>().Send(new GetObjectNodeQuery(bad))).Expect<AnySharpObject>();
		var following = (await Get<IMediator>().Send(new GetObjectNodeQuery(good))).Expect<AnySharpObject>();
		await Assert.That(await StartupAttributeRunner.RunObjectAttributeAsync(Factory.CommandParser, Get<IAttributeService>(), broken, "STARTUP", god)).IsFalse();
		await Assert.That(await StartupAttributeRunner.RunObjectAttributeAsync(Factory.CommandParser, Get<IAttributeService>(), following, "STARTUP", god)).IsTrue();
		await Assert.That((await Attribute(good, "BOOT_DONE")).Value.ToPlainText()).IsEqualTo("yes");
	}

	[Test]
	public async Task WildcardEditRefusesAllMatchesBeforeChangingAnyAttribute()
	{
		var target = await Thing();
		await Seed(target);
		var result = await Command($"@edit {target}/SAFETY_*=foo,bar");
		await Assert.That(result.Message.ToPlainText().Contains("cannot be edited safely")).IsTrue();
		await Assert.That((await Attribute(target, "SAFETY_PLAIN")).Value.ToPlainText()).IsEqualTo("foo plain");
		await Assert.That((await Attribute(target, "SAFETY_FUTURE")).StoredValue!.Serialized).IsEqualTo(Future);
		await Command($"&SAFETY_FUTURE {target}=replacement");
		await Assert.That((await Attribute(target, "SAFETY_FUTURE")).Value.ToPlainText()).IsEqualTo("replacement");
	}

	[Test]
	public async Task CopyMoveAndClonePreserveOriginalSerialization()
	{
		var source = await Thing();
		var target = await Thing();
		await Seed(source);
		await Command($"@cpattr {source}/SAFETY_FUTURE={target}/COPIED");
		await Assert.That((await Attribute(target, "COPIED")).StoredValue!.Serialized).IsEqualTo(Future);
		await Command($"@mvattr {source}/SAFETY_FUTURE={target}/MOVED");
		await Assert.That((await Attribute(target, "MOVED")).StoredValue!.Serialized).IsEqualTo(Future);
		await Assert.That(await Get<IMediator>().CreateStream(new GetAttributeQuery(source, ["SAFETY_FUTURE"])).AnyAsync()).IsFalse();
		var clone = DBRef.Parse((await Command($"@clone {target}")).Message.ToPlainText());
		await Assert.That((await Attribute(clone, "COPIED")).StoredValue!.Serialized).IsEqualTo(Future);
		await Assert.That((await Attribute(clone, "MOVED")).StoredValue!.Serialized).IsEqualTo(Future);
	}
}
