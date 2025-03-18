using NSubstitute;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Functions;

public class FlowFunctionUnitTests : BaseUnitTest
{
	private static IMUSHCodeParser? _parser;

	[Before(Class)]
	public static async Task OneTimeSetup()
	{
		_parser = await TestParser(
			ns: Substitute.For<INotifyService>()
		);
	}

	[Test]
	[Arguments("if(1,True)", "True")]
	[Arguments("if(0,True)", "")]
	public async Task If(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await _parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}
	

	[Test]
	[Arguments("if(1,True,False)", "True")]
	[Arguments("if(0,True,False)", "False")]
	public async Task IfElse(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await _parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}
	
}