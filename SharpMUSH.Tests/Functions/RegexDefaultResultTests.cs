using SharpMUSH.Implementation;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Functions;

public class RegexDefaultResultTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory Factory { get; init; }

	[Test]
	[Arguments("reswitch", false)]
	[Arguments("reswitch", true)]
	[Arguments("reswitchall", false)]
	[Arguments("reswitchall", true)]
	public async Task ReturnedDefaultIsACompletedValue(string function, bool hadErrors)
	{
		var original = (MUSHCodeParser)Factory.FunctionParser;
		var library = new FunctionLibraryService();
		foreach (var pair in original.FunctionLibrary) library.Add(pair.Key, pair.Value);
		var calls = 0;
		library.Add("resultprobe", (new FunctionDefinition(new SharpFunctionAttribute
		{ Name = "resultprobe", Flags = FunctionFlags.Regular, MinArgs = 0, MaxArgs = 0 }, _ =>
		{
			calls++;
			return ValueTask.FromResult(new CallState("value")
			{
				HadErrors = hadErrors,
				ParsedResult = () => { calls++; return ValueTask.FromResult<CallState?>(new CallState("value")); }
			});
		}), true));
		var parser = original with { FunctionLibrary = library };
		var result = await parser.FunctionParse(MarkupText.Plain($"{function}(a,b,unused,resultprobe())"));
		await Assert.That(result!.Message!.Text).IsEqualTo("value");
		await Assert.That(result.HadErrors).IsEqualTo(hadErrors);
		await Assert.That((await result.GetParsedResultAsync()).Message!.Text).IsEqualTo("value");
		await Assert.That(calls).IsEqualTo(1);
	}
}
