using Mediator;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Common;

/// <summary>What <c>@icmd</c> does to one command for a player, or for every player in a location.</summary>
public enum IcmdSetting
{
	/// <summary>The command runs as usual.</summary>
	On,

	/// <summary>The command is refused with "Permission denied.".</summary>
	Disabled,

	/// <summary>The command is not a command at all: the line goes on to $-commands and then Huh?.</summary>
	Ignored
}

/// <summary>
/// The ICMD attribute <c>@icmd</c> keeps: a <c>disable:&lt;COMMAND&gt;</c> or <c>ignore:&lt;COMMAND&gt;</c> word per
/// command, by its full name, and <c>unchecked</c> on a player whose settings are switched off.
/// </summary>
public sealed record IcmdList(IReadOnlyDictionary<string, IcmdSetting> Commands, bool Checked)
{
	/// <summary>The attribute the settings live in.</summary>
	public const string Attribute = "ICMD";

	private const string UncheckedWord = "unchecked";

	public static readonly IcmdList Empty = new(new Dictionary<string, IcmdSetting>(), true);

	/// <summary>
	/// The settings on <paramref name="holder"/>: its ICMD attribute counts only when God owns it and it is flagged
	/// <c>wizard</c>, as <c>@icmd</c> leaves it. Any other ICMD attribute changes nothing.
	/// </summary>
	public static async ValueTask<IcmdList> OnAsync(IMediator mediator, DBRef holder)
		=> await mediator.CreateStream(new GetAttributeQuery(holder, [Attribute])).LastOrDefaultAsync() is { } attribute
			&& attribute.IsWizard()
			&& await attribute.Owner.WithCancellation(CancellationToken.None) is { Object.DBRef.Number: 1 }
				? Parse(attribute.Value.ToPlainText())
				: Empty;

	/// <summary>The settings in an ICMD value; a word in no known form is skipped.</summary>
	public static IcmdList Parse(string? value)
	{
		var commands = new Dictionary<string, IcmdSetting>(StringComparer.OrdinalIgnoreCase);
		var isChecked = true;
		foreach (var word in (value ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			if (word.Equals(UncheckedWord, StringComparison.OrdinalIgnoreCase))
			{
				isChecked = false;
				continue;
			}

			var colon = word.IndexOf(':');
			if (colon <= 0 || colon == word.Length - 1) continue;
			var setting = word[..colon].ToLowerInvariant() switch
			{
				"disable" => IcmdSetting.Disabled,
				"ignore" => IcmdSetting.Ignored,
				_ => IcmdSetting.On
			};
			if (setting != IcmdSetting.On) commands[word[(colon + 1)..]] = setting;
		}

		return new IcmdList(commands, isChecked);
	}

	/// <summary>What the list does to <paramref name="command"/>: nothing while its checking is switched off.</summary>
	public IcmdSetting For(string command)
		=> Checked && Commands.TryGetValue(command, out var setting) ? setting : IcmdSetting.On;

	/// <summary>The ICMD value for this list, empty when it changes nothing.</summary>
	public string Format()
		=> string.Join(' ', Commands
			.Where(entry => entry.Value != IcmdSetting.On)
			.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
			.Select(entry => $"{(entry.Value == IcmdSetting.Disabled ? "disable" : "ignore")}:{entry.Key.ToUpperInvariant()}")
			.Prepend(Checked ? string.Empty : UncheckedWord)
			.Where(word => word.Length > 0));
}
