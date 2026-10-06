using System.Text;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// In-game staff actions land in the audit log (#1565), custom permissions among them; <c>@boot</c> of
/// someone else's connection needs moderation; and a login records where it came from in <c>LAST</c>,
/// <c>LASTSITE</c> and <c>LASTIP</c>.
/// </summary>
public class StaffAuditTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _wizard = null!;
	private TestIsolationHelpers.TestPlayer _mortal = null!;
	private TestIsolationHelpers.TestPlayer _victim = null!;

	[Before(Test)]
	public async Task CreatePlayers()
	{
		_wizard = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "AuditWiz");
		_mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "AuditMortal");
		_victim = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "AuditVictim");
		var wizard = (await Mediator.Send(new GetObjectNodeQuery(_wizard.DbRef))).Expect<SharpPlayer>();
		await Assert.That(await Mediator.Send(new SetObjectFlagCommand(wizard, (await Mediator.Send(new GetObjectFlagQuery("WIZARD")))!))).IsTrue();
	}

	[After(Test)]
	public async Task Cleanup()
	{
		foreach (var player in new[] { _wizard, _mortal, _victim })
			await ConnectionService.Disconnect(player.Handle);
	}

	private async Task<string> Heard(TestIsolationHelpers.TestPlayer who, string command)
	{
		var before = WebAppFactoryArg.Notifications.CountFor(who.DbRef);
		await CmdAs(who.DbRef, who.Handle, command);
		return string.Join("\n", WebAppFactoryArg.Notifications.For(who.DbRef).Skip(before));
	}

	private async Task<string> Objid(DBRef dbref)
		=> (await Mediator.Send(new GetObjectNodeQuery(dbref))).Expect<SharpPlayer>().Object.DBRef.ToString();

	private async Task<IReadOnlyList<AuditEntry>> AuditAbout(DBRef target)
		=> (await Mediator.Send(new GetAuditEntriesQuery(new AuditFilter(Text: await Objid(target))))).Entries;

	[Test]
	public async Task AMortalMayNotBootSomeoneElse()
	{
		var heard = await Heard(_mortal, $"@boot/port {_victim.Handle}");

		await Assert.That(heard).Contains("Permission denied.");
		await Assert.That(ConnectionService.Get(_victim.Handle)?.Ref?.Number).IsEqualTo(_victim.DbRef.Number);
		await Assert.That(await AuditAbout(_victim.DbRef)).IsEmpty();
	}

	[Test]
	public async Task AWizardsBootIsRecorded()
	{
		await Heard(_wizard, $"@boot/port {_victim.Handle}");

		await Assert.That(ConnectionService.Get(_victim.Handle)?.Ref).IsNull();
		var entry = (await AuditAbout(_victim.DbRef)).Single(e => e.Action == AuditActions.PlayerBoot);
		await Assert.That(entry.Source).IsEqualTo(AuditSource.Game);
		await Assert.That(entry.Actor.Objid).IsEqualTo(await Objid(_wizard.DbRef));
		await Assert.That(entry.Target!.Kind).IsEqualTo(AuditTargetKinds.Character);
		await Assert.That(entry.Target.Name).IsEqualTo(_victim.Name);
	}

	[Test]
	public async Task APasswordResetIsRecordedWithoutThePassword()
	{
		await Heard(_wizard, $"@newpassword #{_victim.DbRef.Number}=AuditFresh1");

		var entry = (await AuditAbout(_victim.DbRef)).Single(e => e.Action == AuditActions.PlayerPassword);
		await Assert.That(entry.Details ?? string.Empty).DoesNotContain("AuditFresh1");
	}

	[Test]
	public async Task DefiningAndRemovingAPermissionIsRecorded()
	{
		var scope = $"test{Guid.NewGuid().ToString("N")[..10]}.audit";
		try
		{
			await Assert.That(await Heard(_wizard, $"@permission/define {scope}=Staff/Audited")).Contains($"Permission {scope} defined");
			await Heard(_wizard, $"@permission/undefine {scope}");

			var entries = (await Mediator.Send(new GetAuditEntriesQuery(new AuditFilter(Text: scope)))).Entries;
			await Assert.That(entries.Select(e => e.Action))
				.IsEquivalentTo([AuditActions.PermissionRemove, AuditActions.PermissionDefine], TUnit.Assertions.Enums.CollectionOrdering.Matching);
			await Assert.That(entries.All(e => e.Target is { Kind: AuditTargetKinds.Permission } target && target.Id == scope)).IsTrue();
			await Assert.That(entries[1].Details).IsEqualTo("Staff: Audited");
			await Assert.That(entries[1].Actor.Objid).IsEqualTo(await Objid(_wizard.DbRef));
		}
		finally
		{
			await Heard(_wizard, $"@permission/undefine {scope}");
		}
	}

	[Test]
	public async Task ALoginRecordsWhereItCameFrom_AndTheNextOneIsToldOfIt()
	{
		var player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "LastSite");
		var name = (await Mediator.Send(new GetObjectNodeQuery(player))).Expect<SharpPlayer>().Object.Name;
		var first = await ConnectFromAsync(name, "198.51.100.7", "first.example");
		var second = 0L;
		try
		{
			await Assert.That(await EvalAs(new DBRef(1), $"get(#{player.Number}/LASTIP)")).IsEqualTo("198.51.100.7");
			await Assert.That(await EvalAs(new DBRef(1), $"get(#{player.Number}/LASTSITE)")).IsEqualTo("first.example");
			await Assert.That(await EvalAs(new DBRef(1), $"get(#{player.Number}/LAST)")).IsNotEmpty();
			await Assert.That(WebAppFactoryArg.Notifications.For(player).Any(line => line.Contains("Last connect was from")))
				.IsFalse().Because("a first login has no last connect to report");

			second = await ConnectFromAsync(name, "198.51.100.8", "second.example");

			await Assert.That(WebAppFactoryArg.Notifications.For(player).Any(line => line.Contains("Last connect was from first.example on ")))
				.IsTrue();
			await Assert.That(await EvalAs(new DBRef(1), $"get(#{player.Number}/LASTSITE)")).IsEqualTo("second.example");
		}
		finally
		{
			await ConnectionService.Disconnect(first);
			if (second != 0) await ConnectionService.Disconnect(second);
		}
	}

	private async Task<long> ConnectFromAsync(string name, string ip, string host)
	{
		var handle = TestIsolationHelpers.GenerateUniqueHandle();
		await ConnectionService.Register(handle, ip, host, "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		await CommandParser.CommandParse(handle, ConnectionService, MarkupText.Plain($"connect \"{name}\" TestPassword123"));
		await Assert.That(ConnectionService.Get(handle)?.Ref).IsNotNull().Because("the login must succeed");
		return handle;
	}
}
