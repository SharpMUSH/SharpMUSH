using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Reality;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// RhostMUSH's and TinyMUX's reality-level functions, over SharpMUSH's reality layers (<c>@reality</c>). A level is a
/// layer, named itself or by an alias, such as the name an imported game gave it.
/// </summary>
public partial class Functions
{
	private union ProfileOrRefusal(ObjectReality, CallState);

	[SharpFunction(Name = "hasrxlevel", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "level"])]
	public ValueTask<CallState> HasRxLevel(IMUSHCodeParser parser, SharpFunctionAttribute _2) => HasLevelAsync(parser, true);

	[SharpFunction(Name = "hastxlevel", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "level"])]
	public ValueTask<CallState> HasTxLevel(IMUSHCodeParser parser, SharpFunctionAttribute _2) => HasLevelAsync(parser, false);

	/// <summary>rxlevel(): the layers an object receives, or, given a list, sets them as <c>@rxlevel</c> does.</summary>
	[SharpFunction(Name = "rxlevel", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX,
		SideEffectMinArgs = 2, ParameterNames = ["object", "levels"])]
	public ValueTask<CallState> RxLevel(IMUSHCodeParser parser, SharpFunctionAttribute _2) => LevelAsync(parser, true);

	/// <summary>txlevel(): the layers an object transmits, or, given a list, sets them as <c>@txlevel</c> does.</summary>
	[SharpFunction(Name = "txlevel", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.HasSideFX,
		SideEffectMinArgs = 2, ParameterNames = ["object", "levels"])]
	public ValueTask<CallState> TxLevel(IMUSHCodeParser parser, SharpFunctionAttribute _2) => LevelAsync(parser, false);

	[SharpFunction(Name = "listrlevels", MinArgs = 0, MaxArgs = 0, Flags = FunctionFlags.Regular)]
	public async ValueTask<CallState> ListRLevels(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> new(string.Join(' ', (await Reality(parser).ConfigurationAsync(ExecutionBudget.CurrentToken)).Layers));

	/// <summary>cansee(): whether the first object receives a configured layer the second transmits, wherever the two are.</summary>
	[SharpFunction(Name = "cansee", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["viewer", "target"])]
	public ValueTask<CallState> CanSee(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> BothAsync(parser, async (viewer, target) => viewer.Object.Equals(target.Object)
			|| RealityPolicy.SharedLayers(await Reality(parser).ConfigurationAsync(ExecutionBudget.CurrentToken), viewer, target).Any());

	/// <summary>chkreality(): whether reality lets the first object perceive the second, as everything else asks it.</summary>
	[SharpFunction(Name = "chkreality", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["viewer", "target"])]
	public ValueTask<CallState> ChkReality(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> BothAsync(parser, async (viewer, target) =>
			await Reality(parser).CanPerceiveAsync(viewer.Object, target.Object, ExecutionBudget.CurrentToken));

	/// <summary>
	/// rxdesc(): the description attribute of each layer an object you control receives, as RhostMUSH's rxdesc() lists
	/// what its levels describe things with; or, given <c>desc</c>, <c>adesc</c>, <c>value</c> or <c>hex</c> and a
	/// layer, that layer's description attribute, the attribute its description action is read from, or its bit.
	/// </summary>
	[SharpFunction(Name = "rxdesc", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "layer"])]
	public async ValueTask<CallState> RxDesc(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var config = await Reality(parser).ConfigurationAsync(ExecutionBudget.CurrentToken);
		if (!parser.CurrentState.ArgumentList.TryGet(1, out var layerArgument))
		{
			return await ProfileAsync(parser, "0") switch
			{
				ObjectReality profile => new CallState(string.Join(' ', config.Layers
					.Where(layer => profile.Receive.Contains(layer, StringComparer.OrdinalIgnoreCase))
					.Select(config.DescriptionOf))),
				CallState refusal => refusal
			};
		}

		var subargument = parser.CurrentState.ArgumentList[0].Message.ToPlainText();
		var name = layerArgument.Message.ToPlainText();
		var kind = subargument.ToLowerInvariant();
		if (kind is not ("desc" or "adesc" or "value" or "hex"))
		{
			return new CallState($"#-1 UNRECOGNIZED SUBARG '{subargument}' FOR FUNCTION RXDESC");
		}

		var layer = name.Trim().Length == 0 ? null : await VisibleLayerAsync(parser, config, name.Trim());
		return kind switch
		{
			"desc" => new CallState(layer is null ? "#-1" : config.DescriptionOf(layer)),
			"adesc" => new CallState(layer is null ? "#-1"
				: config.DescriptionOf(layer) is "DESCRIBE" ? "ADESCRIBE" : config.DescriptionOf(layer) + "_ADESC"),
			"value" => new CallState(layer is null ? "0" : LayerBit(config, layer).ToString(CultureInfo.InvariantCulture)),
			_ => new CallState(layer is null ? "0x00000000" : $"0x{LayerBit(config, layer):X8}")
		};
	}

	/// <summary>A layer's bit, by its place among the configured layers: 1 for the first, 2 for the next.</summary>
	private static uint LayerBit(RealityConfiguration config, string layer) => 1u << Array.IndexOf(config.Layers, layer);

	/// <summary>
	/// The first configured layer whose name or alias starts with <paramref name="name"/> and that the executor may
	/// ask about: one it receives, or any when it holds the guildmaster role or one above it.
	/// </summary>
	private async ValueTask<string?> VisibleLayerAsync(IMUSHCodeParser parser, RealityConfiguration config, string name)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var receives = (await Reality(parser).ReadObjectAsync(executor.Object().DBRef, ExecutionBudget.CurrentToken))?.Receive ?? [];
		var staff = await RolePriorityOrBuiltInAsync(parser, "guildmaster") is { } priority
			&& await executor.GrantsAsync(ExecutionBudget.CurrentToken) is var grants
			&& (grants.IsOwner || grants.Roles.Any(held => held.Role.Priority >= priority));
		return config.Layers.FirstOrDefault(layer =>
			(staff || receives.Contains(layer, StringComparer.OrdinalIgnoreCase))
			&& (layer.StartsWith(name, StringComparison.OrdinalIgnoreCase)
				|| (config.Aliases ?? []).Any(alias => alias.Value == layer && alias.Key.StartsWith(name, StringComparison.OrdinalIgnoreCase))));
	}

	/// <summary>Answers <paramref name="answer"/> for the viewer and target arguments, or the first refusal.</summary>
	private async ValueTask<CallState> BothAsync(IMUSHCodeParser parser,
		Func<ObjectReality, ObjectReality, ValueTask<CallState>> answer)
		=> await ProfileAsync(parser, "0") switch
		{
			ObjectReality viewer => await ProfileAsync(parser, "1") switch
			{
				ObjectReality target => await answer(viewer, target),
				CallState refusal => refusal
			},
			CallState refusal => refusal
		};

	private async ValueTask<CallState> HasLevelAsync(IMUSHCodeParser parser, bool receive)
		=> await ProfileAsync(parser, "0") switch
		{
			ObjectReality profile => (await Reality(parser).ConfigurationAsync(ExecutionBudget.CurrentToken))
				.Layer(parser.CurrentState.ArgumentList[1].Message.ToPlainText()) is { } layer
					? (receive ? profile.Receive : profile.Transmit).Contains(layer)
					: new CallState(ErrorMessages.Returns.NoSuchLevel),
			CallState refusal => refusal
		};

	private async ValueTask<CallState> LevelAsync(IMUSHCodeParser parser, bool receive)
		=> await ProfileAsync(parser, "0") switch
		{
			ObjectReality profile => parser.CurrentState.ArgumentList.TryGet(1, out var levels)
				? await SetLevelsAsync(parser, profile, receive, levels.Message.ToPlainText())
				: new CallState(string.Join(' ', receive ? profile.Receive : profile.Transmit)),
			CallState refusal => refusal
		};

	private async ValueTask<CallState> SetLevelsAsync(IMUSHCodeParser parser, ObjectReality profile, bool receive, string levels)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await SharpMUSH.Implementation.Commands.RealityLevels.SetAsync(parser, executor, profile.Object, receive, levels,
				reset: false) switch
		{
			string => CallState.Empty,
			Error<string> error => new CallState("#-1 " + error.Value)
		};
	}

	/// <summary>
	/// The reality settings of the object argument <paramref name="key"/> names, when the executor controls it; the
	/// refusal otherwise.
	/// </summary>
	private async ValueTask<ProfileOrRefusal> ProfileAsync(IMUSHCodeParser parser, string key)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await SharpMUSH.Implementation.Commands.RealityLevels.FindAsync(parser, LocateService, Mediator, executor,
				parser.CurrentState.ArgumentList[key].Message.ToPlainText()) switch
		{
			AnySharpObject target => await ProfileOfAsync(parser, executor, target),
			CallState refusal => refusal
		};
	}

	private async ValueTask<ProfileOrRefusal> ProfileOfAsync(IMUSHCodeParser parser, AnySharpObject executor, AnySharpObject target)
	{
		if (await PermissionService.Check(executor, Requires.ControlOf(target)) is PermissionRefusal refused)
		{
			return refused.Return;
		}

		return await Reality(parser).ReadObjectAsync(target.Object().DBRef, ExecutionBudget.CurrentToken) is { } profile
			? profile
			: new CallState(ErrorMessages.Returns.NotFound);
	}

	private static RealityPolicy Reality(IMUSHCodeParser parser) => parser.ServiceProvider.GetRequiredService<RealityPolicy>();
}
