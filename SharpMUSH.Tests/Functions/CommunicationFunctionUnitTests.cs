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
	[ClassDataSource<WebAppFactory>(Shared = SharedType.PerTestSession)]
	public required WebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private INotifyService NotifyService => WebAppFactoryArg.Services.GetRequiredService<INotifyService>();
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	[Test]
	[Arguments("pemit(#1,test message)")]
	public async Task Pemit(string str)
	{
		// Execute the function
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		
		// Verify return value is empty (side effect function)
		await Assert.That(result.ToPlainText()).IsEqualTo("");
		
		// Verify NotifyService.Notify was called at least once
		await NotifyService
			.ReceivedWithAnyArgs(1)
			.Notify(default(DBRef)!, default!, default, default);
	}
	
	[Test]
	[Arguments("pemit(1234,test port message)")]
	public async Task PemitPort(string str)
	{
		// Execute the function
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		
		// Verify return value is empty (side effect function)
		await Assert.That(result.ToPlainText()).IsEqualTo("");
		
		// Verify NotifyService.Notify with port array was called at least once
		await NotifyService
			.ReceivedWithAnyArgs(1)
			.Notify(default(long[])!, default!, default, default);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("oemit(#1,test message)", "")]
	public async Task Oemit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("remit(#0,test message)", "")]
	public async Task Remit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("zemit(#0,test message)", "")]
	public async Task Zemit(string str, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		await Assert.That(result.ToPlainText()).IsEqualTo(expected);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nsoemit(#1,test)", "")]
	public async Task Nsoemit(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Arguments("nspemit(#1,test message)")]
	public async Task Nspemit(string str)
	{
		// Execute the function
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message!;
		
		// Verify return value is empty (side effect function)
		await Assert.That(result.ToPlainText()).IsEqualTo("");
		
		// Verify NotifyService.Notify was called at least once
		await NotifyService
			.ReceivedWithAnyArgs(1)
			.Notify(default(DBRef)!, default!, default, default);
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nsprompt(#1,test)", "")]
	public async Task Nsprompt(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nsremit(#0,test)", "")]
	public async Task Nsremit(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}

	[Test]
	[Skip("Not Yet Implemented")]
	[Arguments("nszemit(#0,test)", "")]
	public async Task Nszemit(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);
		var result = (await Parser.FunctionParse(MModule.single(str)))?.Message?.ToString();
		await Assert.That(result).IsNotNull();
	}
}
