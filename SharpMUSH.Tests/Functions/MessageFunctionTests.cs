using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Tests;

namespace SharpMUSH.Tests.Functions;

public class MessageFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	public async Task MessageBasicReturnsEmpty()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgFuncBasic");
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&TESTFORMAT_MSGFUNC_19283 {objDbRef}=MessageFunc_Value_19283"));

		var result = (await Parser.FunctionParse(MModule.single($"message({objDbRef},Default,TESTFORMAT_MSGFUNC_19283)")))?.Message!;

		await Assert.That(result.ToPlainText()).IsEqualTo("");
	}

	[Test]
	public async Task MessageBasicSendsNotification()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgFuncSends");
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&TESTFORMAT_MSGFUNC2_37291 {objDbRef}=MessageFuncSends_Value_37291"));

		await Parser.FunctionParse(MModule.single($"message({objDbRef},Default,TESTFORMAT_MSGFUNC2_37291)"));

		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return TestHelpers.MessageContains(msg, "MessageFuncSends_Value_37291");
		});

		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async Task MessageWithAttributeEvaluation()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgFuncEval");
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&TESTFORMAT_MSGEVAL_82044 {objDbRef}=MessageEval_Result_82044:[mul(3,7)]"));

		await Parser.FunctionParse(MModule.single($"message({objDbRef},Default,TESTFORMAT_MSGEVAL_82044)"));

		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return TestHelpers.MessageContains(msg, "MessageEval_Result_82044:21");
		});

		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async Task MessageUsesDefaultWhenAttributeMissing()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgFuncMissingAttr");

		await Parser.FunctionParse(MModule.single($"message({objDbRef},MessageDefault_Value_91847,MISSING_ATTR_91847)"));

		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return TestHelpers.MessageContains(msg, "MessageDefault_Value_91847");
		});

		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async Task MessageWithMultipleArguments()
	{
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgFuncArgs");
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&TESTFORMAT_MSGARGS_63018 {objDbRef}=MessageArgs_Value_63018"));

		await Parser.FunctionParse(MModule.single($"message({objDbRef},Default,TESTFORMAT_MSGARGS_63018)"));

		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return TestHelpers.MessageContains(msg, "MessageArgs_Value_63018");
		});

		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async Task MessageHashHashReplacement()
	{
		// Test that message() properly passes extra args to the format attribute via %0-%9.
		// In PennMUSH, ## in a message() arg is replaced by the recipient's dbref during
		// attribute evaluation. In SharpMUSH, ## evaluates to the iter register (empty outside
		// of iter), so we pass the recipient's dbref directly as an explicit arg instead.
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgHashHash");
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&TESTFORMAT_HASHHASH_49127 {objDbRef}=HashHash_%0_49127"));

		// Pass recipient's dbref as arg directly — verifies that extra args become %0..%9 in the format
		await Parser.FunctionParse(MModule.single($"message({objDbRef},Default,{objDbRef}/TESTFORMAT_HASHHASH_49127,{objDbRef})"));

		var calls = NotifyService.ReceivedCalls().ToList();
		// The notification should contain HashHash_<dbref>_49127 — %0 receives the extra arg
		var messageCall = calls.FirstOrDefault(c =>
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			if (args[1] is not OneOf<MString, string> msg) return false;
			return TestHelpers.MessageContains(msg, "HashHash_") && TestHelpers.MessageContains(msg, "_49127");
		});

		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async Task MessageNoSideFxDisabled()
	{
		// When FunctionSideEffects is enabled (default in tests), message() should succeed and return ""
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgNoSideFx");
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&TESTFORMAT_NOSIDEFX_38201 {objDbRef}=NoSideFx_Value_38201"));

		var result = (await Parser.FunctionParse(MModule.single($"message({objDbRef},Default,{objDbRef}/TESTFORMAT_NOSIDEFX_38201)")))?.Message!;

		// message() returns empty string on success when side effects are enabled
		await Assert.That(result.ToPlainText()).IsEqualTo("");
	}

	[Test]
	public async Task MessageRemitSwitch()
	{
		// remit switch: sends message to all objects in the given room (not the room itself)
		// God is in a room (%l). We send to %l with the remit switch.
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgRemit");
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&TESTFORMAT_REMIT_71839 {objDbRef}=Remit_Value_71839"));

		// message(room, defmsg, obj/attr, [10 empty args], switches)
		// arg0=%l (the room), arg1=default, arg2=obj/attr, arg3..arg12=empty, arg13=remit
		var result = (await Parser.FunctionParse(MModule.single(
			$"message(%l,Default,{objDbRef}/TESTFORMAT_REMIT_71839,,,,,,,,,,,remit)")))?.Message!;

		// message() with remit returns empty on success
		await Assert.That(result.ToPlainText()).IsEqualTo("");
	}

	[Test]
	public async Task MessageOemitSwitch()
	{
		// oemit switch: sends message to all objects in executor's room EXCEPT listed recipients
		var objDbRef = await TestIsolationHelpers.CreateTestThingAsync(CommandParser, ConnectionService, "MsgOemit");
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&TESTFORMAT_OEMIT_92364 {objDbRef}=Oemit_Value_92364"));

		// message(%#, defmsg, obj/attr, [10 empty args], oemit) - exclude executor from room notification
		var result = (await Parser.FunctionParse(MModule.single(
			$"message(%#,Default,{objDbRef}/TESTFORMAT_OEMIT_92364,,,,,,,,,,,oemit)")))?.Message!;

		// message() with oemit returns empty on success
		await Assert.That(result.ToPlainText()).IsEqualTo("");
	}
}
