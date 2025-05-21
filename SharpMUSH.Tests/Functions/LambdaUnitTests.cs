using NSubstitute;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Functions;

public class LambdaUnitTests : BaseUnitTest
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
	[Arguments("u(#lambda/add\\(1\\,2\\))", "3")]
	// [Arguments("u(lit(#lambda/add(1,2)))", "3")] // Long running bug/test found?
	[Arguments("u(#lambda/[add(1,2)])", "3")] 
	[Arguments("u(#lambda/3)", "3")]
	[Arguments("3", "3")] 
	public async Task BasicLambdaTest(string call, string expected)
	{
		var res = (await _parser!.FunctionParse(MModule.single(call)))!.Message!;
		await Assert.That(res.ToPlainText()).IsEqualTo(expected);
	}
}