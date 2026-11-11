using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Parser;

/// <summary>
/// What the <c>rhost</c> and <c>mux</c> dialects read differently from PennMUSH: RhostMUSH's substitutions and its
/// <c>}</c> prefix, the commas of a one-argument function, and the <c>space_compress</c> option. Outside the dialect
/// each form keeps PennMUSH's reading.
/// </summary>
public class DialectParsingTests : ServerTestBase
{
	private IHookService HookService => WebAppFactoryArg.Services.GetRequiredService<IHookService>();

	private static IDisposable Dialect(string dialect)
		=> TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { Dialect = dialect }
		});

	[Test]
	[Arguments("ulambda(lit(#lambda/%+!|%+!-!|%-|%+),a,b,c)", "a b c|a-b-c||3")]
	[Arguments("ulambda(lit(#lambda/%-|[words(%+!)]),a,b,c,d,e,f,g,h,i,j,k,l)", "k,l|12")]
	[Arguments("%f|%F|%w|%W|%wa", "%f|%f|#-1|#-1|#-1a")]
	[Arguments("%-#|%-m|%-p|%-h<slot>", "#1|||#-1 INVALID CACHE")]
	[Arguments("[strmatch(%-n,name(#1))][strmatch(%-N,name(#1))]", "11")]
	public async Task RhostReadsItsSubstitutions(string code, string expected)
	{
		using var dialect = Dialect("rhost");
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	[Arguments("ulambda(lit(#lambda/%+!|%+!-!|%-),a,b,c)", "3!|3!-!|-")]
	[Arguments("%-n|%-#|%-m|%-p|%-h<slot>", "-n|-#|-m|-p|-h<slot>")]
	[Arguments("%f|%w", "f|w")]
	[Arguments("[iter(a b,%-##)]|[iter(a b,%-#@)]|[switch(x,*,%-#$)]", "-a -b|-1 -2|-x")]
	public async Task OtherwiseTheyKeepPennMushsReading(string code, string expected)
		=> await Assert.That(await Eval(code)).IsEqualTo(expected);

	/// <summary><c>%-#</c> is whoever runs the queue entry, not the object a <c>u()</c> runs as.</summary>
	[Test]
	public async Task QueueRunnerIsNotTheExecutor()
	{
		var thing = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "QueueRunner");
		await Cmd($"&RUNNER {thing}=%-#|%!");
		await Cmd($"@set {thing}/RUNNER=rhost");

		await Assert.That(await Eval($"u({thing}/RUNNER)")).IsEqualTo($"#1|#{thing.Number}");
	}

	/// <summary>In a hook, <c>%-m</c> is the command line it hooks and <c>%-p</c> the line typed.</summary>
	[Test]
	public async Task HookReadsTheHookedCommand()
	{
		var holder = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "HookSubs");
		var target = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "HookSubsTgt");
		await Cmd($"&SEEN {holder}=");
		await Cmd($"&BEFORE {holder}=attrib_set({holder}/SEEN,%-m|%-p)");
		await Cmd($"@set {holder}/BEFORE=rhost");
		await Cmd($"@hook/before @ATRLOCK={holder},BEFORE");
		try
		{
			await Cmd($"&A {target}=1");
			await Cmd($"@atrlock {target}/A=on");
			await Assert.That(await Eval($"get({holder}/SEEN)"))
				.IsEqualTo($"@atrlock {target}/A=on|@atrlock {target}/A=on");
		}
		finally
		{
			await HookService.ClearHookAsync("@ATRLOCK", "BEFORE");
		}
	}

	[Test]
	public async Task BracePrefixKeepsSpaces()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "KeepSpaces");
		var kept = TestIsolationHelpers.GenerateUniqueName("kept");
		var compressed = TestIsolationHelpers.GenerateUniqueName("compressed");
		try
		{
			using var dialect = Dialect("rhost");
			await CmdAs(actor.DbRef, actor.Handle, $"}}@pemit me={kept}   x");
			await CmdAs(actor.DbRef, actor.Handle, $"@pemit me={compressed}   x");

			await Assert.That(Notifications.For(actor.DbRef).Where(message => message.StartsWith(kept, StringComparison.Ordinal)))
				.IsEquivalentTo([$"{kept}   x"]);
			await Assert.That(Notifications.For(actor.DbRef).Where(message => message.StartsWith(compressed, StringComparison.Ordinal)))
				.IsEquivalentTo([$"{compressed} x"]);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	/// <summary>The <c>}</c> keeps the spaces of the line typed, not of the <c>$</c>-command body it runs.</summary>
	[Test]
	public async Task BracePrefixStopsAtTheCommandBody()
	{
		var actor = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "KeepSpacesBody");
		var verb = TestIsolationHelpers.GenerateUniqueName("ksverb").ToLowerInvariant();
		var marker = TestIsolationHelpers.GenerateUniqueName("body");
		try
		{
			await TestIsolationHelpers.ClearNoCommandAsync(CommandParser, ConnectionService, actor.DbRef);
			await CmdAs(actor.DbRef, actor.Handle, $"&CMD me=${verb} *:@pemit %#=%0   y");
			using var dialect = Dialect("rhost");
			await CmdAs(actor.DbRef, actor.Handle, $"}}{verb} {marker}");

			await Assert.That(Notifications.For(actor.DbRef).Where(message => message.StartsWith(marker, StringComparison.Ordinal)))
				.IsEquivalentTo([$"{marker} y"]);
		}
		finally
		{
			await ConnectionService.Disconnect(actor.Handle);
		}
	}

	[Test]
	public async Task SpaceCompressOffKeepsSpaces()
	{
		await Assert.That(await Eval("a    b  [add(1,2)]")).IsEqualTo("a b 3");

		using var keep = TestOptionsOverride.Scope(options => options with
		{
			Compatibility = options.Compatibility with { SpaceCompress = false }
		});
		await Assert.That(await Eval("a    b  [add(1,2)]")).IsEqualTo("a    b  3");
		await Assert.That(await Eval("a   b   ")).IsEqualTo("a   b   ");
	}

	[Test]
	[Arguments("mux", "strlen(a,b)", "3")]
	[Arguments("mux", "strlen(a, b)", "4")]
	[Arguments("mux", "strlen(a,    b)", "4")]
	[Arguments("mux", "strlen(add(1,2),x)", "3")]
	[Arguments("mux", "ucstr(a,b,,c)", "A,B,,C")]
	[Arguments("mux", "lit(a,[b])", "a,[b]")]
	[Arguments("mux", "t(0,1)", "1")]
	[Arguments("mux", "reverse(ab,c)", "c,ba")]
	[Arguments("mux", "strlen(abc)", "3")]
	[Arguments("rhost", "strlenvis(a,b)", "3")]
	[Arguments("rhost", "encode64(a,b)", "YSxi")]
	public async Task OneArgumentFunctionsAbsorbCommas(string dialect, string code, string expected)
	{
		using var scope = Dialect(dialect);
		await Assert.That(await Eval(code)).IsEqualTo(expected);
	}

	[Test]
	public async Task OtherwiseExtraArgumentsAreAnError()
		=> await Assert.That(await Eval("ucstr(a,b)")).StartsWith("#-1");
}
