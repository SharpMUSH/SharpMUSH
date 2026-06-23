using System.Text;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.Core;
using OneOf;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Integration;

/// <summary>
/// Isolates the "remote-enactor locate" bug class behind the Scene capture failure. A command that
/// locates a target object must do so AS THE EXECUTOR (the object running the command), not the enactor —
/// confirmed against the PennMUSH oracle: forcing executor=#15 (RoomA) / enactor=#1 (RoomB) and triggering
/// a probe attribute resolved <c>me</c>=executor, <c>here</c>=the executor's room, and matched a name to the
/// object in the EXECUTOR's room (not the enactor's). SharpMUSH's LocateService is
/// <c>(parser, looker, executor, name, flags)</c>; passing the ENACTOR as the executor/permission arg makes
/// the looker-gate fail whenever a mortal, REMOTE enactor triggers a $-command on another object (e.g. a
/// WIZARD helper in the master room) — which is exactly the Scene geometry.
///
/// Reproduced here with NOTHING but builtins (no user-defined functions): a WIZARD probe thing parked in #2
/// (so its $-commands are global), and a player in a separate dug room. Each command form is run inline vs
/// via the indirection (@include / @trigger) and the remote enactor's result is compared.
/// </summary>
[NotInParallel]
public class RemoteEnactorLocateIsolationTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMUSHCodeParser FunctionParser => WebAppFactoryArg.FunctionParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	private static readonly string Tag = Guid.NewGuid().ToString("N")[..8];

	private async Task<string> Eval(string expression) =>
		(await FunctionParser.FunctionParse(MModule.single(expression)))!.Message!.ToPlainText().Trim();

	private async Task<CallState> God1(string command) =>
		await Parser.CommandParse(1, ConnectionService, MModule.single(command));

	private static string Num(string dbref)
	{
		var s = dbref.Trim();
		var colon = s.IndexOf(':');
		return colon < 0 ? s : s[..colon];
	}

	private int NotificationCount() =>
		NotifyService.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify));

	private static string? ExtractMessageText(ICall call)
	{
		if (call.GetMethodInfo().Name != nameof(INotifyService.Notify)) return null;
		var args = call.GetArguments();
		if (args.Length < 2) return null;
		return args[1] switch
		{
			OneOf<MString, string> oneOf => oneOf.Match(m => m.ToString(), s => s),
			string s => s,
			MString m => m.ToString(),
			_ => null
		};
	}

	/// <summary>Runs a command as a connection handle and returns every notification text it produced.</summary>
	private async Task<List<string>> RunAndCollectAs(long handle, string command)
	{
		var before = NotificationCount();
		await Parser.CommandParse(handle, ConnectionService, MModule.single(command));
		return NotifyService.ReceivedCalls()
			.Where(c => c.GetMethodInfo().Name == nameof(INotifyService.Notify))
			.Skip(before)
			.Select(ExtractMessageText).OfType<string>().ToList();
	}

	private async Task<string> CreatePlayerAsync(string name, string password, long handle)
	{
		await God1($"@pcreate {name}={password}");
		var dbref = (await God1($"think [pmatch({name})]")).Message?.ToPlainText()?.Trim() ?? string.Empty;
		if (string.IsNullOrEmpty(dbref) || dbref.StartsWith("#-") || !DBRef.TryParse(dbref, out var parsed))
			throw new InvalidOperationException($"Failed to create player {name}; pmatch returned '{dbref}'.");

		await ConnectionService.Register(handle, "localhost", "localhost", "test",
			_ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask, () => Encoding.UTF8);
		await ConnectionService.Bind(handle, parsed!.Value);
		return dbref;
	}

	/// <summary>All notifications a trigger produced, joined — so an error to ANY recipient is visible.</summary>
	private static string Joined(IEnumerable<string> notifications)
	{
		var list = notifications.ToList();
		return list.Count == 0 ? "<none>" : string.Join(" | ", list);
	}

	/// <summary>
	/// The shared geometry: a WIZARD probe thing in the master room (#2, so its $-commands are global) and a
	/// player in a SEPARATE dug room (remote — not co-located with the probe). Returns (probe, room, pcNum).
	/// </summary>
	private async Task<(string Probe, string Room, string PcNum)> SetupRemoteGeometryAsync(string who, long handle)
	{
		await God1("@set #1=WIZARD");

		var probe = Num((await TestIsolationHelpers.CreateTestThingAsync(Parser, ConnectionService, $"Probe{who}_{Tag}")).ToString());
		await God1($"@set {probe}=WIZARD");
		await God1($"@teleport {probe}=#2");
		await Assert.That(Num(await Eval($"loc({probe})"))).IsEqualTo("#2")
			.Because("the probe must be in the master room so its $-commands are global to a remote enactor");

		var room = Num((await God1($"@dig Room{who}_{Tag}")).Message!.ToPlainText().Trim());
		var pc = await CreatePlayerAsync($"{who}_{Tag}", "pw_remote_123", handle);
		await God1($"@tel {pc}={room}");
		await Assert.That(Num(await Eval($"loc({pc})"))).IsEqualTo(room)
			.Because("the enactor must be remote from the probe (different room) to reproduce the scene geometry");

		return (probe, room, Num(pc));
	}

	/// <summary>
	/// @include must splice in-place as the executor: each builtin probe (enactor %#, an @if-condition
	/// q-register, loc(%#), an @remit to the enactor's room) must read identically inline vs @include'd.
	/// </summary>
	[Test]
	public async Task Include_FromRemoteEnactor_PreservesCallingContext_LikeInline()
	{
		var (probe, room, pcNum) = await SetupRemoteGeometryAsync("Inc", 73L);
		const long pcHandle = 73L;

		// Probe A — enactor (%#) visibility.
		await God1($"&C_A_INL {probe}=$proa-inl-{Tag}:@pemit %#=R:[%#]");
		await God1($"@set {probe}/C_A_INL=regexp");
		await God1($"&I_A {probe}=@pemit %#=R:[%#]");
		await God1($"&C_A_INC {probe}=$proa-inc-{Tag}:@include %!/I_A");
		await God1($"@set {probe}/C_A_INC=regexp");

		// Probe B — q-register set in an @if CONDITION, then read (the scene sets RoomID this way).
		await God1($"&C_B_INL {probe}=$prob-inl-{Tag}:@if setr(0,SENT{Tag})={{@pemit %#=R:[%q0]}}");
		await God1($"@set {probe}/C_B_INL=regexp");
		await God1($"&I_B {probe}=@pemit %#=R:[%q0]");
		await God1($"&C_B_INC {probe}=$prob-inc-{Tag}:@if setr(0,SENT{Tag})={{@include %!/I_B}}");
		await God1($"@set {probe}/C_B_INC=regexp");

		// Probe C — loc(%#): a locate that depends on the enactor.
		await God1($"&C_C_INL {probe}=$proc-inl-{Tag}:@pemit %#=R:[loc(%#)]");
		await God1($"@set {probe}/C_C_INL=regexp");
		await God1($"&I_C {probe}=@pemit %#=R:[loc(%#)]");
		await God1($"&C_C_INC {probe}=$proc-inc-{Tag}:@include %!/I_C");
		await God1($"@set {probe}/C_C_INC=regexp");

		// Probe D — @remit to the enactor's room (the exact failing construct: a room emit from a $-command).
		await God1($"&C_D_INL {probe}=$prod-inl-{Tag}:@remit loc(%#)=R:INLINE-D");
		await God1($"@set {probe}/C_D_INL=regexp");
		await God1($"&I_D {probe}=@remit loc(%#)=R:INC-D");
		await God1($"&C_D_INC {probe}=$prod-inc-{Tag}:@include %!/I_D");
		await God1($"@set {probe}/C_D_INC=regexp");

		var aInline = Joined(await RunAndCollectAs(pcHandle, $"proa-inl-{Tag}"));
		var aInclude = Joined(await RunAndCollectAs(pcHandle, $"proa-inc-{Tag}"));
		var bInline = Joined(await RunAndCollectAs(pcHandle, $"prob-inl-{Tag}"));
		var bInclude = Joined(await RunAndCollectAs(pcHandle, $"prob-inc-{Tag}"));
		var cInline = Joined(await RunAndCollectAs(pcHandle, $"proc-inl-{Tag}"));
		var cInclude = Joined(await RunAndCollectAs(pcHandle, $"proc-inc-{Tag}"));
		var dInline = Joined(await RunAndCollectAs(pcHandle, $"prod-inl-{Tag}"));
		var dInclude = Joined(await RunAndCollectAs(pcHandle, $"prod-inc-{Tag}"));

		Console.WriteLine("=== @include remote-enactor isolation (inline vs @include) ===");
		Console.WriteLine($"probe={probe} (loc #2)   enactor={pcNum} (loc {room})");
		Console.WriteLine($"A enactor(%#)    inline=[{aInline}]   include=[{aInclude}]");
		Console.WriteLine($"B qreg(@if setr) inline=[{bInline}]   include=[{bInclude}]");
		Console.WriteLine($"C loc(%#)        inline=[{cInline}]   include=[{cInclude}]");
		Console.WriteLine($"D @remit room    inline=[{dInline}]   include=[{dInclude}]");

		await Assert.That(aInline).Contains($"R:{pcNum}").Because("inline %# is the remote enactor");
		await Assert.That(bInline).Contains($"R:SENT{Tag}").Because("inline reads the @if-condition q-register");
		await Assert.That(cInline).Contains($"R:{Num(room)}").Because("inline loc(%#) is the enactor's room");
		await Assert.That(dInline).Contains("R:INLINE-D").Because("inline @remit reaches the enactor's room");

		await Assert.That(aInclude).Contains($"R:{pcNum}").Because("@include must preserve the enactor (%#)");
		await Assert.That(bInclude).Contains($"R:SENT{Tag}").Because("@include must preserve q-registers set before it");
		await Assert.That(cInclude).Contains($"R:{Num(room)}").Because("@include must let loc(%#) resolve like inline");
		await Assert.That(dInclude).Contains("R:INC-D").Because("@include must let @remit reach the enactor's room");
	}

	/// <summary>
	/// @trigger must locate its target AS THE EXECUTOR. A WIZARD helper in #2 runs a $-command (triggered by
	/// a remote mortal) that does `@trigger %!/INNER`; the target (%! = the helper itself) must be locatable
	/// even though the mortal enactor is in another room. With the bug (target located as the enactor) the
	/// @trigger fails with "NOT PERMITTED TO EVALUATE ON LOOKER" and INNER never runs. This also confirms
	/// @trigger's DISTINCT semantics survive the fix: INNER runs AS the target (me = the helper, the new
	/// executor) with the triggerer as the enactor — i.e. the fix touches only the locate, not the queueing.
	/// </summary>
	[Test]
	public async Task Trigger_FromRemoteEnactor_LocatesTargetAsExecutor()
	{
		var (probe, room, _) = await SetupRemoteGeometryAsync("Trg", 74L);
		const long pcHandle = 74L;

		// INNER runs as the triggered object; report who it ran as. Emit to God (#1) so it's always collected.
		await God1($"&I_TRIG {probe}=@pemit #1=R:TRIGGERED ranAs=[num(me)] hereOf=[num(here)]");
		await God1($"&C_TRIG {probe}=$trig-{Tag}:@trigger %!/I_TRIG");
		await God1($"@set {probe}/C_TRIG=regexp");

		var got = Joined(await RunAndCollectAs(pcHandle, $"trig-{Tag}"));
		Console.WriteLine("=== @trigger remote-enactor isolation ===");
		Console.WriteLine($"probe={probe} (loc #2)   room={room}");
		Console.WriteLine($"trigger result=[{got}]");

		await Assert.That(got).DoesNotContain("NOT PERMITTED")
			.Because("@trigger must locate its target (%!) as the executor, not the remote enactor");
		await Assert.That(got).Contains("R:TRIGGERED")
			.Because("the triggered attribute must actually run");
		await Assert.That(got).Contains($"ranAs={probe}")
			.Because("@trigger's distinct semantics must survive: INNER runs AS the target object (the new executor)");
		await Assert.That(got).Contains("hereOf=#2")
			.Because("running as the target, 'here' is the target's room (#2), confirming executor-relative context");
	}
}
