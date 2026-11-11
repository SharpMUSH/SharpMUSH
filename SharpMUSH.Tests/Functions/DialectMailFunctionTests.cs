using SharpMUSH.Library;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Tests.Functions;

/// <summary>
/// TinyMUX's mailcount(), mailflags(), mailinfo(), mailreview() and mailsize(), and RhostMUSH's mailquick(),
/// mailread(), mailquota(), foldercurrent(), folderlist() and msizetot().
/// </summary>
public class DialectMailFunctionTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private DBRef _player;
	private string _tag = string.Empty;

	/// <summary>A fresh player who has mailed itself three messages: one read, one urgent and tagged, one cleared.</summary>
	[Before(Test)]
	public async Task MailThreeMessages()
	{
		_tag = TestIsolationHelpers.GenerateUniqueName("DM");
		_player = await TestIsolationHelpers.CreateTestPlayerAsync(WebAppFactoryArg.Services, Mediator, "DialectMail");
		var executor = (await Mediator.Send(new GetObjectNodeQuery(_player))).Expect<AnySharpObject>();
		var mailbox = executor.Expect<SharpPlayer>();

		SharpMail Message(int hoursAgo, bool read, bool urgent, bool tagged, bool cleared, string text) => new()
		{
			DateSent = DateTimeOffset.UtcNow.AddHours(-hoursAgo),
			Fresh = !read,
			Read = read,
			Tagged = tagged,
			Urgent = urgent,
			Cleared = cleared,
			Forwarded = false,
			Folder = "INBOX",
			Content = MarkupText.Plain($"{_tag}-{text}"),
			Subject = MarkupText.Plain($"{_tag}-Subject-{text}"),
			From = new DotNext.Threading.AsyncLazy<AnyOptionalSharpObject>(
				async _ => await ValueTask.FromResult(executor.WithNoneOption()))
		};

		await Mediator.Send(new SendMailCommand(executor.Object(), mailbox, Message(3, true, false, false, false, "one")));
		await Mediator.Send(new SendMailCommand(executor.Object(), mailbox, Message(2, false, true, true, false, "two")));
		await Mediator.Send(new SendMailCommand(executor.Object(), mailbox, Message(1, false, false, false, true, "three")));
	}

	private async Task<string> AsPlayer(string code)
		=> (await WebAppFactoryArg.FunctionParserFor(_player).EvaluateAsync(MarkupText.Plain(code))).ToPlainText();

	private async Task<string> AsWizard(string code)
		=> (await WebAppFactoryArg.FunctionParser.EvaluateAsync(MarkupText.Plain(code))).ToPlainText();

	[Test]
	public async Task TinyMuxMailFunctionsReadTheCurrentFolder()
	{
		var number = _player.Number;
		await Assert.That(await AsPlayer("[mailcount()] [mailcount(me)]")).IsEqualTo("3 3");
		await Assert.That(await AsPlayer("[mailflags(1)]|[mailflags(2)]|[mailflags(3)]|[mailflags(9)]"))
			.IsEqualTo("R|UT|C|#-1 NO SUCH MESSAGE");
		await Assert.That(await AsPlayer("[mailinfo(2,subject)]|[mailinfo(1,read)]|[mailinfo(2,read)]|[mailinfo(2,flags)]"))
			.IsEqualTo($"{_tag}-Subject-two|1|0|UT");
		await Assert.That(await AsPlayer("[mailinfo(1,from)] [mailinfo(1,to)] [mailinfo(1,tolist)] [mailinfo(1,size)] [mailinfo(1,bogus)]"))
			.IsEqualTo($"#{number} #{number} {number} {$"{_tag}-one".Length} #-1 INVALID FIELD");
		await Assert.That(await AsPlayer("[mailinfo(1,body)]")).IsEqualTo($"{_tag}-one");
		await Assert.That(await AsPlayer("[mailreview(me)] [mailreview(me,9)]")).IsEqualTo("3 #-1 NO SUCH MESSAGE");
		await Assert.That(await AsPlayer("[mailreview(me,1)]")).StartsWith(_tag);

		var size = $"{_tag}-one".Length + $"{_tag}-two".Length + $"{_tag}-three".Length + 3;
		await Assert.That(await AsPlayer("[mailsize(me)]")).IsEqualTo(size.ToString());
		await Assert.That(await AsWizard($"[mailsize(#{number})] [mailcount(#{number})] [mailflags(1,#{number})]"))
			.IsEqualTo($"{size} 3 R");
	}

	[Test]
	public async Task TinyMuxMailFunctionsReadOnlyMailboxesTheCallerControls()
	{
		await Assert.That(await AsPlayer("[mailcount(#1)]")).IsEqualTo("#-1 PERMISSION DENIED: NOT CONTROLLED");
		await Assert.That(await AsPlayer($"[mailsize(*NoSuch{_tag})] [mailinfo(1,body,*NoSuch{_tag})]"))
			.IsEqualTo("#-1 NO SUCH PLAYER #-1 NO SUCH PLAYER");
	}

	[Test]
	public async Task RhostMailFunctionsCountAndReadAMailbox()
	{
		var target = $"#{_player.Number}";
		await Assert.That(await AsWizard($"[mailquick({target})]|[mailquick({target},,1)]|[mailquick({target},Incoming,2)]|[mailquick({target},,3)]"))
			.IsEqualTo("3 1 0 1 1 0|2 1 1|3|1 1 1");
		await Assert.That(await AsWizard($"[mailquick({target},Nowhere)]|[mailquick(*NoSuch{_tag})]"))
			.IsEqualTo("0 0 0 0 0 0|#-1 NOT FOUND");

		await Assert.That(await AsWizard($"[foldercurrent({target})]|[foldercurrent({target},1)]|[folderlist({target})]"))
			.IsEqualTo("Incoming||Incoming");
		await Assert.That(await AsWizard($"[mailquota({target},2)]|[mailquota({target},3)]|[mailquota()]|[mailquota({target},9)]"))
			.IsEqualTo("0 0|3 -1|-1 -1 -1 -1 -1 -1|-1 -1 -1 -1 -1 -1");
		await Assert.That(await AsWizard($"[first(mailquota({target},1))]")).IsEqualTo("3");
		await Assert.That(await AsWizard($"[words(mailquota({target},abc))]")).IsEqualTo("6");

		await Assert.That(await AsWizard($"[mailread({target},1,S)]|[mailread({target},new,I)]|[mailread({target},1,G)]|[mailread({target},2,G)]|[mailread({target},3,G)]"))
			.IsEqualTo($"{_tag}-Subject-one|2|OLD|NEW|MARKED");
		await Assert.That(await AsWizard($"[mailread({target},1,B)]|[mailread({target},1,K)]|[mailread({target},1,?)]|[mailread({target},9,B)]"))
			.IsEqualTo($"{_tag}-one|{$"{_tag}-one".Length}||#-1 NO SUCH MESSAGE");

		await Assert.That(await AsWizard($"[mailread({target},2,B,1)]|[mailread({target},2,G)]|[mailread({target},new,I)]"))
			.IsEqualTo("|OLD|#-1 NO SUCH MESSAGE");
		await Assert.That(await AsWizard("[gt(msizetot(),0)]")).IsEqualTo("1");
	}

	[Test]
	public async Task AThingHasNoMailboxOfItsOwn()
	{
		var thing = HelperFunctions.ParseDbRef(await AsWizard($"[create(Mailless {_tag})]")).Expect<DBRef>();
		var result = (await WebAppFactoryArg.FunctionParserFor(thing).EvaluateAsync(MarkupText.Plain("[mailcount()] [mailflags(1)]")))
			.ToPlainText();
		await Assert.That(result).IsEqualTo("0 #-1 NO SUCH MESSAGE");
	}

	[Test]
	public async Task RhostMailFunctionsAreForWizards()
	{
		await Assert.That(await AsPlayer("[mailquick(me)]")).StartsWith("#-1");
		await Assert.That(await AsPlayer("[msizetot()]")).StartsWith("#-1");
	}
}
