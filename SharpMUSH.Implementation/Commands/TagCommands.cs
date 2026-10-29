using MarkupString.Layout;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Tags;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

/// <summary>
/// RhostMUSH's tags: names for objects, so softcode can say <c>#weather</c> or <c>tagref(weather)</c> where it
/// would otherwise say a dbref. <c>@tag</c> keeps the world's tags, <c>@ltag</c> each owner's own.
/// </summary>
public partial class Commands
{
	private static readonly string[] TagOperations = ["LIST", "ADD", "REMOVE"];

	/// <summary><c>@tag</c>: the world's tags, for those who may change the world's configuration.</summary>
	[SharpCommand(Name = "@TAG", Switches = ["LIST", "ADD", "REMOVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["tag or pattern", "object"])]
	public async ValueTask<Option<CallState>> Tag(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (!await executor.Can(PortalPermission.ConfigAdmin))
		{
			return await NotifyService.PermissionDeniedNeeds(executor, PortalPermission.ConfigAdmin);
		}

		return await TagCommandAsync(parser, executor, null);
	}

	/// <summary><c>@ltag</c>: the executor's owner's own tags, which name only objects that owner controls.</summary>
	[SharpCommand(Name = "@LTAG", Switches = ["LIST", "ADD", "REMOVE"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 0, MaxArgs = 2, ParameterNames = ["tag or pattern", "object"])]
	public async ValueTask<Option<CallState>> LTag(IMUSHCodeParser parser, SharpCommandAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var owner = await executor.Object().Owner.WithCancellation(CancellationToken.None);
		return await TagCommandAsync(parser, executor, owner.Object.DBRef);
	}

	private async ValueTask<Option<CallState>> TagCommandAsync(IMUSHCodeParser parser, AnySharpObject executor, DBRef? owner)
	{
		var switches = parser.CurrentState.Switches.Where(TagOperations.Contains).ToArray();
		if (switches.Length > 1)
		{
			await NotifyService.Notify(executor, "Choose one of /list, /add and /remove.");
			return new CallState("#-1 CHOOSE ONE OPERATION");
		}

		var tags = parser.ServiceProvider.GetRequiredService<ObjectTagRegistry>();
		var name = PlainArgument(parser, "0").Trim();
		var output = switches.FirstOrDefault() switch
		{
			"LIST" => await TagListAsync(parser, tags, owner, name),
			"ADD" => await TagAddAsync(parser, tags, executor, owner, name, PlainArgument(parser, "1").Trim()),
			"REMOVE" => Said(await tags.RemoveAsync(name, owner, ExecutionBudget.CurrentToken)),
			_ => MarkupText.Plain(await tags.ResolveAsync(name, owner, ExecutionBudget.CurrentToken) switch
			{
				DBRef target => new DBRef(target.Number).ToString(),
				NotFound => "#-1"
			})
		};

		await NotifyService.Notify(executor, output);
		return new CallState(output);
	}

	private async ValueTask<MString> TagAddAsync(IMUSHCodeParser parser, ObjectTagRegistry tags, AnySharpObject executor,
		DBRef? owner, string name, string objectName)
	{
		var found = await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, objectName, LocateFlags.All);
		if (found is not AnySharpObject target)
		{
			return MarkupText.Empty;
		}

		if (owner is not null && !await PermissionService.Controls(executor, target))
		{
			return MarkupText.Plain(PermissionRefusal.NotControlled(target).Text);
		}

		return Said(await tags.AddAsync(name, target.Object().DBRef, owner, ExecutionBudget.CurrentToken));
	}

	/// <summary>The tags whose names match <paramref name="pattern"/> (every one when it is empty), with what each names.</summary>
	private static async ValueTask<MString> TagListAsync(IMUSHCodeParser parser, ObjectTagRegistry tags, DBRef? owner, string pattern)
	{
		var listed = (await tags.ListAsync(owner, ExecutionBudget.CurrentToken))
			.Where(tag => owner is null ? tag.Owner is null : tag.Owner is not null)
			.Where(tag => pattern.Length == 0 || MushText.IsWildcardMatch(MarkupText.Plain(tag.Name), pattern))
			.ToList();
		if (listed.Count == 0)
		{
			return MarkupText.Plain(pattern.Length == 0 ? "No tags." : $"No tags match {pattern}.");
		}

		// A personal tag a global one of the same name hides resolves to the global one.
		var global = owner is null
			? []
			: (await tags.ListAsync(null, ExecutionBudget.CurrentToken)).Select(tag => tag.Name).ToHashSet(StringComparer.Ordinal);
		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Tag")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Object")) { Wrap = false },
			],
			listed.Select(tag => new[] { global.Contains(tag.Name) ? $"{tag.Name} (hidden by a global tag)" : tag.Name, new DBRef(tag.Target.Number).ToString() }));
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain(owner is null ? "Tags" : "Your tags"), table), 78, parser.HouseLook());
	}

	private static MString Said(Result<string> result) => result switch
	{
		string message => MarkupText.Plain(message),
		Error<string> error => MarkupText.Plain("#-1 " + error.Value)
	};
}
