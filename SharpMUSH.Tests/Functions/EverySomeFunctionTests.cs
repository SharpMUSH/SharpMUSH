using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

public class EverySomeFunctionTests
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
	public async Task EveryReturnsOneZero()
	{
		var objNum = await CreateObjectWithAttribute("every_obj", "ISNUM", "isnum(%0)");

		await Check($"every(#{objNum}/ISNUM, 1 2 3)", "1");
		await Check($"every(#{objNum}/ISNUM, 1 a 2 b)", "0");
	}

	[Test, NotInParallel]
	public async Task SomeReturnsOneZero()
	{
		var objNum = await CreateObjectWithAttribute("some_obj", "ISNUM", "isnum(%0)");

		await Check($"some(#{objNum}/ISNUM, a 2 b)", "1");
		await Check($"some(#{objNum}/ISNUM, a b c)", "0");
	}

	[Test, NotInParallel]
	public async Task RegisterCapturesNonMatches()
	{
		var objNum = await CreateObjectWithAttribute("everysome_reg_obj", "ISNUM", "isnum(%0)");

		// q-registers only live within one parse, so capture and read-back happen in ONE expression.
		// The empty third argument skips the delimiter (default space).
		await Check($"[every(#{objNum}/ISNUM, 1 a 2 b, , fails)]:%q<fails>", "0:a b");
		await Check($"[some(#{objNum}/ISNUM, 1 a 2 b, , fails)]:%q<fails>", "1:a b");
		// Nothing failed: the register is set, but empty.
		await Check($"[every(#{objNum}/ISNUM, 1 2 3, , fails)]:%q<fails>", "1:");
	}

	[Test, NotInParallel]
	public async Task LambdaPredicateReturnsOneZero()
	{
		// The #lambda branch must produce the same results as an attribute predicate.
		await Check(@"every(#lambda/isnum\(\%0\), 1 2 3)", "1");
		await Check(@"every(#lambda/isnum\(\%0\), 1 a 2)", "0");
		await Check(@"some(#lambda/isnum\(\%0\), a 2 b)", "1");
		await Check(@"some(#lambda/isnum\(\%0\), a b c)", "0");
	}

	[Test, NotInParallel]
	public async Task LambdaBranchCapturesNonMatchesInRegister()
	{
		// With a register the lambda branch evaluates every element (no short-circuit) and
		// collects the failures — the same contract as the attribute branch.
		await Check(@"[every(#lambda/isnum\(\%0\), 1 a 2 b, , fails)]:%q<fails>", "0:a b");
		await Check(@"[some(#lambda/isnum\(\%0\), 1 a 2 b, , fails)]:%q<fails>", "1:a b");
	}
}
