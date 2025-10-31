using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ReceivedExtensions;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

public class VerbCommandTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;

	private static bool MessageEquals(OneOf<MString, string> msg, string expected) =>
		msg.Match(
			ms => ms.ToPlainText() == expected,
			s => s == expected);

	private static bool MessageContains(OneOf<MString, string> msg, string expected) =>
		msg.Match(
			ms => ms.ToPlainText().Contains(expected),
			s => s.Contains(expected));

	[Test]
	public async ValueTask VerbWithDefaultMessages()
	{
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=#1,,,ActorDefault,,,OthersDefault"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("ActorDefault"),
				s => s.Contains("ActorDefault"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async ValueTask VerbWithAttributes()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&WHAT #1=You perform the action!"));
		await Parser.CommandParse(1, ConnectionService, MModule.single("&OWHAT #1=performs the action!"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=#1,WHAT,DefaultWhat,OWHAT,DefaultOwhat"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("You perform the action!"),
				s => s.Contains("You perform the action!"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async ValueTask VerbWithStackArguments()
	{
		await Parser.CommandParse(1, ConnectionService, MModule.single("&WHAT_ARGS #1=You say: %0 %1"));
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=#1,WHAT_ARGS,Default,,,,,Hello,World"));
		
		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("You say: Hello World"),
				s => s.Contains("You say: Hello World"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	public async ValueTask VerbInsufficientArgs()
	{
		NotifyService.ClearReceivedCalls();
		
		await Parser.CommandParse(1, ConnectionService, MModule.single("@verb #1=#2"));

		var calls = NotifyService.ReceivedCalls().ToList();
		var messageCall = calls.FirstOrDefault(c => 
		{
			var args = c.GetArguments();
			if (args.Length < 2) return false;
			var msg = args[1] as OneOf<MString, string>?;
			if (msg == null) return false;
			return msg.Value.Match(
				ms => ms.ToPlainText().Contains("Usage: @verb"),
				s => s.Contains("Usage: @verb"));
		});
		
		await Assert.That(messageCall).IsNotNull();
	}

	[Test]
	[Skip("Requires proper permission setup")]
	public async ValueTask VerbPermissionDenied()
	{
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires AWHAT command list execution verification")]
	public async ValueTask VerbExecutesAwhat()
	{
		await ValueTask.CompletedTask;
	}
}
