using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class JsonGroupByFunctionTests
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
	public async Task GroupsByEvaluatedKey()
	{
		var objNum = await CreateObjectWithAttribute("jgroupby_obj", "KEYLEN", "strlen(%0)");

		// Keys are the evaluated attribute results, in first-seen order; values are the elements.
		await Check($"json_group_by(#{objNum}/KEYLEN, a bb cc d)", "{\"1\":[\"a\",\"d\"],\"2\":[\"bb\",\"cc\"]}");
		// Custom delimiter.
		await Check($"json_group_by(#{objNum}/KEYLEN, a|bb|d, |)", "{\"1\":[\"a\",\"d\"],\"2\":[\"bb\"]}");
	}

	[Test, NotInParallel]
	public async Task GroupsByFirstLetter()
	{
		var objNum = await CreateObjectWithAttribute("jgroupby_fl_obj", "FIRSTLETTER", "left(%0,1)");

		await Check($"json_group_by(#{objNum}/FIRSTLETTER, apple avocado banana)",
			"{\"a\":[\"apple\",\"avocado\"],\"b\":[\"banana\"]}");
	}
}
