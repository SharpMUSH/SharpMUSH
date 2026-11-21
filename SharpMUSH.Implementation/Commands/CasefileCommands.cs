using System.Globalization;
using MarkupString.Layout;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Commands.WikiCommand;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	private static readonly string[] CasefileOperations = ["LIST", "CREATE", "ADD", "REMOVE", "POST"];

	/// <summary>
	/// <c>@casefile</c>: private wiki categories built around a plot (<see cref="ICasefileService"/>). Lists the
	/// casefiles you are in, shows one, opens one, lets people in and out, and posts developments.
	/// </summary>
	[SharpCommand(Name = "@CASEFILE", Switches = ["LIST", "CREATE", "ADD", "REMOVE", "POST"],
		Behavior = CB.Default | CB.EqSplit | CB.NoGagged | CB.NoGuest, MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["casefile", "value"])]
	public async ValueTask<Option<CallState>> Casefile(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.Where(CasefileOperations.Contains).ToArray();
		var args = parser.CurrentState.ArgumentList;
		var left = (args.GetValueOrDefault("0")?.Message.ToPlainText() ?? "").Trim();
		var right = (args.GetValueOrDefault("1")?.Message.ToPlainText() ?? "").Trim();
		var casefiles = parser.ServiceProvider.GetRequiredService<ICasefileService>();
		var reader = await WikiCommandHelper.ReaderAsync(parser, executor);

		if (switches.Length > 1)
		{
			await NotifyService.Notify(executor, "CASEFILE: Choose one @casefile operation.", executor);
			return new CallState(ErrorMessages.Returns.TooManySwitches);
		}

		var operation = switches.FirstOrDefault() ?? (left.Length == 0 ? "LIST" : "SHOW");
		var (output, result) = operation switch
		{
			"LIST" => (await CasefileListAsync(parser, casefiles, reader), string.Empty),
			"SHOW" => await CasefileShowAsync(parser, casefiles, reader, left),
			"CREATE" when left.Length > 0 => await CasefileCreateAsync(parser, casefiles, reader, left, right),
			"ADD" when left.Length > 0 && right.Length > 0 => await CasefileMemberAsync(parser, executor, casefiles, reader, left, right, add: true),
			"REMOVE" when left.Length > 0 && right.Length > 0 => await CasefileMemberAsync(parser, executor, casefiles, reader, left, right, add: false),
			"POST" when left.Length > 0 && right.Length > 0 => await CasefilePostAsync(casefiles, reader, executor, left, right),
			_ => (MarkupText.Plain($"CASEFILE: Usage: {CasefileUsage(operation)}. See help @casefile."), ErrorMessages.Returns.BadArgumentsToWikiCommand),
		};

		await NotifyService.Notify(executor, output, executor);
		return new CallState(result.Length > 0 ? MarkupText.Plain(result) : output);
	}

	private static string CasefileUsage(string operation) => operation switch
	{
		"CREATE" => "@casefile/create <title>[=<briefing>]",
		"ADD" => "@casefile/add <casefile>=<player or role:slug>[/<reader, contributor or lead>]",
		"REMOVE" => "@casefile/remove <casefile>=<player or role:slug>",
		"POST" => "@casefile/post <casefile>=<title>/<what happened>",
		_ => "@casefile [<casefile>]",
	};

	private static async ValueTask<MString> CasefileListAsync(IMUSHCodeParser parser, ICasefileService casefiles, WikiReader reader)
	{
		var list = await casefiles.ListAsync(reader);
		if (list.Count == 0)
			return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Casefiles"),
				new TextBlock(MarkupText.Plain("You are not in any casefile. Open one with @casefile/create <title>."))), 78, parser.HouseLook());

		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Casefile")) { Min = 12 },
				new TableColumn(MarkupText.Plain("You are")) { Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Files")) { Alignment = Alignment.Right, Wrap = false, Priority = 3 },
				new TableColumn(MarkupText.Plain("Updated")) { Wrap = false, Priority = 2 },
			],
			list.Select(item => new[]
			{
				item.Title,
				LevelName(item.Level),
				item.Files.ToString(CultureInfo.InvariantCulture),
				item.UpdatedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
			}));
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Your casefiles"), table), 78, parser.HouseLook());
	}

	private async ValueTask<(MString, string)> CasefileShowAsync(IMUSHCodeParser parser, ICasefileService casefiles,
		WikiReader reader, string name)
	{
		if (await casefiles.GetAsync(reader, name) is not Casefile casefile)
			return (MarkupText.Plain($"CASEFILE: No such casefile: {name}."), ErrorMessages.Returns.NoSuchWikiPage);

		var summary = casefile.Summary;
		var members = new List<string>();
		foreach (var member in casefile.Members) members.Add($"{await MemberNameAsync(member.Principal)} ({LevelName(member.Level)})");

		(string, MString)[] fields =
		[
			("You are", MarkupText.Plain(LevelName(summary.Level))),
			("Briefing", MarkupText.Plain(casefile.Briefing is { } briefing ? $"@wiki Category:{briefing.Title}" : "none yet")),
			("Developments", MarkupText.Plain(casefile.Developments is { } log ? $"@wiki {log.Title}" : "none yet")),
			("Members", MarkupText.Plain(string.Join(", ", members))),
		];
		List<Block> parts = [];
		if (summary.Summary.Length > 0) parts.Add(new TextBlock(MarkupText.Plain(summary.Summary)));
		parts.Add(ServerLayout.KeyValues(fields));
		parts.Add(new Rule(MarkupText.Plain($"Files ({casefile.Files.Count})")) { TitleAlignment = Alignment.Left });
		parts.Add(casefile.Files.Count == 0
			? new TextBlock(MarkupText.Plain("None yet. File a page here with @wiki/category <page>=" + summary.Title + "."))
			: ServerLayout.Listing(
				[
					new TableColumn(MarkupText.Plain("Page")) { Min = 12 },
					new TableColumn(MarkupText.Plain("Updated")) { Wrap = false, Priority = 2 },
				],
				casefile.Files.Select(page => new[]
				{
					page.Title, page.UpdatedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
				})));
		return (ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"Casefile: {summary.Title}"), [.. parts]), 78, parser.HouseLook()),
			string.Empty);
	}

	private static async ValueTask<(MString, string)> CasefileCreateAsync(IMUSHCodeParser parser, ICasefileService casefiles,
		WikiReader reader, string title, string briefing)
		=> await casefiles.CreateAsync(reader, title, briefing) switch
		{
			Casefile casefile => (MarkupText.Plain(
				$"CASEFILE: Opened {casefile.Summary.Title}. You are its lead. Let people in with @casefile/add {casefile.Summary.Title}=<player>."),
				casefile.Summary.Key),
			Error<string> error => (MarkupText.Plain($"CASEFILE: {error.Value}"), ErrorMessages.Returns.PermissionDenied),
		};

	private async ValueTask<(MString, string)> CasefileMemberAsync(IMUSHCodeParser parser, AnySharpObject executor,
		ICasefileService casefiles, WikiReader reader, string name, string value, bool add)
	{
		var slash = value.LastIndexOf('/');
		var who = add && slash > 0 ? value[..slash].Trim() : value;
		WikiMemberLevel? level = null;
		if (add)
		{
			var levelText = slash > 0 ? value[(slash + 1)..].Trim() : "reader";
			if (!Enum.TryParse<WikiMemberLevel>(levelText, ignoreCase: true, out var parsed) || int.TryParse(levelText, out _))
				return (MarkupText.Plain("CASEFILE: A level is reader, contributor or lead."), ErrorMessages.Returns.BadArgumentsToWikiCommand);
			level = parsed;
		}

		string principal;
		string label;
		if (who.StartsWith(WikiMember.RolePrefix, StringComparison.OrdinalIgnoreCase))
		{
			principal = who;
			label = $"Everyone with the {who[WikiMember.RolePrefix.Length..].Trim()} role";
		}
		else
		{
			var locate = parser.ServiceProvider.GetRequiredService<ILocateService>();
			if (await locate.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, who.TrimStart('*'))
				is not (AnySharpObject and SharpPlayer player))
				return (MarkupText.Empty, ErrorMessages.Returns.NoSuchObject);
			principal = player.Object.DBRef.ToString();
			label = player.Object.Name;
		}

		return await casefiles.SetMemberAsync(reader, name, principal, level) switch
		{
			Casefile casefile => (MarkupText.Plain(level is { } set
				? $"CASEFILE: {label} is now a {LevelName(set)} of {casefile.Summary.Title}."
				: $"CASEFILE: {label} is no longer in {casefile.Summary.Title}."), casefile.Summary.Key),
			Error<string> error => (MarkupText.Plain($"CASEFILE: {error.Value}"), ErrorMessages.Returns.PermissionDenied),
		};
	}

	private static async ValueTask<(MString, string)> CasefilePostAsync(ICasefileService casefiles, WikiReader reader,
		AnySharpObject executor, string name, string value)
	{
		var slash = value.IndexOf('/');
		if (slash <= 0 || slash == value.Length - 1)
			return (MarkupText.Plain($"CASEFILE: Usage: {CasefileUsage("POST")}."), ErrorMessages.Returns.BadArgumentsToWikiCommand);

		return await casefiles.PostDevelopmentAsync(reader, name, executor.Object().Name, value[..slash].Trim(),
			value[(slash + 1)..].Trim(), []) switch
		{
			WikiPage log => (MarkupText.Plain($"CASEFILE: Posted to {log.Title}."), log.Id),
			Error<string> error => (MarkupText.Plain($"CASEFILE: {error.Value}"), ErrorMessages.Returns.PermissionDenied),
		};
	}

	/// <summary>A member as people read it: a character's name, or "role &lt;slug&gt;".</summary>
	private async ValueTask<string> MemberNameAsync(string principal)
	{
		var member = new WikiMember(principal, WikiMemberLevel.Reader);
		if (member.RoleSlug is { } slug) return $"role {slug}";
		return DBRef.TryParse(principal, out var parsed) && parsed is { } reference
			&& await Mediator.Send(new GetObjectNodeQuery(reference)) is AnySharpObject found
				? found.Object().Name
				: principal;
	}

	private static string LevelName(WikiMemberLevel level) => level.ToString().ToLowerInvariant();
}
