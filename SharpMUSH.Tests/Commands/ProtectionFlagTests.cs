using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>protection_flags</c>: RhostMUSH's and TinyMUX's NOEXAMINE and NOMODIFY keep everyone below wizard and royalty out,
/// owners included (<c>help compatibility config protection flags</c>).
/// </summary>
public class ProtectionFlagTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _owner = null!;

	[Before(Test)]
	public async Task CreatePlayer()
	{
		_owner = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "Protected");
		foreach (var flag in new[] { "NOEXAMINE", "NOMODIFY" })
		{
			if (await Mediator.Send(new GetObjectFlagQuery(flag)) is null)
			{
				await Mediator.Send(new CreateObjectFlagCommand(flag, null, "", false, ["wizard"], ["wizard"], ["ROOM", "THING", "EXIT", "PLAYER"]));
			}
		}
	}

	[After(Test)]
	public async Task Disconnect() => await ConnectionService.Disconnect(_owner.Handle);

	private Task<string> AsOwner(string command) => CmdAs(_owner.DbRef, _owner.Handle, command);

	private static IDisposable Protecting(bool on)
		=> TestOptionsOverride.Scope(options => options with { Compatibility = options.Compatibility with { ProtectionFlags = on } });

	[Test]
	public async Task NomodifyKeepsEvenTheOwnerOut()
	{
		var thing = DBRef.Parse(await AsOwner($"@create {TestIsolationHelpers.GenerateUniqueName("Ledger")}")).Number;
		await Cmd($"@set #{thing}=NOMODIFY");

		using (Protecting(true))
		{
			await AsOwner($"&NOTE #{thing}=changed");
			await Assert.That(await Eval($"get(#{thing}/NOTE)")).IsEmpty();
			await Cmd($"&NOTE #{thing}=by god");
			await Assert.That(await Eval($"get(#{thing}/NOTE)")).IsEqualTo("by god");
		}

		using (Protecting(false))
		{
			await AsOwner($"&NOTE #{thing}=changed");
			await Assert.That(await Eval($"get(#{thing}/NOTE)")).IsEqualTo("changed");
		}
	}

	[Test]
	public async Task NoexamineKeepsEvenTheOwnerOut()
	{
		var thing = DBRef.Parse(await AsOwner($"@create {TestIsolationHelpers.GenerateUniqueName("Secret")}")).Number;
		await AsOwner($"&HIDDEN #{thing}=shh");
		await Cmd($"@set #{thing}=NOEXAMINE");

		using (Protecting(true))
		{
			await Assert.That(await EvalAs(_owner.DbRef, $"get(#{thing}/HIDDEN)")).StartsWith("#-1");
			await Assert.That(await Eval($"get(#{thing}/HIDDEN)")).IsEqualTo("shh");
		}

		using (Protecting(false))
		{
			await Assert.That(await EvalAs(_owner.DbRef, $"get(#{thing}/HIDDEN)")).IsEqualTo("shh");
		}
	}

	[Test]
	public async Task NomodifyLeavesTheOwnerAbleToExamine()
	{
		var thing = DBRef.Parse(await AsOwner($"@create {TestIsolationHelpers.GenerateUniqueName("Ledger")}")).Number;
		await AsOwner($"&NOTE #{thing}=kept");
		await Cmd($"@set #{thing}=NOMODIFY");

		using (Protecting(true))
		{
			await Assert.That(await EvalAs(_owner.DbRef, $"get(#{thing}/NOTE)")).IsEqualTo("kept");
		}
	}

	[Test]
	public async Task NomodifyKeepsTheObjectFromChangingItself()
	{
		var thing = DBRef.Parse(await AsOwner($"@create {TestIsolationHelpers.GenerateUniqueName("Ledger")}")).Number;
		await Cmd($"@set #{thing}=NOMODIFY");

		using (Protecting(true))
		{
			await EvalAs(new DBRef(thing), "set(me,NOTE:changed)");
			await Assert.That(await Eval($"get(#{thing}/NOTE)")).IsEmpty();
		}

		using (Protecting(false))
		{
			await EvalAs(new DBRef(thing), "set(me,NOTE:changed)");
			await Assert.That(await Eval($"get(#{thing}/NOTE)")).IsEqualTo("changed");
		}
	}

	[Test]
	public async Task NoexamineLeavesTheObjectItsOwnAttributes()
	{
		var thing = DBRef.Parse(await AsOwner($"@create {TestIsolationHelpers.GenerateUniqueName("Secret")}")).Number;
		await AsOwner($"&DATA #{thing}=mine");
		await Cmd($"@set #{thing}=NOEXAMINE");

		using (Protecting(true))
		{
			await Assert.That(await EvalAs(new DBRef(thing), "v(DATA)")).IsEqualTo("mine");
		}
	}

	[Test]
	public async Task TheOptionIsReadFromAConfigurationFile()
	{
		var path = Path.Combine(Path.GetTempPath(), $"sharpmush-protection-{Guid.NewGuid():N}.cnf");
		try
		{
			await File.WriteAllTextAsync(path, "protection_flags yes\n");
			var on = SharpMUSH.Configuration.ReadPennMushConfig.Create(path).Compatibility.ProtectionFlags;
			await File.WriteAllTextAsync(path, string.Empty);
			var off = SharpMUSH.Configuration.ReadPennMushConfig.Create(path).Compatibility.ProtectionFlags;

			await Assert.That(on).IsTrue();
			await Assert.That(off).IsFalse();
		}
		finally
		{
			File.Delete(path);
		}
	}
}
