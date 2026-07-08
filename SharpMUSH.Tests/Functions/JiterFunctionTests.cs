using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class JiterFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMUSHCodeParser Parser => WebAppFactoryArg.FunctionParser;
	private IMUSHCodeParser CommandParser => WebAppFactoryArg.CommandParser;
	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();

	/// <summary>
	/// Creates a unique test object with a single attribute set on it.
	/// Each test creates its own object to avoid cross-contamination when running in parallel.
	/// </summary>
	private async Task<int> CreateObjectWithAttribute(string objectName, string attrName, string attrValue)
	{
		var createResult = await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"@create {objectName}"));
		var dbRef = DBRef.Parse(createResult.Message!.ToPlainText()!);
		await CommandParser.CommandParse(1, ConnectionService,
			MModule.single($"&{attrName} #{dbRef.Number}={attrValue}"));
		return dbRef.Number;
	}

	private async Task Check(string function, string expected)
	{
		var result = (await Parser.FunctionParse(MModule.single(function)))?.Message!;
		await Assert.That(result.ToString()).IsEqualTo(expected);
	}

	[Test, NotInParallel]
	public async Task Jiter()
	{
		var objNum = await CreateObjectWithAttribute("jiter_obj", "F1", "add(%0,1)");
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&F2 #{objNum}=mul(%0,2)"));
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&F3 #{objNum}=sub(%0,3)"));

		// Every attribute receives the SAME input as %0; results are juxtaposed.
		await Check($"jiter(#{objNum}/F1 #{objNum}/F2 #{objNum}/F3, 10)", "11 20 7");
		// Custom output separator.
		await Check($"jiter(#{objNum}/F1 #{objNum}/F2 #{objNum}/F3, 10, |)", "11|20|7");
		// A single attribute is just that one evaluation.
		await Check($"jiter(#{objNum}/F1, 10)", "11");
	}

	[Test, NotInParallel]
	public async Task JiterStrings()
	{
		var objNum = await CreateObjectWithAttribute("jiter_str_obj", "W", "ucstr(%0)");
		await CommandParser.CommandParse(1, ConnectionService, MModule.single($"&L #{objNum}=strlen(%0)"));

		await Check($"jiter(#{objNum}/W #{objNum}/L, abc)", "ABC 3");
	}
}
