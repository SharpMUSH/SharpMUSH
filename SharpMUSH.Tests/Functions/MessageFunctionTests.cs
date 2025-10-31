using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class MessageFunctionTests
{
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();

	[Test]
	[Skip("Requires attribute setup to fully test")]
	public async Task MessageBasic()
	{
		const string uniqueMessage = "Message_test_unique_message";
		
		// message(<recipients>, <message>, <attribute>)
		var result = (await Parser.FunctionParse(MModule.single($"message(#1,{uniqueMessage},TESTFORMAT)")))?.Message!;
		
		// message() returns empty string (side effect function)
		await Assert.That(result.ToPlainText()).IsEqualTo("");
	}

	[Test]
	[Skip("Requires attribute setup")]
	public async Task MessageWithAttribute()
	{
		// message(<recipients>, <default>, <object>/<attribute>, <arg0>, <arg1>, ...)
		// This would require setting up an attribute to evaluate
		
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires proper attribute and argument setup")]
	public async Task MessageWithArguments()
	{
		// message(#1, default, #0/FORMATTER, arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9)
		// Arguments should be passed to the attribute evaluation
		
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires attribute setup")]
	public async Task MessageWithSwitches()
	{
		// message(#1, default, #0/FORMAT, arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, remit)
		// Last argument can be switches: remit, oemit, nospoof, spoof
		
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires attribute setup")]
	public async Task MessageHashHashReplacement()
	{
		// When ## is passed as an argument, it should be replaced with recipient's dbref
		// message(#1, default, #0/FORMAT, ##)
		
		await ValueTask.CompletedTask;
	}

	[Test]
	public async Task MessageNoSideFxDisabled()
	{
		// When side effects are disabled, message() should return error
		// This would require modifying configuration for the test
		
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires room setup")]
	public async Task MessageRemitSwitch()
	{
		// message(here, default, #0/FORMAT, arg0,,,,,,,,,,remit)
		// Should send to room contents
		
		await ValueTask.CompletedTask;
	}

	[Test]
	[Skip("Requires multiple objects setup")]
	public async Task MessageOemitSwitch()
	{
		// message(#2 #3, default, #0/FORMAT, arg0,,,,,,,,,,oemit)
		// Should exclude specified objects
		
		await ValueTask.CompletedTask;
	}
}
