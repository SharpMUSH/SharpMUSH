using DotNext.Threading;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Tests.Database;

public class AttributeSyntaxFlagTests
{
	private static SharpAttribute WithFlags(params string[] names) => new(
		Id: "attribute/1",
		Key: "TEST",
		Name: "TEST",
		Flags: names.Select(n => new SharpAttributeFlag
		{
			Name = n, Symbol = n == "cmdsyntax" ? "x" : "f", System = true, Inheritable = true
		}).ToArray(),
		CommandListIndex: null,
		LongName: "TEST",
		Leaves: new AsyncLazy<IAsyncEnumerable<SharpAttribute>>(_ => Task.FromResult(AsyncEnumerable.Empty<SharpAttribute>())),
		Owner: new AsyncLazy<SharpPlayer?>(_ => Task.FromResult<SharpPlayer?>(null)),
		SharpAttributeEntry: new AsyncLazy<SharpAttributeEntry?>(_ => Task.FromResult<SharpAttributeEntry?>(null)))
	{
		Value = MModule.single("say hi")
	};

	[Test]
	public async Task CmdSyntaxFlag_MapsToCommandList()
	{
		await Assert.That(WithFlags("cmdsyntax").IsCmdSyntax()).IsTrue();
		await Assert.That(WithFlags("cmdsyntax").SyntaxParseType()).IsEqualTo(ParseType.CommandList);
	}

	[Test]
	public async Task FunSyntaxFlag_MapsToFunction()
	{
		await Assert.That(WithFlags("funsyntax").IsFunSyntax()).IsTrue();
		await Assert.That(WithFlags("funsyntax").SyntaxParseType()).IsEqualTo(ParseType.Function);
	}

	[Test]
	public async Task BothFlags_CommandWins()
		=> await Assert.That(WithFlags("cmdsyntax", "funsyntax").SyntaxParseType())
			.IsEqualTo(ParseType.CommandList);

	[Test]
	public async Task NoFlags_ReturnsNull()
		=> await Assert.That(WithFlags().SyntaxParseType()).IsNull();

	[Test]
	public async Task IsNoDebug_MatchesSeededFlagName()
		=> await Assert.That(WithFlags("no_debug").IsNoDebug()).IsTrue();
}
