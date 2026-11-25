using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// <c>pref(&lt;group&gt;, &lt;key&gt;[, &lt;player&gt;])</c>: the value a player has for a preference a package declares
	/// (<c>preferences:</c>), read from their account or character as the group's scope says: the value they set, or
	/// the default while they have set none (or the one they set is no longer allowed). The player is the executor's
	/// owner unless named. Reading another player's is refused as <c>get()</c> refuses it.
	/// </summary>
	[SharpFunction(Name = "pref", MinArgs = 2, MaxArgs = 3, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["group", "key", "player"])]
	public async ValueTask<CallState> Pref(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentList;
		var preferences = parser.ServiceProvider.GetRequiredService<IPreferenceService>();
		var token = ExecutionBudget.CurrentToken;
		if (await preferences.FindGroupAsync(args[0].Message.ToPlainText().Trim(), token) is not PreferenceGroup group)
		{
			return new CallState(ErrorMessages.Returns.NoSuchPreferenceGroup);
		}

		if (group.Spec.Find(args[1].Message.ToPlainText().Trim()) is not { } key)
		{
			return new CallState(ErrorMessages.Returns.NoSuchPreference);
		}

		if (!args.TryGet(2, out var playerArgument))
		{
			return await PrefOfAsync(executor, await executor.Object().Owner.WithCancellation(token), group, key, preferences);
		}

		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor,
			playerArgument.Message.ToPlainText().Trim(), LocateFlags.PlayersPreference,
			async found => found is SharpPlayer player
				? await PrefOfAsync(executor, player, group, key, preferences)
				: new CallState(ErrorMessages.Returns.NotAPlayer));
	}

	/// <summary>
	/// <paramref name="player"/>'s value for <paramref name="key"/>, read as <c>get()</c> reads its attribute. A
	/// player with nothing to keep it on (an account group, and no account) has the default.
	/// </summary>
	private async ValueTask<CallState> PrefOfAsync(AnySharpObject executor, SharpPlayer player, PreferenceGroup group,
		PackagePreferenceSpec key, IPreferenceService preferences)
	{
		var token = ExecutionBudget.CurrentToken;
		if (await preferences.HolderAsync(group, null, player, token) is not DBRef reference
			|| await PreferenceHolderAsync(player, reference) is not AnySharpObject holder)
		{
			return new CallState(key.Default ?? string.Empty);
		}

		var path = string.Join('`', group.AttributePath(key));
		return await AttributeService.GetAttributeAsync(executor, holder, path, IAttributeService.AttributeMode.Read, false) switch
		{
			SharpAttribute[] found => new CallState(Preferences.Effective(key, found.Last().Value.ToPlainText())),
			Error<string> refused => new CallState(refused.Value),
			_ => await MissingAttributeGetResultAsync(executor, holder, path) is { } missing && missing.Message?.Length > 0
				? missing
				: new CallState(key.Default ?? string.Empty)
		};
	}

	private async ValueTask<AnyOptionalSharpObject> PreferenceHolderAsync(SharpPlayer player, DBRef reference)
		=> !reference.IsAccountHolder()
			? new AnyOptionalSharpObject(new AnySharpObject(player))
			: await Mediator.Send(new GetAccountHolderQuery(reference), ExecutionBudget.CurrentToken) is SharpAccountHolder account
				? new AnyOptionalSharpObject(new AnySharpObject(account))
				: new AnyOptionalSharpObject(new None());
}
