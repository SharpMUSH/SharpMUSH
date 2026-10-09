using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// Commands that read one attribute read it as PennMUSH reads it at the same place: through
/// <c>atr_get</c> (the object, its @parent chain, then the type ancestor) or through
/// <c>atr_get_noparent</c> (the object alone). Each test names the PennMUSH line it follows.
/// </summary>
public class CommandAttributeInheritanceTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();
	private IMUSHCodeParser GodParser => WebAppFactoryArg.CommandParser;

	private static readonly DBRef AncestorThing = new(6);

	private sealed record Mortal(TestIsolationHelpers.TestPlayer Player, IMUSHCodeParser Parser, DBRef Room);

	private async Task<Mortal> MortalInOwnRoom(string token)
	{
		var player = await TestIsolationHelpers.CreateTestPlayerWithHandleAsync(
			WebAppFactoryArg.Services, Mediator, ConnectionService, "CmdInh");
		var parser = WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle);

		var dig = await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@dig Room_{token}"));
		var room = DBRef.Parse(dig.Message.ToPlainText().Trim());
		await parser.CommandParse(player.Handle, ConnectionService, MarkupText.Plain($"@tel me={room}"));
		return new Mortal(player, parser, room);
	}

	private static async Task<CallState> Run(Mortal who, string command)
		=> await who.Parser.CommandParse(who.Player.Handle, who.Parser.ServiceProvider.GetRequiredService<IConnectionService>(),
			MarkupText.Plain(command));

	private async Task<DBRef> Create(Mortal who, string name)
		=> DBRef.Parse((await Run(who, $"@create {name}")).Message.ToPlainText().Trim());

	private Task God(string command)
		=> GodParser.CommandParse(1, ConnectionService, MarkupText.Plain(command)).AsTask();

	/// <summary>The object's own value of <paramref name="attribute"/>, or null; never inherited.</summary>
	private async Task<string?> OwnValue(DBRef obj, string attribute)
		=> (await Mediator.CreateStream(new GetAttributeQuery(obj, [attribute.ToUpperInvariant()])).FirstOrDefaultAsync())
			?.Value.ToPlainText();

	/// <summary>A child thing of the mortal's whose @parent holds FOO; returns (parent, child).</summary>
	private async Task<(DBRef Parent, DBRef Child)> ParentAndChild(Mortal who, string token, string fooValue)
	{
		var parent = await Create(who, $"P_{token}");
		var child = await Create(who, $"C_{token}");
		await Run(who, $"&FOO {parent}={fooValue}");
		await Run(who, $"@parent {child}={parent}");
		return (parent, child);
	}

	private List<string> Heard(Mortal who) => WebAppFactoryArg.Notifications.For(who.Player.DbRef);

	/// <summary><c>do_trigger</c> queues with noparent 0 (<c>set.c:1342</c>, <c>cque.c:805-808</c>).</summary>
	[Test]
	public async Task Trigger_RunsAnInheritedAttribute()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("trig");
		var who = await MortalInOwnRoom(token);
		var (_, child) = await ParentAndChild(who, token, $"@pemit %#=TRIGGERED_{token}");

		await Run(who, $"@trigger {child}/FOO");

		await WebAppFactoryArg.Notifications.WaitForAsync(who.Player.DbRef, $"TRIGGERED_{token}");
	}

	/// <summary><c>queue_include_attribute</c> reads with noparent 0 (<c>cque.c:712-717</c>).</summary>
	[Test]
	public async Task Include_RunsAnInheritedAttribute()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("incl");
		var who = await MortalInOwnRoom(token);
		var (_, child) = await ParentAndChild(who, token, $"think INCLUDED_{token}");

		await Run(who, $"@include {child}/FOO");

		await WebAppFactoryArg.Notifications.WaitForAsync(who.Player.DbRef, $"INCLUDED_{token}");
	}

	/// <summary>
	/// <c>find_var_dest</c> reads DESTINATION through <c>call_attrib</c> (<c>move.c:376-377</c>), which
	/// inherits and ignores permissions: the exit's parent may hold it.
	/// </summary>
	[Test]
	public async Task VariableExit_TakesItsDestinationFromItsParent()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("vexit");
		var who = await MortalInOwnRoom(token);
		var target = DBRef.Parse((await Run(who, $"@dig Target_{token}")).Message.ToPlainText().Trim());

		var exit = DBRef.Parse((await Run(who, $"@open VarExit_{token}")).Message.ToPlainText().Trim());
		await Run(who, $"@link {exit}=variable");
		var code = await Create(who, $"VarCode_{token}");
		await Run(who, $"&DESTINATION {code}=#{target.Number}");
		await Run(who, $"@parent {exit}={code}");

		await Run(who, $"VarExit_{token}");
		await Run(who, $"think WHERE_{token}:[loc(me)]");

		await Assert.That(string.Join("\n", Heard(who))).Contains($"WHERE_{token}:#{target.Number}");
	}

	/// <summary><c>do_cpattr</c> copies only the source's own attribute (<c>set.c:723</c>).</summary>
	[Test]
	public async Task Cpattr_RefusesAnInheritedSource()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("cpattr");
		var who = await MortalInOwnRoom(token);
		var (_, child) = await ParentAndChild(who, token, $"VALUE_{token}");

		await Run(who, $"@cpattr {child}/FOO=me/COPY");
		await Run(who, $"@mvattr {child}/FOO=me/MOVED");

		await Assert.That(await OwnValue(who.Player.DbRef, "COPY")).IsNull();
		await Assert.That(await OwnValue(who.Player.DbRef, "MOVED")).IsNull();
	}

	/// <summary>
	/// <c>do_atrchown</c> works only on the object's own attribute (<c>attrib.c:2597</c>): an inherited one is
	/// "No such attribute.", and no copy appears on the child.
	/// </summary>
	[Test]
	public async Task Atrchown_RefusesAnInheritedAttribute()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("atrchown");
		var who = await MortalInOwnRoom(token);
		var (_, child) = await ParentAndChild(who, token, $"VALUE_{token}");

		var before = WebAppFactoryArg.Notifications.CountFor(who.Player.DbRef);
		await Run(who, $"@atrchown {child}/FOO=me");

		await Assert.That(Heard(who).Skip(before)).Contains("No such attribute.");
		await Assert.That(await OwnValue(child, "FOO")).IsNull();
	}

	/// <summary>
	/// <c>examine/parent</c> lists through <c>atr_iter_get_parent</c> (<c>look.c:380</c>) and prints an inherited
	/// attribute as <c>#&lt;parent&gt;/NAME</c> (<c>look.c:353-358</c>), with or without a pattern.
	/// </summary>
	[Test]
	public async Task ExamineParent_ListsInheritedAttributesWithTheirSource()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("exparent");
		var who = await MortalInOwnRoom(token);
		var (parent, child) = await ParentAndChild(who, token, $"VALUE_{token}");
		await Run(who, $"&OWN {child}=MINE_{token}");

		var before = WebAppFactoryArg.Notifications.CountFor(who.Player.DbRef);
		await Run(who, $"examine/parent {child}");
		var whole = Heard(who).Skip(before).ToList();
		await Assert.That(whole).Contains((string line) => line.StartsWith($"#{parent.Number}/FOO [") && line.EndsWith($"VALUE_{token}"));
		await Assert.That(whole).Contains((string line) => line.StartsWith("OWN [") && line.EndsWith($"MINE_{token}"));

		before = WebAppFactoryArg.Notifications.CountFor(who.Player.DbRef);
		await Run(who, $"examine/parent {child}/FOO");
		await Assert.That(Heard(who).Skip(before))
			.Contains((string line) => line.StartsWith($"#{parent.Number}/FOO [") && line.EndsWith($"VALUE_{token}"));

		before = WebAppFactoryArg.Notifications.CountFor(who.Player.DbRef);
		await Run(who, $"examine {child}");
		await Assert.That(Heard(who).Skip(before)).DoesNotContain((string line) => line.Contains($"VALUE_{token}"));
	}

	/// <summary>
	/// A name without wildcards is looked up as <c>atr_get</c> would (<c>attrib.c:1524-1526</c>), so
	/// <c>examine/parent</c> finds an attribute on the type ancestor too.
	/// </summary>
	[Test]
	[NotInParallel]
	public async Task ExamineParent_FindsANamedAttributeOnTheAncestor()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("exanc");
		var attribute = $"EXANC_{token}".ToUpperInvariant();
		var who = await MortalInOwnRoom(token);
		var thing = await Create(who, $"T_{token}");

		await God($"&{attribute} {AncestorThing}=ANCESTRAL_{token}");
		try
		{
			var before = WebAppFactoryArg.Notifications.CountFor(who.Player.DbRef);
			await Run(who, $"examine/parent {thing}/{attribute}");
			await Assert.That(Heard(who).Skip(before)).Contains((string line) =>
				line.StartsWith($"#{AncestorThing.Number}/{attribute} [") && line.EndsWith($"ANCESTRAL_{token}"));
		}
		finally
		{
			await God($"&{attribute} {AncestorThing}=");
		}
	}

	/// <summary>
	/// <c>get_doing</c> (<c>bsd.c:6251-6255</c>) fetches DOING with <c>UFUN_IGNORE_PERMS</c> and runs it, so
	/// @doing is evaluated and found through <c>atr_get</c>. DOING is <c>AF_PRIVATE</c> in the standard
	/// table (<c>atr_tab.h:61</c>), so a parent's copy counts only once that flag is cleared.
	/// <c>who_check_name</c> (<c>bsd.c:5619</c>) matches a wildcard pattern against the aliases too.
	/// </summary>
	[Test]
	public async Task Doing_EvaluatesDoingFoundAsAtrGetFindsItAndMatchesAliases()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("doing");
		var who = await MortalInOwnRoom(token);
		var code = await Create(who, $"DoingCode_{token}");
		await Run(who, $"&DOING {code}=BUSY_{token} [add(1,1)]");
		await Run(who, $"@parent me={code}");
		var alias = $"A{Guid.NewGuid():N}"[..9];
		await Run(who, $"@alias me={alias}");

		var before = WebAppFactoryArg.Notifications.CountFor(who.Player.DbRef);
		await Run(who, $"DOING {alias}*");
		var privateDoing = string.Join("\n", Heard(who).Skip(before));
		await Assert.That(privateDoing).Contains(who.Player.Name);
		await Assert.That(privateDoing).DoesNotContain($"BUSY_{token}");

		await Run(who, $"@set {code}/DOING=!no_inherit");
		before = WebAppFactoryArg.Notifications.CountFor(who.Player.DbRef);
		await Run(who, $"DOING {alias}*");
		await Assert.That(string.Join("\n", Heard(who).Skip(before))).Contains($"BUSY_{token} 2");
	}

	/// <summary>Every @wcheck attribute test is an <c>atr_get</c> (<c>warnings.c:76-195</c>).</summary>
	[Test]
	public async Task Wcheck_CountsAnInheritedDescription()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("wcheck");
		var who = await MortalInOwnRoom(token);
		var parent = await Create(who, $"DescParent_{token}");
		await Run(who, $"@desc {parent}=A thing.");
		var described = await Create(who, $"Described_{token}");
		await Run(who, $"@parent {described}={parent}");
		var bare = await Create(who, $"Bare_{token}");
		await Run(who, $"drop {described}");
		await Run(who, $"drop {bare}");
		await Run(who, $"@warnings {described}=thing-desc");
		await Run(who, $"@warnings {bare}=thing-desc");

		var before = WebAppFactoryArg.Notifications.CountFor(who.Player.DbRef);
		await Run(who, $"@wcheck {described}");
		await Run(who, $"@wcheck {bare}");

		var heard = Heard(who).Skip(before).ToList();
		await Assert.That(heard).Contains((string line) => line.Contains($"Bare_{token}") && line.Contains("thing-desc"));
		await Assert.That(heard).DoesNotContain((string line) => line.Contains($"Described_{token}") && line.Contains("thing-desc"));
	}

	/// <summary>
	/// <c>get_gender</c> is a bare <c>atr_get</c> (<c>funstr.c:70</c>): inherited, and with no permission
	/// check, so a SEX the player could not read itself still sets its pronouns.
	/// </summary>
	[Test]
	public async Task Pronouns_ReadAnInheritedSexWithoutAPermissionCheck()
	{
		var token = TestIsolationHelpers.GenerateUniqueName("sex");
		var who = await MortalInOwnRoom(token);
		var parent = DBRef.Parse((await GodParser.CommandParse(1, ConnectionService,
			MarkupText.Plain($"@create SexParent_{token}"))).Message.ToPlainText().Trim());
		var gender = WebAppFactoryArg.Services.GetRequiredService<IOptionsWrapper<SharpMUSHOptions>>().CurrentValue.Attribute.GenderAttribute;
		await God($"&{gender} {parent}=Female");
		await God($"@set {parent}/{gender}=mortal_dark");
		await God($"@parent #{who.Player.DbRef.Number}={parent}");

		await Run(who, $"think PRONOUN_{token}:[subj(me)]");

		await Assert.That(Heard(who)).Contains($"PRONOUN_{token}:she");
	}
}
