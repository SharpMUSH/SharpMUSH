using System.Globalization;
using MarkupString.Layout;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Implementation.Common;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models.Palette;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// <c>@context/push &lt;player&gt;=&lt;name&gt;[,&lt;values as JSON&gt;[,&lt;lifetime&gt;[,&lt;info&gt;]]]</c>,
	/// <c>@context/drop &lt;player&gt;=&lt;name&gt;</c>, and <c>@context &lt;player&gt;</c> to list the stack.
	/// Code may use the stack of the player whose command it runs, or of a player its object controls.
	/// A push or drop is silent; a refusal is told to the executor and returned.
	/// </summary>
	[SharpCommand(Name = "@CONTEXT", Switches = ["PUSH", "DROP"], Behavior = CB.Default | CB.EqSplit | CB.NoGagged,
		MinArgs = 1, MaxArgs = 2, ParameterNames = ["player", "context"])]
	public async ValueTask<Option<CallState>> Context(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var switches = parser.CurrentState.Switches.ToArray();
		var arguments = parser.CurrentState.ArgumentsOrdered;
		var name = arguments.GetValueOrDefault("0")?.Message.ToPlainText() ?? string.Empty;
		var right = arguments.GetValueOrDefault("1")?.Message.ToPlainText() ?? string.Empty;

		if (switches.Length > 1) return await ContextError(executor, "#-1 CONTEXT ACCEPTS ONE SWITCH");
		if (switches.Length == 1 && string.IsNullOrWhiteSpace(right))
			return await ContextError(executor, ContextPush.InvalidName);

		return await ContextAuthority.ResolveAsync(parser, LocateService, PermissionService, executor, name) switch
		{
			SharpPlayer player => switches switch
			{
				["PUSH"] => await ContextPushAsync(parser, executor, player, right),
				["DROP"] => await ContextDropAsync(parser, executor, player, right),
				_ => await ContextListAsync(parser, executor, player)
			},
			Error<string> error => await ContextError(executor, error.Value)
		};
	}

	private async ValueTask<Option<CallState>> ContextPushAsync(IMUSHCodeParser parser, AnySharpObject executor,
		SharpPlayer player, string text)
		=> ContextPush.Parse(text) switch
		{
			ContextPush push => Pushed(parser, player, push),
			Error<string> error => await ContextError(executor, error.Value)
		};

	private static Option<CallState> Pushed(IMUSHCodeParser parser, SharpPlayer player, ContextPush push)
	{
		parser.ServiceProvider.GetRequiredService<IContextStack>()
			.Push(player.Object.DBRef, push.Name, push.Values, push.Lifetime, push.Info);
		return CallState.Empty;
	}

	private async ValueTask<Option<CallState>> ContextDropAsync(IMUSHCodeParser parser, AnySharpObject executor,
		SharpPlayer player, string name)
	{
		if (!ContextPush.IsValidName(name.Trim())) return await ContextError(executor, ContextPush.InvalidName);
		var dropped = parser.ServiceProvider.GetRequiredService<IContextStack>().Drop(player.Object.DBRef, name);
		return new CallState(dropped ? "1" : "0");
	}

	private async ValueTask<Option<CallState>> ContextListAsync(IMUSHCodeParser parser, AnySharpObject executor,
		SharpPlayer player)
	{
		var stack = parser.ServiceProvider.GetRequiredService<IContextStack>().Read(player.Object.DBRef);
		if (stack.Count == 0)
		{
			await NotifyService.Notify(executor, $"{player.Object.Name} has no context.");
			return CallState.Empty;
		}

		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Context")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Weight")) { Alignment = Alignment.Right, Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("Values")) { Min = 10, Priority = 3 },
				new TableColumn(MarkupText.Plain("Info")) { Min = 10, Priority = 4 },
			],
			stack.Select(context => new[]
			{
				context.Name,
				context.Weight.ToString("0.00", CultureInfo.InvariantCulture),
				string.Join(" · ", context.Context.Values.Select(pair => $"{pair.Key}={pair.Value}")),
				context.Context.Info ?? string.Empty
			}));
		var output = ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"Context of {player.Object.Name}, newest first"), table), 78, parser.HouseLook());
		await NotifyService.Notify(executor, output);
		return CallState.Empty;
	}

	private async ValueTask<Option<CallState>> ContextError(AnySharpObject executor, string error)
	{
		await NotifyService.Notify(executor, error);
		return new CallState(error);
	}
}
