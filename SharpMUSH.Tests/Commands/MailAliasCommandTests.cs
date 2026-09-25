using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Tests.Commands;

/// <summary>
/// <c>@malias</c>, <c>malias()</c> and mail to a <c>+alias</c>, against PennMUSH's <c>src/malias.c</c>
/// and <c>send_mail_alias</c> (<c>src/extmail.c</c>). Each test makes its own players and alias, since
/// alias names are one namespace across the shared world.
/// </summary>
public class MailAliasCommandTests
{
	[ClassDataSource<ServerWebAppFactory>(Shared = SharedType.PerTestSession)]
	public required ServerWebAppFactory WebAppFactoryArg { get; init; }

	private IConnectionService ConnectionService => WebAppFactoryArg.Services.GetRequiredService<IConnectionService>();
	private IMediator Mediator => WebAppFactoryArg.Services.GetRequiredService<IMediator>();

	private Task<TestIsolationHelpers.TestPlayer> PlayerAsync(string prefix)
		=> TestIsolationHelpers.CreateTestPlayerWithHandleAsync(WebAppFactoryArg.Services, Mediator, ConnectionService, prefix);

	private async Task<string[]> RunAsync(TestIsolationHelpers.TestPlayer player, string command)
	{
		var before = NotificationsTo(player.DbRef).Length;
		await WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.CommandParse(player.Handle, ConnectionService, MarkupText.Plain(command));
		return [.. NotificationsTo(player.DbRef).Skip(before)];
	}

	private async Task<string> EvaluateAsync(TestIsolationHelpers.TestPlayer player, string expression)
		=> (await WebAppFactoryArg.CommandParserFor(player.DbRef, player.Handle)
			.FunctionParse(MarkupText.Plain(expression)))!.Message!.ToPlainText();

	private static string UniqueAlias(string stem) => $"+{stem}{Guid.NewGuid().ToString("N")[..6]}";

	/// <summary>Alias owner, one member and one outsider, with the alias made by the owner.</summary>
	private async Task<(TestIsolationHelpers.TestPlayer Owner, TestIsolationHelpers.TestPlayer Member,
		TestIsolationHelpers.TestPlayer Outsider, string Alias)> AliasAsync(string stem)
	{
		var owner = await PlayerAsync($"{stem}Own");
		var member = await PlayerAsync($"{stem}Mem");
		var outsider = await PlayerAsync($"{stem}Out");
		var alias = UniqueAlias("A");

		var created = await RunAsync(owner, $"@malias {alias}=#{owner.DbRef.Number} #{member.DbRef.Number}");
		await Assert.That(created).Contains($"MAIL: Alias set '{alias}' defined.");

		return (owner, member, outsider, alias);
	}

	[Test]
	public async ValueTask CreatingReportsEachMemberAndRejectsWhatIsNoPlayer()
	{
		var owner = await PlayerAsync("MalCreate");
		var alias = UniqueAlias("C");

		var output = await RunAsync(owner, $"@malias/create {alias}=me NoSuchPlayerHere");

		await Assert.That(output).Contains(m => m.StartsWith("MAIL: ") && m.EndsWith($" added to alias {alias}"));
		await Assert.That(output).Contains("MAIL: No such player 'NoSuchPlayerHere'.");
		await Assert.That(output).Contains($"MAIL: Alias set '{alias}' defined.");

		await Assert.That(await RunAsync(owner, $"@malias {alias}=me"))
			.Contains($"MAIL: Mail Alias '{alias}' already exists.");
		await Assert.That(await RunAsync(owner, "@malias NoToken=me"))
			.Contains("MAIL: All Mail aliases must begin with '+'.");
		await Assert.That(await RunAsync(owner, "@malias +bad!name=me"))
			.Contains("MAIL: Invalid character in mail alias.");
		await Assert.That(await RunAsync(owner, $"@malias {UniqueAlias("N")}=NobodyAtAll"))
			.Contains("MAIL: No valid recipients for alias-list!");
	}

	/// <summary>
	/// A new alias may be used by its owner and members (<c>nflags</c> Owner|Members) and its members seen
	/// only by its owner (<c>mflags</c> Owner). Admin see the members of any alias they can find.
	/// </summary>
	[Test]
	public async ValueTask MembersAreShownToTheOwnerOnly()
	{
		var (owner, member, outsider, alias) = await AliasAsync("MalSee");

		await Assert.That(await RunAsync(owner, $"@malias/members {alias}"))
			.Contains($"MAIL: Alias {alias}: {owner.Name}, {member.Name}");
		await Assert.That(await RunAsync(member, $"@malias {alias}")).Contains("MAIL: Permission denied.");
		await Assert.That(await RunAsync(outsider, $"@malias/who {alias}")).Contains($"MAIL: Alias '{alias}' not found.");

		await Assert.That(await EvaluateAsync(owner, $"malias({alias})"))
			.IsEqualTo($"#{owner.DbRef.Number} #{member.DbRef.Number}");
		await Assert.That(await EvaluateAsync(owner, $"malias({alias},|)"))
			.IsEqualTo($"#{owner.DbRef.Number}|#{member.DbRef.Number}");
		await Assert.That(await EvaluateAsync(member, $"malias({alias})")).IsEqualTo("#-1 PERMISSION DENIED");
		await Assert.That(await EvaluateAsync(outsider, $"malias({alias})")).IsEqualTo("#-1 NO MATCH");
	}

	[Test]
	public async ValueTask TheListingShowsWhatEachPlayerMayUse()
	{
		var (owner, member, outsider, alias) = await AliasAsync("MalList");

		await Assert.That((await EvaluateAsync(owner, "malias()")).Split(' ')).Contains(alias);
		await Assert.That((await EvaluateAsync(member, "malias(|)")).Split('|')).Contains(alias);
		await Assert.That((await EvaluateAsync(outsider, "malias()")).Split(' ')).DoesNotContain(alias);

		var listing = await RunAsync(owner, "@malias/list");
		// "%c%-12.12s %-35.35s %s %-15.15s": the owner's name is cut to fifteen characters.
		await Assert.That(listing).Contains(m => m.StartsWith(alias) && m.Contains(" M-  -O ")
			&& m.Contains(owner.Name[..Math.Min(15, owner.Name.Length)]));
		await Assert.That(listing).Contains(m => m.StartsWith("Name          Alias Description"));
		await Assert.That(listing).Contains("*****  End of Mail Aliases *****");
	}

	/// <summary>
	/// send_mail_alias: a member who may not see the list is told the alias was mailed and the send goes
	/// silent; every member still gets the message.
	/// </summary>
	[Test]
	public async ValueTask AMemberMailsTheAliasWithoutSeeingWhoIsOnIt()
	{
		var (owner, member, _, alias) = await AliasAsync("MalSend");

		var output = await RunAsync(member, $"@mail {alias}=Alias subject/Alias body");

		await Assert.That(output).Contains($"You sent your message to the '{alias[1..]}' alias");
		await Assert.That(output).DoesNotContain(m => m.StartsWith($"MAIL: You sent your message to {owner.Name}"));
		await Assert.That(NotificationsTo(owner.DbRef)).Contains(m => m.StartsWith("MAIL: You have a new message"));
		await Assert.That(NotificationsTo(member.DbRef)).Contains(m => m.StartsWith("MAIL: You have a new message"));
	}

	[Test]
	public async ValueTask TheOwnerMailsTheAliasAndIsToldOfEachDelivery()
	{
		var (owner, member, _, alias) = await AliasAsync("MalOwnSend");

		var output = await RunAsync(owner, $"@mail {alias}=Owner subject/Owner body");

		await Assert.That(output).Contains($"MAIL: You sent your message to {member.Name}.");
		await Assert.That(output).DoesNotContain(m => m.Contains("' alias"));
	}

	[Test]
	public async ValueTask AnOutsiderCannotMailTheAlias()
	{
		var (_, member, outsider, alias) = await AliasAsync("MalDeny");
		var memberMailBefore = NotificationsTo(member.DbRef).Count(m => m.StartsWith("MAIL: You have a new message"));

		var output = await RunAsync(outsider, $"@mail {alias}=Denied/Denied body");

		await Assert.That(output).Contains($"No such unique player: {alias}.");
		await Assert.That(output).DoesNotContain(m => m.StartsWith("MAIL: You sent your message to"));
		await Assert.That(NotificationsTo(member.DbRef).Count(m => m.StartsWith("MAIL: You have a new message")))
			.IsEqualTo(memberMailBefore);
	}

	/// <summary>@malias/useflag with no privileges opens the alias to everyone (<c>nflags</c> 0).</summary>
	[Test]
	public async ValueTask UseFlagsDecideWhoMayMail()
	{
		var (owner, _, outsider, alias) = await AliasAsync("MalUse");

		await Assert.That(await RunAsync(outsider, $"@malias/useflag {alias}=")).Contains("MAIL: I cannot find that alias!");
		await Assert.That(await RunAsync(owner, $"@malias/useflag {alias}=M"))
			.Contains($"MAIL: Permission to see/use alias '{alias}' changed to Members");
		await Assert.That(await RunAsync(owner, $"@malias/seeflag {alias}=admin !owner members"))
			.Contains($"MAIL: Permission to see/use alias '{alias}' changed to Admin Members");
		await Assert.That(await RunAsync(owner, $"@malias/useflag {alias}="))
			.Contains(m => m.TrimEnd() == $"MAIL: Permission to see/use alias '{alias}' changed to");

		var output = await RunAsync(outsider, $"@mail {alias}=Open/Open body");

		await Assert.That(NotificationsTo(outsider.DbRef)).DoesNotContain($"No such unique player: {alias}.");
		await Assert.That(output).Contains($"You sent your message to the '{alias[1..]}' alias");
	}

	[Test]
	public async ValueTask OnlyTheOwnerChangesTheAlias()
	{
		var (owner, member, outsider, alias) = await AliasAsync("MalEdit");

		await Assert.That(await RunAsync(member, $"@malias/add {alias}=#{outsider.DbRef.Number}")).Contains("Permission denied.");
		await Assert.That(await RunAsync(member, $"@malias/describe {alias}=Mine now")).Contains("MAIL: Permission denied.");
		await Assert.That(await RunAsync(member, $"@malias/destroy {alias}")).Contains("MAIL: Permission denied!");
		await Assert.That(await RunAsync(member, $"@malias/chown {alias}=me")).Contains("MAIL: You cannot do that!");

		var added = await RunAsync(owner, $"@malias/add {alias}=#{outsider.DbRef.Number} #{member.DbRef.Number}");
		await Assert.That(added).Contains($"MAIL: player '#{member.DbRef.Number}' exists already in alias {alias}.");
		await Assert.That(added).Contains($"MAIL: Alias set '{alias}' redefined.");
		await Assert.That(await EvaluateAsync(owner, $"malias({alias})"))
			.IsEqualTo($"#{owner.DbRef.Number} #{member.DbRef.Number} #{outsider.DbRef.Number}");

		// do_malias_remove moves the last member into the removed one's place.
		var removed = await RunAsync(owner, $"@malias/remove {alias}=#{owner.DbRef.Number}");
		await Assert.That(removed).Contains(m => m.EndsWith($" removed from alias {alias}"));
		await Assert.That(await EvaluateAsync(owner, $"malias({alias})"))
			.IsEqualTo($"#{outsider.DbRef.Number} #{member.DbRef.Number}");

		await Assert.That(await RunAsync(owner, $"@malias/describe {alias}=The edit crew")).Contains("MAIL: Description changed.");

		var renamed = UniqueAlias("R");
		await Assert.That(await RunAsync(owner, $"@malias/rename {alias}={renamed}")).Contains("MAIL: Mail Alias renamed.");
		await Assert.That(await RunAsync(owner, $"@malias/members {alias}")).Contains($"MAIL: Alias '{alias}' not found.");
		await Assert.That(await RunAsync(owner, $"@malias/destroy {renamed}")).Contains("MAIL: Alias Destroyed.");
		await Assert.That(await EvaluateAsync(owner, $"malias({renamed})")).IsEqualTo("#-1 NO MATCH");
	}

	/// <summary>The short switches the help documents; Penn reaches them as prefixes of the long ones.</summary>
	[Test]
	public async ValueTask TheDocumentedShortSwitchesWork()
	{
		var (owner, member, outsider, alias) = await AliasAsync("MalShort");

		await Assert.That(await RunAsync(owner, $"@malias/desc {alias}=Short crew")).Contains("MAIL: Description changed.");
		await Assert.That(await RunAsync(owner, $"@malias/see {alias}=members"))
			.Contains($"MAIL: Permission to see/use alias '{alias}' changed to Members");
		await Assert.That(await RunAsync(member, $"@malias/members {alias}"))
			.Contains($"MAIL: Alias {alias}: {owner.Name}, {member.Name}");
		await Assert.That(await RunAsync(owner, $"@malias/use {alias}="))
			.Contains(m => m.StartsWith($"MAIL: Permission to see/use alias '{alias}' changed to"));
		await Assert.That((await EvaluateAsync(outsider, "malias()")).Split(' ')).Contains(alias);
		await Assert.That(await RunAsync(outsider, "@malias/stat")).Contains("MAIL: Permission denied.");
	}

	[Test]
	public async ValueTask MortalsCannotStatOrNuke()
	{
		var mortal = await PlayerAsync("MalMortal");

		await Assert.That(await RunAsync(mortal, "@malias/stats")).Contains("MAIL: Permission denied.");
		await Assert.That(await RunAsync(mortal, "@malias/nuke")).Contains("MAIL: Only god can do that!");
		await Assert.That(await RunAsync(mortal, "@malias =me")).Contains("MAIL: Invalid malias command.");
	}

	/// <summary>The recipient-keyed recorder: the shared substitute's ReceivedCalls() is unsafe while parallel tests record.</summary>
	private string[] NotificationsTo(DBRef target) => [.. WebAppFactoryArg.Notifications.For(target)];
}
