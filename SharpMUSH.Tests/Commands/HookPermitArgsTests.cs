using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Requests;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// The hook types SharpMUSH takes from TinyMUX: /permit, /fail and /args, and /extend naming no object (igswitch);
/// and /ignore handing the line to $-commands. Each hook is on @CPATTR and answers only for a line carrying the
/// test's own token, so a test elsewhere that copies an attribute is let through unchanged.
/// </summary>
[NotInParallel]
public class HookPermitArgsTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.Services.GetRequiredService<IMUSHCodeParser>();
	private IHookService HookService => WebAppFactoryArg.Services.GetRequiredService<IHookService>();
	private ISharpDatabase Database => WebAppFactoryArg.Services.GetRequiredService<ISharpDatabase>();

	private async Task RunQueued(string list)
	{
		await WebAppFactoryArg.Services.GetRequiredService<IMediator>().Send(new AdmitCommandListRequest(
			MString.Plain(list),
			WebAppFactoryArg.CommandParserFor(new DBRef(1), 1).CurrentState,
			new DbRefAttribute(new DBRef(1), ["QUEUED_TEST"]),
			-1));
		await WebAppFactoryArg.Services.GetRequiredService<ITaskScheduler>().DrainImmediateQueueForTests();
	}

	private async Task<string> ReadAttributeAsync(DBRef obj, string attribute) =>
		(await Database.GetAttributeAsync(obj, [attribute], CancellationToken.None).LastOrDefaultAsync())
			?.Value.ToPlainText() ?? "";

	private async Task<(DBRef Obj, string Token)> FixtureAsync(string prefix)
	{
		var obj = await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, prefix);
		var token = $"T{Guid.NewGuid():N}"[..12].ToUpperInvariant();
		await RunQueued($"&SRC{token} {obj}=copied");
		return (obj, token);
	}

	private async Task ClearAsync(params string[] types)
	{
		foreach (var type in types)
		{
			await HookService.ClearHookAsync("@CPATTR", type);
		}
	}

	[Test]
	public async ValueTask Permit_False_RefusesTheCommand()
	{
		var (obj, token) = await FixtureAsync("HookPermitNo");
		try
		{
			await RunQueued($"&P_CPATTR {obj}=[lit(not(strmatch(%<ARGS>,*{token}*)))]");
			await RunQueued($"@hook/permit @cpattr={obj},P_CPATTR");

			await RunQueued($"@cpattr {obj}/SRC{token}={obj}/DST{token}");

			await Assert.That(await ReadAttributeAsync(obj, $"DST{token}")).IsEqualTo("")
				.Because("a false /permit answer refuses the command before it runs");
		}
		finally
		{
			await ClearAsync("PERMIT");
		}
	}

	[Test]
	public async ValueTask Permit_True_LetsTheCommandRun()
	{
		var (obj, token) = await FixtureAsync("HookPermitYes");
		try
		{
			await RunQueued($"&P_CPATTR {obj}=1");
			await RunQueued($"@hook/permit @cpattr={obj},P_CPATTR");

			await RunQueued($"@cpattr {obj}/SRC{token}={obj}/DST{token}");

			await Assert.That(await ReadAttributeAsync(obj, $"DST{token}")).IsEqualTo("copied");
		}
		finally
		{
			await ClearAsync("PERMIT");
		}
	}

	[Test]
	public async ValueTask Permit_EmptyAttribute_HasNoSay()
	{
		var (obj, token) = await FixtureAsync("HookPermitGone");
		try
		{
			await RunQueued($"&P_CPATTR {obj}=1");
			await RunQueued($"@hook/permit @cpattr={obj},P_CPATTR");
			await RunQueued($"&P_CPATTR {obj}=");

			await RunQueued($"@cpattr {obj}/SRC{token}={obj}/DST{token}");

			await Assert.That(await ReadAttributeAsync(obj, $"DST{token}")).IsEqualTo("copied")
				.Because("TinyMUX lets a command through when its permit hook has no code");
		}
		finally
		{
			await ClearAsync("PERMIT");
		}
	}

	[Test]
	public async ValueTask Fail_RunsInPlaceOfTheRefusal()
	{
		var (obj, token) = await FixtureAsync("HookFail");
		try
		{
			await RunQueued($"&P_CPATTR {obj}=[lit(not(strmatch(%<ARGS>,*{token}*)))]");
			await RunQueued($"&AF_CPATTR {obj}=[lit(if(strmatch(%<ARGS>,*{token}*),set(me,FAILED{token}:%#)))]");
			await RunQueued($"@hook/permit @cpattr={obj},P_CPATTR");
			await RunQueued($"@hook/fail @cpattr={obj},AF_CPATTR");

			await RunQueued($"@cpattr {obj}/SRC{token}={obj}/DST{token}");

			await Assert.That(await ReadAttributeAsync(obj, $"DST{token}")).IsEqualTo("");
			await Assert.That(await ReadAttributeAsync(obj, $"FAILED{token}")).IsEqualTo("#1")
				.Because("the /fail hook runs when /permit refuses, with the refused player as %#");
		}
		finally
		{
			await ClearAsync("PERMIT", "FAIL");
		}
	}

	[Test]
	public async ValueTask Args_RewritesEachArgument()
	{
		var (obj, token) = await FixtureAsync("HookArgs");
		try
		{
			// Unchanged unless the line is this test's: %0 is the left side, %2 the list item, %3 its position.
			await RunQueued($"&R_CPATTR {obj}=[lit(if(strmatch(%<ARGS>,*{token}*),switch(%3,0,%0,%2X%3),switch(%3,0,%0,1,%1,%2)))]");
			await RunQueued($"@hook/args @cpattr={obj},R_CPATTR");

			await RunQueued($"@cpattr {obj}/SRC{token}={obj}/DST{token},{obj}/OTHER{token}");

			await Assert.That(await ReadAttributeAsync(obj, $"DST{token}X2")).IsEqualTo("copied");
			await Assert.That(await ReadAttributeAsync(obj, $"OTHER{token}X3")).IsEqualTo("copied");
			await Assert.That(await ReadAttributeAsync(obj, $"DST{token}")).IsEqualTo("");
		}
		finally
		{
			await ClearAsync("ARGS");
		}
	}

	[Test]
	public async ValueTask ExtendAnywhere_HandsAnUnknownSwitchToDollarCommands()
	{
		var (obj, token) = await FixtureAsync("HookIgswitch");
		try
		{
			await RunQueued($"&CMD {obj}=[lit($@cpattr/{token.ToLowerInvariant()} *:&GOT{token} me=%0)]");
			await RunQueued("@hook/extend @cpattr=*");

			var hook = (await HookService.GetHookAsync("@CPATTR", "EXTEND")).Expect<CommandHook>();
			await Assert.That(hook.TargetObject.Number).IsEqualTo(-1);

			await RunQueued($"@cpattr/{token} hello");

			await Assert.That(await ReadAttributeAsync(obj, $"GOT{token}")).IsEqualTo("hello");
		}
		finally
		{
			await ClearAsync("EXTEND");
		}
	}

	[Test]
	public async ValueTask Ignore_False_HandsTheLineToDollarCommands()
	{
		var (obj, token) = await FixtureAsync("HookIgnoreFall");
		try
		{
			await RunQueued($"&CMD {obj}=[lit($@cpattr *{token}*:&GOT{token} me=yes)]");
			await RunQueued($"&I_CPATTR {obj}=[lit(not(strmatch(%<ARGS>,*{token}*)))]");
			await RunQueued($"@hook/ignore @cpattr={obj},I_CPATTR");

			await RunQueued($"@cpattr {obj}/SRC{token}={obj}/DST{token}[set({obj},RUNS:[inc(default({obj}/RUNS,0))])]");

			await Assert.That(await ReadAttributeAsync(obj, $"DST{token}")).IsEqualTo("")
				.Because("a false /ignore skips the built-in");
			await Assert.That(await ReadAttributeAsync(obj, $"GOT{token}")).IsEqualTo("yes")
				.Because("and the line is matched against $-commands, as PennMUSH's command_parse returns it to be");
			await Assert.That(await ReadAttributeAsync(obj, "RUNS")).IsEqualTo("1")
				.Because("the line is matched as already evaluated, so a function in it runs once");
		}
		finally
		{
			await ClearAsync("IGNORE");
		}
	}
}
