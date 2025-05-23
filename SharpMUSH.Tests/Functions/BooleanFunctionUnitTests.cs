﻿using NSubstitute;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Functions;

public class BooleanFunctionUnitTests : BaseUnitTest
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
	[Arguments("t(1)", "1")]
	[Arguments("t(0)", "0")]
	[Arguments("t(true)", "1")]
	[Arguments("t(false)", "1")]
	[Arguments("t(#-1 Words)", "0")]
	[Arguments("t()", "0")]
	[Arguments("t( )", "0")]
	[Arguments("t(%b)", "1")]
	public async Task T(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await _parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("and(1,1)", "1")]
	[Arguments("and(0,1)", "0")]
	[Arguments("and(0,0,1)", "0")]
	[Arguments("and(1,1,1)", "1")]
	public async Task And(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await _parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}

	[Test]
	[Arguments("nand(1,1)", "0")]
	[Arguments("nand(0,1)", "1")]
	[Arguments("nand(0,0,1)", "1")]
	[Arguments("nand(1,1,1)", "0")]
	public async Task Nand(string str, string expected)
	{
		Console.WriteLine("Testing: {0}", str);

		var result = (await _parser!.FunctionParse(MModule.single(str)))?.Message?.ToString();

		await Assert.That(result).IsEqualTo(expected);
	}
}
