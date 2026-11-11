using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>attribute_eval_flags</c>: TinyMUX's <c>noeval</c> attribute flag keeps an attribute from ever being evaluated, and
/// TinyMUX's and RhostMUSH's <c>noparse</c> matches a <c>$</c>-command against the command as typed
/// (<c>help compatibility config attribute_eval_flags</c>).
/// </summary>
public class AttributeEvalFlagTests : ServerTestBase
{
	private TestIsolationHelpers.TestPlayer _player = null!;
	private int _room;

	[Before(Test)]
	public async Task CreatePlayer()
	{
		_player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, "Typist");
		_room = DBRef.Parse(await Cmd($"@dig {TestIsolationHelpers.GenerateUniqueName("Study")}")).Number;
		await Cmd($"@tel #{_player.DbRef.Number}=#{_room}");
	}

	[After(Test)]
	public async Task Disconnect() => await ConnectionService.Disconnect(_player.Handle);

	private Task<string> AsPlayer(string command) => CmdAs(_player.DbRef, _player.Handle, command);

	private static IDisposable Emulating(bool on)
		=> TestOptionsOverride.Scope(options => options with { Compatibility = options.Compatibility with { AttributeEvalFlags = on } });

	private async Task<int> ThingInTheRoom()
	{
		var thing = DBRef.Parse(await Cmd($"@create {TestIsolationHelpers.GenerateUniqueName("Slate")}")).Number;
		await Cmd($"@tel #{thing}=#{_room}");
		return thing;
	}

	[Test]
	public async Task NoevalAttributeIsTextToU()
	{
		var thing = await ThingInTheRoom();
		await Cmd($"&FN #{thing}=[add(1,2)]");
		await Cmd($"@set #{thing}/FN=noeval");

		using (Emulating(true))
		{
			await Assert.That(await Eval($"u(#{thing}/FN)")).IsEqualTo("[add(1,2)]");
		}

		using (Emulating(false))
		{
			await Assert.That(await Eval($"u(#{thing}/FN)")).IsEqualTo("3");
		}
	}

	[Test]
	public async Task NoevalDescriptionIsShownAsWritten()
	{
		var thing = await ThingInTheRoom();
		var marker = TestIsolationHelpers.GenerateUniqueName("Chalk");
		await Cmd($"@desc #{thing}={marker} [add(1,2)]");
		await Cmd($"@set #{thing}/DESCRIBE=noeval");

		using (Emulating(true))
		{
			await AsPlayer($"look #{thing}");
		}

		using (Emulating(false))
		{
			await AsPlayer($"look #{thing}");
		}

		var heard = Notifications.For(_player.DbRef).Where(message => message.Contains(marker, StringComparison.Ordinal)).ToList();
		await Assert.That(heard).IsEquivalentTo(new[] { $"{marker} [add(1,2)]", $"{marker} 3" });
	}

	/// <summary>A noeval action attribute is found but never queued; the one after it in the queue still runs.</summary>
	[Test]
	public async Task NoevalActionDoesNotRun()
	{
		var thing = await ThingInTheRoom();
		var attributes = WebAppFactoryArg.Services.GetRequiredService<IAttributeService>();
		var target = (await Mediator.Send(new GetObjectNodeQuery(new DBRef(thing)))).Expect<AnySharpObject>();
		await Cmd($"@ause #{thing}=&RAN me=yes");
		await Cmd($"@set #{thing}/AUSE=noeval");
		await Cmd($"&MARK #{thing}=&DONE me=yes");

		using (Emulating(true))
		{
			await AsPlayer($"use #{thing}");
			await Cmd($"@trigger #{thing}/MARK");
			await TestHelpers.WaitForAttribute(attributes, target, "DONE");
		}

		await Assert.That(await Eval($"get(#{thing}/DONE)")).IsEqualTo("yes");
		await Assert.That(await Eval($"hasattr(#{thing},RAN)")).IsEqualTo("0");

		await AsPlayer($"use #{thing}");
		await TestHelpers.WaitForAttribute(attributes, target, "RAN");
		await Assert.That(await Eval($"get(#{thing}/RAN)")).IsEqualTo("yes");
	}

	[Test]
	public async Task NoparseCommandMatchesTheLineAsTyped()
	{
		var thing = await ThingInTheRoom();
		var marker = TestIsolationHelpers.GenerateUniqueName("Echo");
		await Cmd($"@set #{thing}=!no_command");
		await Cmd($"&CMD #{thing}=$+{marker} *:@pemit %#=heard %0");
		await Cmd($"@set #{thing}/CMD=noparse");

		using (Emulating(true))
		{
			await AsPlayer($"+{marker} [add(1,2)]");
			await Notifications.WaitForAsync(_player.DbRef, "heard [add(1,2)]");
		}

		using (Emulating(false))
		{
			await AsPlayer($"+{marker} [add(1,2)]");
			await Notifications.WaitForAsync(_player.DbRef, "heard 3");
		}
	}
	/// <summary>@scan matches the command it is given, so a noparse command is found as any other.</summary>
	[Test]
	public async Task ScanFindsANoparseCommand()
	{
		var thing = await ThingInTheRoom();
		var marker = TestIsolationHelpers.GenerateUniqueName("probe").ToLowerInvariant();
		await Cmd($"@set #{thing}=!no_command");
		await Cmd($"&CMD #{thing}=$+{marker} *:@pemit %#=heard %0");
		await Cmd($"@set #{thing}/CMD=noparse");
		await Cmd($"@tel #{thing}=me");

		using (Emulating(true))
		{
			await Assert.That(await Cmd($"@scan/self +{marker} words")).Contains($"#{thing}/CMD");
		}
	}
}
