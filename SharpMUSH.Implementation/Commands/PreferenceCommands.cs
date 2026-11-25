using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Models.Packages;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;
using CB = SharpMUSH.Library.Definitions.CommandBehavior;

namespace SharpMUSH.Implementation.Commands;

public partial class Commands
{
	/// <summary>
	/// <c>@preference</c>: the player's own preferences, the groups installed packages declare (<c>preferences:</c>).
	/// Bare, it lists the groups; given a group, its preferences; <c>&lt;group&gt;/&lt;key&gt;=&lt;value&gt;</c> sets one,
	/// checked as the package declares it; <c>/reset</c> returns a group, or one key, to its defaults. The values are the
	/// ones the portal's Preferences page and <c>api/preferences</c> show, kept on the account or the character.
	/// </summary>
	[SharpCommand(Name = "@PREFERENCE", Switches = ["RESET"],
		Behavior = CB.Default | CB.EqSplit | CB.RSNoParse | CB.NoGagged | CB.NoGuest, MinArgs = 0, MaxArgs = 2,
		ParameterNames = ["group/key", "value"])]
	public async ValueTask<Option<CallState>> Preference(IMUSHCodeParser parser, SharpCommandAttribute _)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		var args = parser.CurrentState.ArgumentList;
		var left = (args.GetValueOrDefault("0")?.Message.ToPlainText() ?? "").Trim();
		var preferences = parser.ServiceProvider.GetRequiredService<IPreferenceService>();

		var output = executor is SharpPlayer player
			? (parser.CurrentState.Switches.Contains("RESET"), left.Length, args.ContainsKey("1")) switch
			{
				(true, 0, _) => MarkupText.Plain("Usage: @preference/reset <group>[/<key>]"),
				(true, _, _) => MarkupText.Plain(await PreferenceResetAsync(preferences, player, left)),
				(false, 0, _) => await PreferenceGroupsAsync(parser, preferences),
				(false, _, true) => MarkupText.Plain(await PreferenceSetAsync(preferences, player, left,
					args["1"].Message.ToPlainText())),
				(false, _, false) => await PreferenceShowAsync(parser, preferences, player, left)
			}
			: MarkupText.Plain("Only a player has preferences.");

		await NotifyService.Notify(executor, output);
		return new CallState(output);
	}

	private static async ValueTask<MString> PreferenceGroupsAsync(IMUSHCodeParser parser, IPreferenceService preferences)
	{
		var groups = await preferences.GetGroupsAsync(ExecutionBudget.CurrentToken);
		if (groups.Count == 0)
		{
			return MarkupText.Plain("No installed package declares preferences.");
		}

		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Group")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Kept on")) { Wrap = false, Priority = 2 },
				new TableColumn(MarkupText.Plain("What it is")) { Min = 10 },
			],
			groups.Select(group => new[]
			{
				group.Id,
				Preferences.ScopeName(group.Spec.Scope),
				group.Spec.Label.Length > 0 ? group.Spec.Label : group.Spec.Description
			}));
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain("Preferences"), table), 78, parser.HouseLook());
	}

	private static async ValueTask<MString> PreferenceShowAsync(IMUSHCodeParser parser, IPreferenceService preferences,
		SharpPlayer player, string groupId)
	{
		var token = ExecutionBudget.CurrentToken;
		if (await preferences.FindGroupAsync(groupId, token) is not PreferenceGroup group)
		{
			return MarkupText.Plain(NoPreferenceGroup(groupId));
		}

		return await preferences.HolderAsync(group, null, player, token) switch
		{
			DBRef holder => PreferenceTable(parser, group, await preferences.ReadAsync(group, holder, token)),
			Error<string> refused => MarkupText.Plain(refused.Value)
		};
	}

	private static MString PreferenceTable(IMUSHCodeParser parser, PreferenceGroup group, IReadOnlyList<PreferenceReading> readings)
	{
		var table = ServerLayout.Listing(
			[
				new TableColumn(MarkupText.Plain("Key")) { Wrap = false },
				new TableColumn(MarkupText.Plain("Value")) { Min = 10 },
				new TableColumn(MarkupText.Plain("Allows")) { Priority = 2 },
			],
			readings.Select(reading => new[]
			{
				reading.Spec.Key,
				reading.IsSet ? reading.Value : $"{reading.Value} (default)",
				Allows(reading.Spec)
			}));
		var title = group.Spec.Label.Length > 0 ? $"{group.Spec.Label} ({group.Id})" : group.Id;
		return ServerLayout.Build(ServerLayout.Panel(MarkupText.Plain($"{title}, kept on your {Preferences.ScopeName(group.Spec.Scope)}"), table),
			78, parser.HouseLook());
	}

	/// <summary>What a preference takes, in a few words.</summary>
	private static string Allows(PackagePreferenceSpec spec) => spec.Type switch
	{
		PreferenceType.Choice => string.Join(", ", spec.Values),
		PreferenceType.Boolean => "1 or 0",
		PreferenceType.Number or PreferenceType.Integer => (spec.Min, spec.Max) switch
		{
			({ } min, { } max) => $"{Preferences.TypeName(spec.Type)}, {Preferences.Format(min)} to {Preferences.Format(max)}",
			({ } min, null) => $"{Preferences.TypeName(spec.Type)}, at least {Preferences.Format(min)}",
			(null, { } max) => $"{Preferences.TypeName(spec.Type)}, at most {Preferences.Format(max)}",
			_ => Preferences.TypeName(spec.Type)
		},
		_ => Preferences.TypeName(spec.Type)
	};

	private static async ValueTask<string> PreferenceSetAsync(IPreferenceService preferences, SharpPlayer player,
		string left, string value)
	{
		var token = ExecutionBudget.CurrentToken;
		var slash = left.IndexOf('/');
		if (slash <= 0 || slash == left.Length - 1)
		{
			return "Usage: @preference <group>/<key>=<value>";
		}

		var (groupId, keyName) = (left[..slash].Trim(), left[(slash + 1)..].Trim());
		if (await preferences.FindGroupAsync(groupId, token) is not PreferenceGroup group)
		{
			return NoPreferenceGroup(groupId);
		}

		return await preferences.HolderAsync(group, null, player, token) switch
		{
			DBRef holder => await preferences.WriteAsync(group, holder, new Dictionary<string, string> { [keyName] = value }, token) switch
			{
				IReadOnlyList<PreferenceReading> readings => Changed(group,
					readings.First(r => string.Equals(r.Spec.Key, keyName, StringComparison.OrdinalIgnoreCase))),
				Error<string> refused => refused.Value
			},
			Error<string> refused => refused.Value
		};
	}

	private static string Changed(PreferenceGroup group, PreferenceReading reading)
		=> $"{group.Id}/{reading.Spec.Key} is now {reading.Value}.";

	private static async ValueTask<string> PreferenceResetAsync(IPreferenceService preferences, SharpPlayer player, string left)
	{
		var token = ExecutionBudget.CurrentToken;
		var slash = left.IndexOf('/');
		var groupId = (slash < 0 ? left : left[..slash]).Trim();
		var keyName = slash < 0 ? null : left[(slash + 1)..].Trim();
		if (await preferences.FindGroupAsync(groupId, token) is not PreferenceGroup group)
		{
			return NoPreferenceGroup(groupId);
		}

		return await preferences.HolderAsync(group, null, player, token) switch
		{
			DBRef holder => await preferences.ResetAsync(group, holder, keyName is { Length: > 0 } ? [keyName] : [], token) switch
			{
				IReadOnlyList<PreferenceReading> => keyName is { Length: > 0 }
					? $"{group.Id}/{keyName.ToLowerInvariant()} is back to its default."
					: $"{group.Id} is back to its defaults.",
				Error<string> refused => refused.Value
			},
			Error<string> refused => refused.Value
		};
	}

	private static string NoPreferenceGroup(string groupId)
		=> $"No installed package declares the preference group '{groupId}'. See @preference.";
}
