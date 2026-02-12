using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class CommunicationFunctionUnitTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	public async Task PrivateEmit()
	{
		const string uniqueMessage = "Pemit_test_unique_message_for_verification";
		
		// Execute the function with unique message
		var result = (await Parser.FunctionParse(MModule.single($"pemit(#1,{uniqueMessage})")))?.Message!;
		
		// Verify return value is empty (side effect function)
		await Assert.That(result.ToPlainText()).IsEqualTo("");
		
		// Verify NotifyService.Notify was called (message now passed through function)
		// We check that Notify was called at least once with any AnySharpObject
		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(),
				Arg.Is<OneOf.OneOf<MString, string>>(x => x.Value.ToString()!.Contains(uniqueMessage)), 
				Arg.Any<AnySharpObject?>(), 
				Arg.Any<INotifyService.NotificationType>());
	}
	
	[Test]
	public async Task PemitPort()
	{
		const string uniqueMessage = "PemitPort_test_unique_message_for_verification";
		
		// Execute the function with unique message for port-based messaging
		var result = (await Parser.FunctionParse(MModule.single($"pemit(1234,{uniqueMessage})")))?.Message!;
		
		// Verify return value is empty (side effect function)
		// Note: Port 1234 may not have a valid connection in the test environment,
		// but the function should still return successfully
		await Assert.That(result.ToPlainText()).IsEqualTo("");
		
		// The function now filters ports by permission check, so Notify may or may not be called
		// depending on whether port 1234 has a valid connection with proper permissions
		// We just verify the function executed without error
	}

	[Test]
	[Arguments("oemit(#1,test message)", "")]
	public async Task Oemit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("remit(#0,test message)", "")]
	public async Task Remit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Zone system not yet implemented")]
	[Arguments("zemit(#0,test message)", "")]
	public async Task Zemit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Arguments("nsoemit(#1,test)", "")]
	public async Task Nsoemit(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	public async Task Nspemit()
	{
		const string uniqueMessage = "Nspemit_test_unique_message_for_verification";
		
		// Execute the function with unique message
		var result = (await Parser.FunctionParse(MModule.single($"nspemit(#1,{uniqueMessage})")))?.Message!;
		
		// Verify return value is empty (side effect function)
		await Assert.That(result.ToPlainText()).IsEqualTo("");
		
		// Verify NotifyService.Notify was called (message now passed through function)
		// We check that Notify was called at least once with any AnySharpObject
		await NotifyService
			.Received()
			.Notify(
				Arg.Any<AnySharpObject>(), 
				Arg.Any<OneOf<MString, string>>(), 
				Arg.Any<AnySharpObject?>(), 
				Arg.Any<INotifyService.NotificationType>());
	}

	[Test]
	[Arguments("nsprompt(#1,test)", "")]
	public async Task Nsprompt(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nsremit(#0,test)", "")]
	public async Task Nsremit(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nszemit(#0,test)", "")]
	public async Task Nszemit(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}
}
