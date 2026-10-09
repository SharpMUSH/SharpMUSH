using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// The name listings (<c>lattr</c>/<c>nattr</c>/<c>xattr</c> and their <c>reg</c> forms) and the content
/// searches (<c>grep</c>, <c>wildgrep</c>, <c>regrep</c>) read through the lazy pattern read: names and
/// counts without any value, bodies one at a time and only for attributes past the read gate. These pin
/// that the switch kept the gate — a mortal_dark branch still hides its leaf from every one of them — and
/// that a regular expression that does not compile or cannot finish is reported, not swallowed.
/// </summary>
public class AttributeReadPathFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMUSHCodeParser Parser => WebAppFactoryArg.CommandParser;
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	/// <summary>A pattern that backtracks exponentially on a run of A's that is not followed by C — its
	/// parentheses escaped so the function parser hands them to the regex as text.</summary>
	private const string Pathological = @"^\(A|AA\)+C$";

	private async Task<string> Eval(long handle, string expression)
	{
		var result = await Parser.CommandParse(handle, ConnectionService, MarkupText.Plain($"think {expression}"));
		return result?.Message.ToPlainText() ?? string.Empty;
	}

	private async Task<string> God(string expression)
		=> (await WebAppFactoryArg.FunctionParser.EvaluateAsync(MarkupText.Plain(expression))).ToPlainText();

	[Test]
	[Arguments("lattr(me/{0}`*)")]
	[Arguments("nattr(me/{0}`*)")]
	[Arguments("xattr(me/{0}`*,1,5)")]
	[Arguments("reglattr(me/^{0}`)")]
	[Arguments("grep(me,{0}`*,needle)")]
	[Arguments("wildgrep(me,{0}`*,*needle*)")]
	[Arguments("regrep(me,{0}`*,needle)")]
	public async Task AMortalDarkBranchHidesItsLeavesFromListingsAndSearches(string template)
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		var mortal = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "ReadPath");
		var dbref = mortal.DbRef.ToString();

		foreach (var branch in new[] { $"RD{uid}", $"RO{uid}" })
		{
			await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{branch} {dbref}=branch"));
			for (var i = 0; i < 3; i++)
			{
				await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"&{branch}`L{i} {dbref}=a needle {i}"));
			}
		}

		await Parser.CommandParse(1, ConnectionService, MarkupText.Plain($"@set {dbref}/RD{uid}=mortal_dark"));

		var open = await Eval(mortal.Handle, string.Format(template, $"RO{uid}"));
		var dark = await Eval(mortal.Handle, string.Format(template, $"RD{uid}"));

		await Assert.That(open).IsNotEqualTo(template.StartsWith("nattr") ? "0" : string.Empty)
			.Because("the control branch is unflagged, so its leaves are listed and searched");
		await Assert.That(dark).IsEqualTo(template.StartsWith("nattr") ? "0" : string.Empty)
			.Because("a mortal_dark branch hides its leaves from the lazy read exactly as it did from the eager one");
	}

	[Test]
	public async Task ContentSearchesReturnTheMatchesInListingOrder()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		var created = await God($"create(ReadPath{uid})");
		await God($"[attrib_set({created}/G{uid}10,needle ten)][attrib_set({created}/G{uid}2,needle two)][attrib_set({created}/G{uid}1,haystack)][attrib_set({created}/G{uid}3,needle)]");

		var listed = await God($"lattr({created}/G{uid}*)");
		await Assert.That(listed).IsEqualTo($"G{uid}1 G{uid}2 G{uid}3 G{uid}10");
		await Assert.That(await God($"grep({created},G{uid}*,needle)")).IsEqualTo($"G{uid}2 G{uid}3 G{uid}10");
		await Assert.That(await God($"wildgrep({created},G{uid}*,needle*)")).IsEqualTo($"G{uid}2 G{uid}3 G{uid}10");
		await Assert.That(await God($"regrep({created},G{uid}*,^needle$)")).IsEqualTo($"G{uid}3");
		await Assert.That(await God($"nattr({created}/G{uid}*)")).IsEqualTo("4");
		await Assert.That(await God($"xattr({created}/G{uid}*,2,2)")).IsEqualTo($"G{uid}2 G{uid}3");
	}

	[Test]
	[Arguments("reglattr")]
	[Arguments("regnattr")]
	[Arguments("reglattrp")]
	public async Task AnAttributeNameRegexThatDoesNotCompileIsReported(string function)
		=> await Assert.That(await God($"{function}(me/+ABC)")).IsEqualTo(ErrorMessages.Returns.RegexpInvalid);

	[Test]
	public async Task AnAttributeNameRegexThatCannotFinishTimesOutAndTheNextCallWorks()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		var created = await God($"create(ReadPathRx{uid})");
		await God($"attrib_set({created}/{new string('A', 40)},x)");

		await Assert.That(await God($"reglattr({created}/{Pathological})")).IsEqualTo(ErrorMessages.Returns.RegexpTimeout);
		await Assert.That(await God($"reglattr({created}/^A+$)")).IsEqualTo(new string('A', 40));
	}

	[Test]
	public async Task AWorldNameRegexThatDoesNotCompileIsReported()
		=> await Assert.That(await God("lsearchr(all,name,+ABC)")).IsEqualTo(ErrorMessages.Returns.RegexpInvalid);

	[Test]
	public async Task AWorldNameRegexThatCannotFinishTimesOutAndTheNextCallWorks()
	{
		var uid = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
		var created = await God($"create({new string('A', 40)})");
		var marker = await God($"create(ReadPathMarker{uid})");

		await Assert.That(await God($"lsearchr(all,name,{Pathological})")).IsEqualTo(ErrorMessages.Returns.RegexpTimeout);
		await Assert.That(await God($"lsearchr(all,name,^ReadPathMarker{uid}$)")).StartsWith(marker.Split(':')[0]);
		await Assert.That(created).StartsWith("#");
	}
}
