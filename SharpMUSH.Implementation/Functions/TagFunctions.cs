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

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// RhostMUSH's tag functions, over the tags <c>@tag</c> and <c>@ltag</c> keep. A tag is looked up as
/// <c>#tag</c> is: the world's tag of that name, else one of the executor's owner's own.
/// </summary>
public partial class Functions
{
	/// <summary>tagref(): the object a tag names, or #-1. RhostMUSH calls it tag(), which is HTML's here.</summary>
	[SharpFunction(Name = "tagref", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["tag"])]
	public async ValueTask<CallState> TagRef(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await ResolveTagAsync(parser, parser.CurrentState.Arguments["0"].Message.ToPlainText().Trim().TrimStart('#')) switch
		{
			DBRef target => new CallState(new DBRef(target.Number).ToString()),
			NotFound => new CallState(ErrorMessages.Returns.Nothing)
		};

	/// <summary>istag(): 1 when the argument is <c>#</c> and a tag that names an object.</summary>
	[SharpFunction(Name = "istag", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["string"])]
	public async ValueTask<CallState> IsTag(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var given = parser.CurrentState.Arguments["0"].Message.ToPlainText().Trim();
		return given.StartsWith('#') && await ResolveTagAsync(parser, given[1..]) is DBRef;
	}

	/// <summary>
	/// listtags(): every tag as <c>tag|#dbref</c>, or the names of those that name the given object; the world's and
	/// the executor's owner's own.
	/// </summary>
	[SharpFunction(Name = "listtags", MinArgs = 0, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object"])]
	public async ValueTask<CallState> ListTags(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (!await executor.Can(PortalPermission.ConfigAdmin))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var owner = await OwnerOfAsync(executor);
		// listtags() is called with one empty argument.
		if (!parser.CurrentState.Arguments.TryGetValue("0", out var named) || named.Message.ToPlainText().Trim().Length == 0)
		{
			var listed = await Tags(parser).ListAsync(owner, ExecutionBudget.CurrentToken);
			return new CallState(string.Join(' ', listed.Select(tag => $"{tag.Name}|{new DBRef(tag.Target.Number)}")));
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor,
				named.Message.ToPlainText(), LocateFlags.All) switch
		{
			AnySharpObject target => new CallState(string.Join(' ',
				(await Tags(parser).OfAsync(target.Object().DBRef, ExecutionBudget.CurrentToken))
				.Where(tag => tag.Owner is not { } tagOwner || tagOwner.SameObjectAs(owner))
				.Select(tag => tag.Name).Order(StringComparer.Ordinal))),
			Error<CallState> refusal => refusal.Value
		};
	}

	/// <summary>tagmatch(): the names of the tags, the world's and the executor's owner's own, that match a wildcard pattern.</summary>
	[SharpFunction(Name = "tagmatch", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["pattern"])]
	public async ValueTask<CallState> TagMatch(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (!await executor.Can(PortalPermission.ConfigAdmin))
		{
			return new CallState(ErrorMessages.Returns.PermissionDenied);
		}

		var pattern = parser.CurrentState.Arguments["0"].Message.ToPlainText();
		var listed = await Tags(parser).ListAsync(await OwnerOfAsync(executor), ExecutionBudget.CurrentToken);
		return new CallState(string.Join(' ', listed.Select(tag => tag.Name).Distinct()
			.Where(name => MushText.IsWildcardMatch(MarkupText.Plain(name), pattern))));
	}

	private async ValueTask<Found<DBRef>> ResolveTagAsync(IMUSHCodeParser parser, string name)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await Tags(parser).ResolveAsync(name, await OwnerOfAsync(executor), ExecutionBudget.CurrentToken);
	}

	private static async ValueTask<DBRef> OwnerOfAsync(AnySharpObject executor)
		=> (await executor.Object().Owner.WithCancellation(CancellationToken.None)).Object.DBRef;

	private static ObjectTagRegistry Tags(IMUSHCodeParser parser) => parser.ServiceProvider.GetRequiredService<ObjectTagRegistry>();
}
