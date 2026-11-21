using Mediator;
using MarkupString.Layout;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
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

namespace SharpMUSH.Implementation.Commands.WikiCommand;

/// <summary>
/// <c>@wiki/member &lt;page&gt;[=&lt;who&gt;[/&lt;level&gt;]]</c> — who is in a private page, letting someone in,
/// changing their level, or taking them out; and <c>@wiki/insert &lt;page&gt;=&lt;heading&gt;/&lt;markdown&gt;</c>,
/// which adds text at the top of a section. Who may do what is <see cref="IWikiMembersService"/>'s and
/// <see cref="IWikiAccessService"/>'s to decide.
/// </summary>
public static class MemberWiki
{
	/// <summary>Shows the page's members, or with <paramref name="memberArg"/> sets one.</summary>
	public static async ValueTask<MString> Handle(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		INotifyService notifyService,
		MString targetArg,
		MString? memberArg)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var members = parser.ServiceProvider.GetRequiredService<IWikiMembersService>();
		var reader = await WikiCommandHelper.ReaderAsync(parser, executor);
		var targetText = targetArg.ToPlainText().Trim();
		var (ns, slug) = WikiHelpers.ResolveTitle(targetText);
		if (await wikiService.GetBySlugAsync(slug, ns) is not WikiPage page
			|| !await WikiCommandHelper.Access(parser).CanSeeAsync(reader, page))
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {targetText}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		var memberText = memberArg?.ToPlainText().Trim() ?? string.Empty;
		if (memberText.Length == 0)
			return await ShowAsync(parser, mediator, members, notifyService, executor, reader, page);

		var slash = memberText.LastIndexOf('/');
		var who = slash < 0 ? memberText : memberText[..slash].Trim();
		WikiMemberLevel? level = null;
		if (slash >= 0)
		{
			var levelText = memberText[(slash + 1)..].Trim();
			if (!Enum.TryParse<WikiMemberLevel>(levelText, ignoreCase: true, out var parsed) || int.TryParse(levelText, out _))
			{
				await notifyService.Notify(executor, "WIKI: A level is reader, contributor or lead.", executor);
				return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToWikiCommand);
			}

			level = parsed;
		}

		if (await PrincipalAsync(parser, executor, who) is not string principal)
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchObject);

		var name = await NameAsync(mediator, principal);
		var outcome = await members.SetMemberAsync(reader, page, principal, level);
		if (outcome is Error<string> refused && refused.Value != WikiMembersService.LeftMessage)
		{
			await notifyService.Notify(executor, $"WIKI: {refused.Value}", executor);
			return MarkupText.Plain($"#-1 {refused.Value.TrimEnd('.').ToUpperInvariant()}");
		}

		if (!WikiCommandHelper.Quiet(parser))
			await notifyService.Notify(executor, outcome is Error<string> left
				? $"WIKI: {left.Value}"
				: level is { } set
					? $"WIKI: {name} is now a {set.ToString().ToLowerInvariant()} of '{page.Title}'."
					: $"WIKI: {name} is no longer a member of '{page.Title}'.", executor);
		return MarkupText.Plain(page.Slug);
	}

	/// <summary>
	/// <c>@wiki/insert &lt;page&gt;=&lt;heading&gt;/&lt;markdown&gt;</c>: puts the markdown first under the page's heading of
	/// that name, which is added at the end when the page has none. An edit like any other.
	/// </summary>
	public static async ValueTask<MString> Insert(
		IMUSHCodeParser parser,
		IMediator mediator,
		IWikiService wikiService,
		INotifyService notifyService,
		MString targetArg,
		MString contentArg)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(mediator);
		var (ns, slug) = WikiHelpers.ResolveTitle(targetArg.ToPlainText());
		if (await wikiService.GetBySlugAsync(slug, ns) is not WikiPage page)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		if (await WikiCommandHelper.RefusalAsync(parser, notifyService, executor, page, WikiAction.Edit,
			targetArg.ToPlainText()) is { } refused)
			return refused;

		var content = contentArg.ToPlainText();
		var slash = content.IndexOf('/');
		var heading = slash < 0 ? string.Empty : content[..slash].Trim().TrimStart('#').Trim();
		var text = slash < 0 ? string.Empty : content[(slash + 1)..].Trim();
		if (heading.Length == 0 || heading.Contains('\n') || text.Length == 0)
		{
			await notifyService.Notify(executor, "WIKI: Insert needs <heading>/<text>.", executor);
			return MarkupText.Plain(ErrorMessages.Returns.BadArgumentsToWikiCommand);
		}

		var result = await wikiService.UpdateAsync(page.Id, WikiHelpers.InsertUnder(page.MarkdownSource, heading, text),
			WikiCommandHelper.EditorDbref(executor), $"Added to {heading}");
		if (result is not WikiPage updated)
		{
			await notifyService.Notify(executor, $"WIKI: No such page: {targetArg.ToPlainText().Trim()}", executor);
			return MarkupText.Plain(ErrorMessages.Returns.NoSuchWikiPage);
		}

		if (!WikiCommandHelper.Quiet(parser))
			await notifyService.Notify(executor,
				$"WIKI: Added to {heading} on '{updated.Title}' (now rev {updated.RevisionNumber}).", executor);
		return MarkupText.Plain(updated.Slug);
	}

	private static async ValueTask<MString> ShowAsync(IMUSHCodeParser parser, IMediator mediator, IWikiMembersService members,
		INotifyService notifyService, AnySharpObject executor, WikiReader reader, WikiPage page)
	{
		if (await members.GetAsync(reader, page) is not WikiPageMembers found)
		{
			await notifyService.Notify(executor, $"WIKI: '{page.Title}' is not a private page, or you are not in it.", executor);
			return MarkupText.Plain(string.Empty);
		}

		var rows = new List<string[]>();
		foreach (var member in found.Members)
			rows.Add([await NameAsync(mediator, member.Principal), member.Level.ToString().ToLowerInvariant()]);
		var listing = ServerLayout.Listing(
			[new TableColumn(MarkupText.Plain("Member")) { Min = 10 }, new TableColumn(MarkupText.Plain("Level")) { Wrap = false }],
			rows);
		var output = ServerLayout.Build(
			ServerLayout.Panel(MarkupText.Plain($"Members of '{page.Title}'"), listing), 78, parser.HouseLook());
		await notifyService.Notify(executor, output, executor);
		return MarkupText.Plain(string.Join(' ', found.Members.Select(member => member.Principal)));
	}

	/// <summary><c>role:&lt;slug&gt;</c> as given, or the objid of the player <paramref name="who"/> names; null (and told) when it names nobody.</summary>
	private static async ValueTask<string?> PrincipalAsync(IMUSHCodeParser parser, AnySharpObject executor, string who)
	{
		if (who.StartsWith(WikiMember.RolePrefix, StringComparison.OrdinalIgnoreCase))
			return who;
		var locate = parser.ServiceProvider.GetRequiredService<ILocateService>();
		return await locate.LocatePlayerAndNotifyIfInvalid(parser, executor, executor, who.TrimStart('*'))
			is AnySharpObject found
				? found.Object().DBRef.ToString()
				: null;
	}

	/// <summary>A member as a person reads it: the character's name, or "role &lt;slug&gt;".</summary>
	public static async ValueTask<string> NameAsync(IMediator mediator, string principal)
	{
		if (new WikiMember(principal, WikiMemberLevel.Reader).RoleSlug is { } role)
			return $"role {role}";
		return DBRef.TryParse(principal, out var dbref) && dbref is { } parsed
			&& await mediator.Send(new GetObjectNodeQuery(parsed)) is AnySharpObject found
				? found.Object().Name
				: principal;
	}
}
