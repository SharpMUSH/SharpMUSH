using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class ArgumentReviewRegressionTests : ServerTestBase
{
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public async Task ConnectionHookReservedSlotIsVisibleButNotSupplied(bool connect)
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "HookPresence");
		var hook = connect ? "ACONNECT" : "ADISCONNECT";
		var token = TestIsolationHelpers.GenerateUniqueName("presence");
		await Cmd($"&{hook} {actor.DbRef}=@pemit me={token} [hasarg(0)]|[hasarg(0,supplied)]|[hasarg(1)]|[hasarg(1,supplied)]|%1");
		try
		{
			var player = (await Mediator.Send(new GetObjectNodeQuery(actor.DbRef))).Expect<AnySharpObject>();
			var announcements = WebAppFactoryArg.Services.GetRequiredService<IConnectionAnnounceService>();
			if (connect) await announcements.AnnounceConnectAsync(player, 2, false);
			else await announcements.AnnounceDisconnectAsync(player, 2, false);
			await WebAppFactoryArg.QueueBarrierAsync();
			await Assert.That(Notifications.For(actor.DbRef).Where(message => message.StartsWith(token, StringComparison.Ordinal)))
				.IsEquivalentTo([$"{token} 1|0|1|1|2"]);
		}
		finally
		{
			await Cmd($"&{hook} {actor.DbRef}=");
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task LocalAliasListingDisplaysItsTargetsCurrentSignature()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "AliasListing");
		var name = "target" + Guid.NewGuid().ToString("N");
		var alias = "alias" + Guid.NewGuid().ToString("N");
		Task<string> Run(string command) => CmdAs(actor.DbRef, actor.Handle, command);
		await Run($"&BODY me=%0");
		await Run($"@function/local {name}=me,BODY");
		await Run($"@function/local/alias {alias}={name}");
		await Run($"@function/local/args {name}=who count");
		try
		{
			var before = Notifications.CountFor(actor.DbRef);
			await Run("@function/local");
			var listing = Notifications.For(actor.DbRef).Skip(before).ToList();
			var aliasRow = listing.FindIndex(line => line.Contains(alias, StringComparison.OrdinalIgnoreCase));
			await Assert.That(aliasRow).IsGreaterThanOrEqualTo(0);
			await Assert.That(listing[aliasRow + 1]).Contains("who count");
		}
		finally
		{
			await Run($"@function/local/delete {name}");
			await ConnectionService.Disconnect(actor.Handle);
		}
	}
}
