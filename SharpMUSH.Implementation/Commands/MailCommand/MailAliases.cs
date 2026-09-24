using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Common;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Globalization;

namespace SharpMUSH.Implementation.Commands.MailCommand;

/// <summary>
/// PennMUSH's global mail aliases (<c>src/malias.c</c>): <c>@malias</c>, <c>malias()</c>, and the
/// <c>+alias</c> recipient of <c>@mail</c>. Every message and permission rule is Penn's, cited by function.
/// </summary>
/// <remarks>
/// One divergence: Penn destroys an alias by moving the last one into its slot, so <c>@malias/all</c>
/// numbers shift; here aliases keep their creation order.
/// </remarks>
public static class MailAliases
{
	public sealed record Services(IMediator Mediator, INotifyService Notify, IPermissionService Permissions);

	/// <summary>The players a <c>+alias</c> recipient mails, and whether the send must go silent.</summary>
	public sealed record Recipients(SharpPlayer[] Members, bool Silent);

	/// <summary><c>MALIAS_TOKEN</c> (<c>hdrs/malias.h:13</c>).</summary>
	public const char Token = '+';

	/// <summary>do_malias_create's cap on one list.</summary>
	private const int MaxMembers = 100;

	/// <summary>do_malias_create — besides letters and digits, what an alias name may contain.</summary>
	private const string LegalPunctuation = "`$_-.'";

	/// <summary><c>malias_priv_table</c>: name, letter and bit, in Penn's order.</summary>
	private static readonly (string Name, char Letter, MailAliasPrivileges Bit)[] PrivilegeTable =
	[
		("Admin", 'A', MailAliasPrivileges.Admin),
		("Members", 'M', MailAliasPrivileges.Members),
		("Owner", 'O', MailAliasPrivileges.Owner)
	];

	private static bool IsMember(SharpMailAlias alias, AnySharpObject who)
		=> alias.Members.Contains(who.Object().DBRef.Number);

	private static bool IsOwner(SharpMailAlias alias, AnySharpObject who)
		=> alias.Owner == who.Object().DBRef.Number;

	/// <summary>
	/// <c>get_malias</c>'s gate, which send_mail_alias also applies before mailing: the owner, anyone for an
	/// alias with no use privileges, any admin, or a member when members may use it.
	/// </summary>
	private static async ValueTask<bool> MayUseAsync(SharpMailAlias alias, AnySharpObject who)
		=> IsOwner(alias, who)
			|| alias.UsePrivileges == MailAliasPrivileges.Everyone
			|| await who.IsPriv()
			|| (alias.UsePrivileges.HasFlag(MailAliasPrivileges.Members) && IsMember(alias, who));

	/// <summary>
	/// do_malias_list and fun_malias's listing gate. Unlike <see cref="MayUseAsync"/>, an admin sees an
	/// alias only when its use privileges include Admin.
	/// </summary>
	private static async ValueTask<bool> IsListedForAsync(SharpMailAlias alias, AnySharpObject who)
		=> IsOwner(alias, who)
			|| alias.UsePrivileges == MailAliasPrivileges.Everyone
			|| (alias.UsePrivileges.HasFlag(MailAliasPrivileges.Admin) && await who.IsPriv())
			|| (alias.UsePrivileges.HasFlag(MailAliasPrivileges.Members) && IsMember(alias, who));

	/// <summary>do_malias_members, fun_malias and send_mail_alias: who may see the member list.</summary>
	private static async ValueTask<bool> MaySeeMembersAsync(SharpMailAlias alias, AnySharpObject who)
		=> IsOwner(alias, who)
			|| alias.SeePrivileges == MailAliasPrivileges.Everyone
			|| await who.IsPriv()
			|| (alias.SeePrivileges.HasFlag(MailAliasPrivileges.Members) && IsMember(alias, who));

	private static IAsyncEnumerable<SharpMailAlias> AllAsync(Services services)
		=> services.Mediator.CreateStream(new GetMailAliasesQuery());

	/// <summary>
	/// <c>get_malias</c>: the alias <paramref name="name"/> (with its <c>+</c>) names, if
	/// <paramref name="who"/> may use it; <paramref name="who"/> null is GOD, who sees every alias.
	/// </summary>
	private static async ValueTask<SharpMailAlias?> FindAsync(Services services, AnySharpObject? who, string name)
	{
		if (name.Length == 0 || name[0] != Token)
		{
			return null;
		}

		var bare = name[1..];
		await foreach (var alias in AllAsync(services))
		{
			if ((who is null || await MayUseAsync(alias, who))
					&& alias.Name.Equals(bare, StringComparison.OrdinalIgnoreCase))
			{
				return alias;
			}
		}

		return null;
	}

	/// <summary>
	/// send_mail_alias (<c>extmail.c:3248</c>): the members a <c>+alias</c> recipient mails, or
	/// <see cref="NotFound"/> when <paramref name="name"/> is not an alias <paramref name="sender"/> may use.
	/// A sender who may not see the members is told the alias was mailed, and the send goes silent.
	/// </summary>
	public static async ValueTask<Found<Recipients>> RecipientsAsync(Services services, AnySharpObject sender,
		string name)
	{
		if (await FindAsync(services, sender, name) is not { } alias)
		{
			return new NotFound();
		}

		var silent = false;
		if (!await MaySeeMembersAsync(alias, sender))
		{
			silent = true;
			await services.Notify.Notify(sender, $"You sent your message to the '{alias.Name}' alias", sender);
		}

		var members = new List<SharpPlayer>();
		foreach (var member in alias.Members)
		{
			if (await services.Mediator.Send(new GetObjectNodeQuery(new DBRef(member))) is AnySharpObject and SharpPlayer player)
			{
				members.Add(player);
			}
		}

		return new Recipients([.. members], silent);
	}

	/// <summary>cmd_malias (<c>src/cmds.c:1052</c>): the switch picks the action, the first one Penn tests winning.</summary>
	/// <remarks>
	/// Penn takes any unique switch prefix; SharpMUSH matches switches exactly, so the short forms the help
	/// documents (<c>/desc</c>, <c>/stat</c>, <c>/use</c>, <c>/see</c>) are registered as switches of their own.
	/// </remarks>
	public static async ValueTask<MString> Handle(Services services, AnySharpObject executor, string[] switches,
		string left, string right)
	{
		bool Has(string name) => switches.Contains(name, StringComparer.OrdinalIgnoreCase);

		if (Has("LIST")) await ListAsync(services, executor);
		else if (Has("ALL")) await AllListAsync(services, executor);
		else if (Has("MEMBERS") || Has("WHO")) await MembersAsync(services, executor, left);
		else if (Has("CREATE")) await CreateAsync(services, executor, left, right);
		else if (Has("SET")) await SetAsync(services, executor, left, right);
		else if (Has("DESTROY")) await DestroyAsync(services, executor, left);
		else if (Has("ADD")) await AddAsync(services, executor, left, right);
		else if (Has("REMOVE")) await RemoveAsync(services, executor, left, right);
		else if (Has("DESCRIBE") || Has("DESC")) await DescribeAsync(services, executor, left, right);
		else if (Has("RENAME")) await RenameAsync(services, executor, left, right);
		else if (Has("STATS") || Has("STAT")) await StatsAsync(services, executor);
		else if (Has("CHOWN")) await ChownAsync(services, executor, left, right);
		else if (Has("USEFLAG") || Has("USE")) await PrivilegesAsync(services, executor, left, right, members: false);
		else if (Has("SEEFLAG") || Has("SEE")) await PrivilegesAsync(services, executor, left, right, members: true);
		else if (Has("NUKE")) await NukeAsync(services, executor);
		else await DefaultAsync(services, executor, left, right);

		return MarkupText.Empty;
	}

	private static ValueTask Tell(Services services, AnySharpObject executor, string message)
		=> services.Notify.Notify(executor, message, executor);

	/// <summary>do_malias: no switch lists, lists one alias's members, or creates.</summary>
	private static async ValueTask DefaultAsync(Services services, AnySharpObject executor, string left, string right)
	{
		if (left.Length == 0)
		{
			if (right.Length > 0)
			{
				await Tell(services, executor, "MAIL: Invalid malias command.");
				return;
			}

			await ListAsync(services, executor);
			return;
		}

		if (right.Length > 0)
		{
			await CreateAsync(services, executor, left, right);
		}
		else
		{
			await MembersAsync(services, executor, left);
		}
	}

	private static async ValueTask<string> OwnerNameAsync(Services services, int owner)
		=> await services.Mediator.Send(new GetObjectNodeQuery(new DBRef(owner))) is AnySharpObject found
			? found.Object().Name
			: "*NOTHING*";

	/// <summary>A printf <c>%-N.Ns</c>: cut to <paramref name="width"/>, then padded to it.</summary>
	private static string Column(string text, int width)
		=> (text.Length > width ? text[..width] : text).PadRight(width);

	/// <summary>do_malias_list.</summary>
	private static async ValueTask ListAsync(Services services, AnySharpObject executor)
	{
		var notified = false;
		await foreach (var alias in AllAsync(services))
		{
			if (!await IsListedForAsync(alias, executor))
			{
				continue;
			}

			if (!notified)
			{
				await Tell(services, executor,
					$"{Column("Name", 13)} {Column("Alias Description", 35)} Use See {Column("Owner", 15)}");
				notified = true;
			}

			await Tell(services, executor,
				$"{Token}{Column(alias.Name, 12)} {Column(alias.Description, 35)} {ShortPrivileges(alias)} {Column(await OwnerNameAsync(services, alias.Owner), 15)}");
		}

		await Tell(services, executor, "*****  End of Mail Aliases *****");
	}

	/// <summary>do_malias_all: every alias, numbered, for admin; anyone else gets the ordinary list.</summary>
	private static async ValueTask AllListAsync(Services services, AnySharpObject executor)
	{
		if (!await executor.IsPriv())
		{
			await ListAsync(services, executor);
			return;
		}

		await Tell(services, executor, "Num   Name       Description                              Owner       Count");

		var index = 0;
		await foreach (var alias in AllAsync(services))
		{
			await Tell(services, executor,
				$"#{index++,-4} {Token}{Column(alias.Name, 10)} {Column(alias.Description, 40)} {Column(await OwnerNameAsync(services, alias.Owner), 11)} ({alias.Members.Length,3})");
		}

		await Tell(services, executor, "***** End of Mail Aliases *****");
	}

	/// <summary>do_malias_members.</summary>
	private static async ValueTask MembersAsync(Services services, AnySharpObject executor, string name)
	{
		if (await FindAsync(services, executor, name) is not { } alias)
		{
			await Tell(services, executor, $"MAIL: Alias '{name}' not found.");
			return;
		}

		if (!await MaySeeMembersAsync(alias, executor))
		{
			await Tell(services, executor, "MAIL: Permission denied.");
			return;
		}

		var names = new List<string>();
		foreach (var member in alias.Members)
		{
			names.Add(await OwnerNameAsync(services, member));
		}

		await Tell(services, executor, $"MAIL: Alias {Token}{alias.Name}: {string.Join(", ", names)}");
	}

	/// <summary>do_malias_create.</summary>
	private static async ValueTask CreateAsync(Services services, AnySharpObject executor, string name, string list)
	{
		if (!executor.IsPlayer)
		{
			await Tell(services, executor, "MAIL: Only players may create mail aliases.");
			return;
		}

		if (name.Length == 0 || list.Length == 0)
		{
			await Tell(services, executor, "MAIL: What alias do you want to create?");
			return;
		}

		if (name[0] != Token)
		{
			await Tell(services, executor, $"MAIL: All Mail aliases must begin with '{Token}'.");
			return;
		}

		if (name.Skip(1).Any(c => !char.IsAsciiLetterOrDigit(c) && !LegalPunctuation.Contains(c)))
		{
			await Tell(services, executor, "MAIL: Invalid character in mail alias.");
			return;
		}

		if (await FindAsync(services, null, name) is not null)
		{
			await Tell(services, executor, $"MAIL: Mail Alias '{name}' already exists.");
			return;
		}

		var members = await ResolveListAsync(services, executor, name, list, existing: null);
		if (members.Count == 0)
		{
			return;
		}

		var bare = name[1..];
		var created = await services.Mediator.Send(new CreateMailAliasCommand(new SharpMailAlias(bare, bare,
			executor.Object().DBRef.Number, [.. members], MailAliasPrivileges.Owner | MailAliasPrivileges.Members,
			MailAliasPrivileges.Owner)));

		await Tell(services, executor, created is SharpMailAlias
			? $"MAIL: Alias set '{name}' defined."
			: $"MAIL: Mail Alias '{name}' already exists.");
	}

	/// <summary>do_malias_set: replaces the member list.</summary>
	private static async ValueTask SetAsync(Services services, AnySharpObject executor, string name, string list)
	{
		if (await FindAsync(services, executor, name) is not { } alias)
		{
			await Tell(services, executor, $"MAIL: Not a valid alias. Remember to prefix the alias name with {Token}.");
			return;
		}

		if (list.Length == 0)
		{
			await Tell(services, executor, "MAIL: You must set the alias to a non-empty list.");
			return;
		}

		if (!await MayManageAsync(alias, executor))
		{
			await Tell(services, executor, "MAIL: Permission denied!");
			return;
		}

		var members = await ResolveListAsync(services, executor, name, list, existing: null);
		if (members.Count == 0)
		{
			return;
		}

		if (!await UpdateAsync(services, executor, alias, alias with { Members = [.. members] }))
		{
			return;
		}

		await Tell(services, executor, "MAIL: Alias list set.");
	}

	/// <summary>
	/// Writes a changed alias. Another command can destroy or rename the alias between its read and this
	/// write; then this says so and returns false, so the caller reports no success.
	/// </summary>
	private static async ValueTask<bool> UpdateAsync(Services services, AnySharpObject executor, SharpMailAlias alias,
		SharpMailAlias updated)
	{
		if (await services.Mediator.Send(new UpdateMailAliasCommand(alias.Name, updated)) is SharpMailAlias)
		{
			return true;
		}

		await Tell(services, executor, $"MAIL: Mail Alias '{Token}{alias.Name}' not found.");
		return false;
	}

	/// <summary>The owner, or a wizard: who may change an alias.</summary>
	private static async ValueTask<bool> MayManageAsync(SharpMailAlias alias, AnySharpObject executor)
		=> IsOwner(alias, executor) || await executor.IsWizard();

	/// <summary>do_malias_destroy.</summary>
	private static async ValueTask DestroyAsync(Services services, AnySharpObject executor, string name)
	{
		if (await FindAsync(services, executor, name) is not { } alias)
		{
			await Tell(services, executor, $"MAIL: Not a valid alias. Remember to prefix the alias name with {Token}.");
			return;
		}

		if (!await MayManageAsync(alias, executor))
		{
			await Tell(services, executor, "MAIL: Permission denied!");
			return;
		}

		await Tell(services, executor, "MAIL: Alias Destroyed.");
		await services.Mediator.Send(new DeleteMailAliasCommand(alias.Name));
	}

	/// <summary>do_malias_add: appends players not already on the alias.</summary>
	private static async ValueTask AddAsync(Services services, AnySharpObject executor, string name, string list)
	{
		if (await FindAsync(services, executor, name) is not { } alias)
		{
			await Tell(services, executor, $"MAIL: Mail Alias '{name}' not found.");
			return;
		}

		if (!await MayManageAsync(alias, executor))
		{
			await Tell(services, executor, "Permission denied.");
			return;
		}

		var added = await ResolveListAsync(services, executor, name, list, existing: alias);
		if (added.Count == 0)
		{
			return;
		}

		if (!await UpdateAsync(services, executor, alias,
			alias with { Members = [.. alias.Members, .. added] }))
		{
			return;
		}

		await Tell(services, executor, $"MAIL: Alias set '{name}' redefined.");
	}

	/// <summary>do_malias_remove: Penn moves the last member into a removed one's place, and so does this.</summary>
	private static async ValueTask RemoveAsync(Services services, AnySharpObject executor, string name, string list)
	{
		if (await FindAsync(services, executor, name) is not { } alias)
		{
			await Tell(services, executor, $"MAIL: Mail Alias '{name}' not found.");
			return;
		}

		if (!await MayManageAsync(alias, executor))
		{
			await Tell(services, executor, "Permission denied.");
			return;
		}

		var members = alias.Members.ToList();
		foreach (var entry in SplitList(list))
		{
			if (await ResolvePlayerAsync(services, executor, entry) is not { } target)
			{
				await Tell(services, executor, $"MAIL: No such player '{entry}'.");
				continue;
			}

			var index = members.IndexOf(target.Object.DBRef.Number);
			if (index < 0)
			{
				await Tell(services, executor, $"MAIL: player '{entry}' is not in alias {name}.");
				continue;
			}

			members[index] = members[^1];
			members.RemoveAt(members.Count - 1);
			await Tell(services, executor,
				$"MAIL: {await UnparseAsync(services, executor, target)} removed from alias {name}");
		}

		if (!await UpdateAsync(services, executor, alias, alias with { Members = [.. members] }))
		{
			return;
		}

		await Tell(services, executor, $"MAIL: Alias set '{name}' redefined.");
	}

	/// <summary>do_malias_desc.</summary>
	private static async ValueTask DescribeAsync(Services services, AnySharpObject executor, string name,
		string description)
	{
		if (await FindAsync(services, executor, name) is not { } alias)
		{
			await Tell(services, executor, $"MAIL: Alias {name} not found.");
			return;
		}

		if (!await MayManageAsync(alias, executor))
		{
			await Tell(services, executor, "MAIL: Permission denied.");
			return;
		}

		if (!await UpdateAsync(services, executor, alias, alias with { Description = description }))
		{
			return;
		}

		await Tell(services, executor, "MAIL: Description changed.");
	}

	/// <summary>do_malias_rename. Penn checks only the token of the new name, not its characters.</summary>
	private static async ValueTask RenameAsync(Services services, AnySharpObject executor, string name, string newName)
	{
		if (await FindAsync(services, executor, name) is not { } alias)
		{
			await Tell(services, executor, "MAIL: I cannot find that alias!");
			return;
		}

		if (newName.Length == 0 || newName[0] != Token)
		{
			await Tell(services, executor, $"MAIL: Bad alias. Aliases must start with '{Token}'.");
			return;
		}

		if (await FindAsync(services, null, newName) is not null)
		{
			await Tell(services, executor, "MAIL: That name already exists!");
			return;
		}

		if (!await MayManageAsync(alias, executor))
		{
			await Tell(services, executor, "MAIL: Permission denied.");
			return;
		}

		var renamed = await services.Mediator.Send(new UpdateMailAliasCommand(alias.Name, alias with { Name = newName[1..] }));
		// A failed rename lost a race: either the alias went away or another took the new name first.
		await Tell(services, executor, renamed is SharpMailAlias
			? "MAIL: Mail Alias renamed."
			: await FindAsync(services, null, $"{Token}{alias.Name}") is null
				? "MAIL: I cannot find that alias!"
				: "MAIL: That name already exists!");
	}

	/// <summary>do_malias_stats. Penn's "allocated slots" is its array's capacity; here it is the count.</summary>
	private static async ValueTask StatsAsync(Services services, AnySharpObject executor)
	{
		if (!await executor.IsPriv())
		{
			await Tell(services, executor, "MAIL: Permission denied.");
			return;
		}

		var count = await AllAsync(services).CountAsync();
		await Tell(services, executor, $"MAIL: Number of mail aliases defined: {count}");
		await Tell(services, executor, $"MAIL: Allocated slots {count}");
	}

	/// <summary>do_malias_chown: wizards only, to a player lookup_player finds.</summary>
	private static async ValueTask ChownAsync(Services services, AnySharpObject executor, string name, string owner)
	{
		if (await FindAsync(services, executor, name) is not { } alias)
		{
			await Tell(services, executor, $"MAIL: Alias {name} not found.");
			return;
		}

		if (!await executor.IsWizard())
		{
			await Tell(services, executor, "MAIL: You cannot do that!");
			return;
		}

		if (await LookupPlayerAsync(services, owner) is not { } newOwner)
		{
			await Tell(services, executor, "MAIL: I cannot find that player.");
			return;
		}

		if (!await UpdateAsync(services, executor, alias,
			alias with { Owner = newOwner.Object.DBRef.Number }))
		{
			return;
		}

		await Tell(services, executor, "MAIL: Owner changed for alias.");
	}

	/// <summary>do_malias_privs: /useflag sets who may use the alias, /seeflag who may see its members.</summary>
	private static async ValueTask PrivilegesAsync(Services services, AnySharpObject executor, string name,
		string privileges, bool members)
	{
		if (await FindAsync(services, executor, name) is not { } alias)
		{
			await Tell(services, executor, "MAIL: I cannot find that alias!");
			return;
		}

		if (!await MayManageAsync(alias, executor))
		{
			await Tell(services, executor, "MAIL: Permission denied.");
			return;
		}

		var parsed = ParsePrivileges(privileges);
		if (!await UpdateAsync(services, executor, alias, members
			? alias with { SeePrivileges = parsed }
			: alias with { UsePrivileges = parsed }))
		{
			return;
		}

		await Tell(services, executor,
			$"MAIL: Permission to see/use alias '{name}' changed to {PrivilegesToString(parsed)}");
	}

	/// <summary>do_malias_nuke: God only.</summary>
	private static async ValueTask NukeAsync(Services services, AnySharpObject executor)
	{
		if (!executor.IsGod())
		{
			await Tell(services, executor, "MAIL: Only god can do that!");
			return;
		}

		await services.Mediator.Send(new DeleteAllMailAliasesCommand());
		await Tell(services, executor, "MAIL: All mail aliases destroyed!");
	}

	/// <summary>
	/// fun_malias: no argument lists the aliases the executor may see; an alias lists its members' dbrefs
	/// (or <c>#-1 PERMISSION DENIED</c>); a one-character argument that is no alias is the delimiter.
	/// </summary>
	public static async ValueTask<string> FunctionAsync(Services services, AnySharpObject executor, string[] args)
	{
		var separator = " ";
		if (args.Length >= 1)
		{
			if (await FindAsync(services, executor, args[0]) is { } alias)
			{
				if (args.Length >= 2 && args[1].Length > 0)
				{
					if (args[1].Length != 1) return ErrorMessages.Returns.SeparatorMustBeOneChar;
					separator = args[1];
				}

				return await MaySeeMembersAsync(alias, executor)
					? string.Join(separator, alias.Members.Select(member => $"#{member}"))
					: ErrorMessages.Returns.PermissionDenied;
			}

			if (args[0].Length > 1) return ErrorMessages.Returns.NoMatch;
			if (args[0].Length == 1) separator = args[0];
		}

		var names = new List<string>();
		await foreach (var alias in AllAsync(services))
		{
			if (await IsListedForAsync(alias, executor))
			{
				names.Add($"{Token}{alias.Name}");
			}
		}

		return string.Join(separator, names);
	}

	/// <summary>
	/// The list parser do_malias_create, _set, _add and _remove share: space-separated, with a double-quoted
	/// run taken whole.
	/// </summary>
	private static IEnumerable<string> SplitList(string list)
	{
		var position = 0;
		while (position < list.Length)
		{
			while (position < list.Length && list[position] == ' ') position++;
			if (position >= list.Length) yield break;

			if (list[position] == '"')
			{
				var close = list.IndexOf('"', position + 1);
				var end = close < 0 ? list.Length : close;
				yield return list[(position + 1)..end];
				position = close < 0 ? list.Length : close + 1;
				continue;
			}

			var space = list.IndexOf(' ', position);
			var stop = space < 0 ? list.Length : space;
			yield return list[position..stop];
			position = stop;
		}
	}

	/// <summary>
	/// Resolves a member list, telling the executor about each entry as Penn does. With
	/// <paramref name="existing"/>, a player already on it is refused (do_malias_add). Empty, with the
	/// executor told, when nothing resolved.
	/// </summary>
	private static async ValueTask<List<int>> ResolveListAsync(Services services, AnySharpObject executor,
		string name, string list, SharpMailAlias? existing)
	{
		var members = new List<int>();
		var entries = SplitList(list).ToList();
		var consumed = 0;

		foreach (var entry in entries)
		{
			consumed++;
			if (await ResolvePlayerAsync(services, executor, entry) is not { } target)
			{
				await Tell(services, executor, $"MAIL: No such player '{entry}'.");
			}
			else if (existing is not null && existing.Members.Contains(target.Object.DBRef.Number))
			{
				await Tell(services, executor, $"MAIL: player '{entry}' exists already in alias {name}.");
			}
			else
			{
				await Tell(services, executor, $"MAIL: {await UnparseAsync(services, executor, target)} added to alias {name}");
				members.Add(target.Object.DBRef.Number);
			}

			if (members.Count == MaxMembers) break;
		}

		if (consumed < entries.Count)
		{
			await Tell(services, executor, "MAIL: Alias list is restricted to maximal 100 entries!");
		}

		if (members.Count == 0)
		{
			await Tell(services, executor, "MAIL: No valid recipients for alias-list!");
		}

		return members;
	}

	/// <summary>An entry of a member list: <c>me</c>, <c>#dbref</c>, or what lookup_player finds; players only.</summary>
	private static async ValueTask<SharpPlayer?> ResolvePlayerAsync(Services services, AnySharpObject executor,
		string entry)
	{
		if (entry.Equals("me", StringComparison.OrdinalIgnoreCase))
		{
			return executor is AnySharpObject and SharpPlayer self ? self : null;
		}

		if (entry.StartsWith('#'))
		{
			// atoi: the leading digits, or 0.
			var digits = new string(entry.Skip(1).TakeWhile(char.IsAsciiDigit).ToArray());
			var number = int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
			return await services.Mediator.Send(new GetObjectNodeQuery(new DBRef(number))) is AnySharpObject and SharpPlayer found
				? found
				: null;
		}

		return await LookupPlayerAsync(services, entry);
	}

	/// <summary><c>lookup_player</c>: a player by exact name or alias, a leading <c>*</c> ignored.</summary>
	private static async ValueTask<SharpPlayer?> LookupPlayerAsync(Services services, string name)
	{
		var bare = name.TrimStart('*');
		if (bare.StartsWith('#'))
		{
			return DBRef.TryParse(bare, out var dbref) && dbref is { } reference
					&& await services.Mediator.Send(new GetObjectNodeQuery(reference)) is AnySharpObject and SharpPlayer byDbref
				? byDbref
				: null;
		}

		return bare.Length == 0
			? null
			: await services.Mediator.CreateStream(new GetPlayerQuery(bare)).FirstOrDefaultAsync();
	}

	/// <summary><c>unparse_object(player, target, AN_SYS)</c>: the name, with dbref and flags when the viewer may see them.</summary>
	private static async ValueTask<string> UnparseAsync(Services services, AnySharpObject viewer, SharpPlayer target)
	{
		var obj = new AnySharpObject(target);
		var showReference = await services.Permissions.CanExamine(viewer, obj)
			|| await services.Permissions.CanLinkToAsync(viewer, obj) || await obj.HasFlag("JUMP_OK")
			|| await obj.HasFlag("CHOWN_OK") || await obj.HasFlag("DESTROY_OK");
		return showReference ? await MessageFormatting.FormatObjectWithDbref(target.Object) : target.Object.Name;
	}

	/// <summary>get_shortprivs: the Use and See columns of the list, <c>E</c> for everyone.</summary>
	private static string ShortPrivileges(SharpMailAlias alias)
	{
		static string Column(MailAliasPrivileges privileges)
		{
			if (privileges == MailAliasPrivileges.Everyone) return "E-";

			var first = privileges.HasFlag(MailAliasPrivileges.Members) ? 'M' : '-';
			var second = privileges.HasFlag(MailAliasPrivileges.Admin) ? 'A' : '-';
			if (first == '-' && second == '-') second = 'O';
			return $"{first}{second}";
		}

		return $"{Column(alias.UsePrivileges)}  {Column(alias.SeePrivileges)} ";
	}

	/// <summary>
	/// <c>string_to_privs(malias_priv_table, str, 0)</c>: space-separated words, <c>!</c> negating; a
	/// one-letter word is a letter, a longer one a prefix of a name; a lone word that matched nothing is
	/// read as a run of letters.
	/// </summary>
	private static MailAliasPrivileges ParsePrivileges(string text)
	{
		var yes = MailAliasPrivileges.Everyone;
		var no = MailAliasPrivileges.Everyone;
		var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

		foreach (var raw in words)
		{
			var negate = raw[0] == '!';
			var word = negate ? raw[1..] : raw;
			if (word.Length == 0) continue;

			var bits = word.Length == 1 ? Letters(word) : MailAliasPrivileges.Everyone;
			if (bits == MailAliasPrivileges.Everyone)
			{
				bits = PrivilegeTable
					.Where(entry => entry.Name.StartsWith(word, StringComparison.OrdinalIgnoreCase))
					.Select(entry => entry.Bit)
					.FirstOrDefault();
			}

			if (negate) no |= bits;
			else yes |= bits;
		}

		if (yes == MailAliasPrivileges.Everyone && no == MailAliasPrivileges.Everyone && words.Length == 1)
		{
			return Letters(text.Trim());
		}

		return yes & ~no;
	}

	/// <summary><c>letter_to_privs</c> from nothing: each letter sets its bit, <c>!</c> before one clears it.</summary>
	private static MailAliasPrivileges Letters(string text)
	{
		var yes = MailAliasPrivileges.Everyone;
		var no = MailAliasPrivileges.Everyone;
		for (var i = 0; i < text.Length; i++)
		{
			var negate = text[i] == '!';
			if (negate && ++i >= text.Length) break;

			var bit = PrivilegeTable.FirstOrDefault(entry => entry.Letter == text[i]).Bit;
			if (negate) no |= bit;
			else yes |= bit;
		}

		return yes & ~no;
	}

	/// <summary><c>privs_to_string</c>: the names of the bits set, in table order.</summary>
	private static string PrivilegesToString(MailAliasPrivileges privileges)
		=> string.Join(" ", PrivilegeTable.Where(entry => privileges.HasFlag(entry.Bit) && entry.Bit != 0)
			.Select(entry => entry.Name));
}
