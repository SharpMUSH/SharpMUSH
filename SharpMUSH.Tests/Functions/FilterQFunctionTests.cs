using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class FilterQFunctionTests
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
	public async Task FilterQCapturesRejects()
	{
		var objNum = await CreateObjectWithAttribute("filterq_obj", "ISNUM", "isnum(%0)");

		// q-registers only live within one parse, so capture and read-back happen in ONE expression.
		await Check($"[filterq(rejects, #{objNum}/ISNUM, 1 a 2 b)]:%q<rejects>", "1 2:a b");
		// Nothing rejected: the register is set, but empty.
		await Check($"[filterq(r, #{objNum}/ISNUM, 1 2 3)]:%q<r>", "1 2 3:");
		// Everything rejected.
		await Check($"[filterq(r, #{objNum}/ISNUM, a b c)]:%q<r>", ":a b c");
		// Custom delimiter applies to the output and the captured rejects alike.
		await Check($"[filterq(r, #{objNum}/ISNUM, 1|a|2, |)]:%q<r>", "1|2:a");
	}

	[Test, NotInParallel]
	public async Task FilterQPassesExtraArgs()
	{
		var objNum = await CreateObjectWithAttribute("filterq_args_obj", "GTN", "gt(%0,%1)");

		// Positions 5+ reach the predicate as %1, %2, ... (filter()'s extra args, shifted right).
		await Check($"[filterq(r, #{objNum}/GTN, 1 5 9, , , 4)]:%q<r>", "5 9:1");
	}
}
