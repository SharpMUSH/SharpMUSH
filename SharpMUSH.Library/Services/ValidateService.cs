using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Markup;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Library.Services;

public partial class ValidateService(
	IMediator mediator,
	IOptionsWrapper<SharpMUSHOptions> configuration,
	ILockService lockService)
	: IValidateService
{

	/// <summary>Names that always resolve to something else, so nothing may be called by them.</summary>
	private static readonly HashSet<string> MagicCookies = new(["me", "here", "!", "home"], StringComparer.Ordinal);

	private static readonly HashSet<string> MagicCookiesIgnoreCase = new(MagicCookies, StringComparer.OrdinalIgnoreCase);

	public async ValueTask<bool> Valid(IValidateService.ValidationType type, MString value,
		ValidationTarget target)
		=> type switch
		{
			_ when value.Length == 0
				=> false,
			IValidateService.ValidationType.Name
				=> ValidateName(value),
			IValidateService.ValidationType.PlayerName when target is AnySharpObject player
				=> await ValidatePlayerName(value, player),
			IValidateService.ValidationType.PlayerAlias when target is AnySharpObject player
				=> ValidatePlayerAlias(value, player),
			IValidateService.ValidationType.AttributeName
				=> ValidateAttributeName(value.ToPlainText()),
			IValidateService.ValidationType.AttributeValue when target is SharpAttributeEntry entry
				=> ValidateAttributeValue(value, entry),
			IValidateService.ValidationType.AttributeValue
				=> ValidateAttributeValueBasic(value),
			IValidateService.ValidationType.ColorName
				=> true,
			IValidateService.ValidationType.AnsiCode
				=> true,
			IValidateService.ValidationType.CommandName
				=> ValidCommandNameRegex().IsMatch(value.ToPlainText()),
			IValidateService.ValidationType.LockKey when target is AnySharpObject lockee
				=> lockService.Validate(value.ToPlainText(), lockee),
			IValidateService.ValidationType.LockType
				=> ValidateLockType(value),
			IValidateService.ValidationType.BoolExp
				=> ValidateLockType(value),
			IValidateService.ValidationType.FlagName
				=> ValidAttributeNameRegex().IsMatch(value.ToPlainText()),
			IValidateService.ValidationType.PowerName
				=> ValidAttributeNameRegex().IsMatch(value.ToPlainText()),
			IValidateService.ValidationType.ChannelName when target is None
				=> ChannelNameRegex().IsMatch(value.ToPlainText()),
			IValidateService.ValidationType.ChannelName when target is SharpChannel channel
				=> channel.Name.ToPlainText() == value.ToPlainText()
					 || ChannelNameRegex().IsMatch(value.ToPlainText()),
			IValidateService.ValidationType.Password
				=> PasswordRegex().IsMatch(value.ToPlainText()),
			IValidateService.ValidationType.QRegisterName
				=> ValidAttributeNameRegex().IsMatch(value.ToPlainText()) && value.ToPlainText()[0] != '-',
			IValidateService.ValidationType.Timezone
				=> TimeZoneInfo.TryFindSystemTimeZoneById(value.ToPlainText(), out _),
			IValidateService.ValidationType.FunctionName
				=> FunctionNameRegex().IsMatch(value.ToPlainText()),
			_
				=> throw new InvalidEnumArgumentException(type.ToString())
		};

	private bool ValidateLockType(MString value)
	{
		var lockTypeName = value.ToPlainText();

		// Empty or null means "Basic" lock
		if (string.IsNullOrEmpty(lockTypeName))
		{
			return true;
		}

		if (Enum.TryParse<LockType>(lockTypeName, ignoreCase: true, out _))
		{
			return true;
		}

		// Check for user-defined locks (format: "User:AttributeName")
		if (lockTypeName.StartsWith("User:", StringComparison.OrdinalIgnoreCase))
		{
			if (lockTypeName.Contains('|'))
			{
				return false;
			}

			var colonIndex = lockTypeName.IndexOf(':');
			if (colonIndex >= 0 && colonIndex < lockTypeName.Length - 1)
			{
				return ValidAttributeNameRegex().IsMatch(lockTypeName.AsSpan(colonIndex + 1));
			}

			return false;
		}

		return false;
	}

	[GeneratedRegex(@"^\P{C}+$")]
	private partial Regex ChannelNameRegex();

	[GeneratedRegex(@"^\P{C}+$")]
	private partial Regex PasswordRegex();

	[GeneratedRegex(@"^[^:;""#\\&\]\p{C}]\P{C}*$")]
	private partial Regex FunctionNameRegex();

	/// <summary>
	/// Whether <paramref name="value"/> may be stored in the attribute <paramref name="attribute"/>
	/// describes: within the configured byte limit, and accepted by its <c>@attribute/limit</c> and
	/// <c>@attribute/enum</c> (<see cref="AttributeValueRestriction"/>).
	/// </summary>
	private bool ValidateAttributeValue(MString value, SharpAttributeEntry attribute)
		=> ValidateAttributeValueBasic(value)
			&& AttributeValueRestriction.Check(attribute, value.ToPlainText()) is string;

	/// <summary>
	/// Validates an attribute value without a specific target attribute — only checks byte length.
	/// </summary>
	private bool ValidateAttributeValueBasic(MString value)
	{
		var plainValue = value.ToPlainText();
		var maxBytes = (int)configuration.CurrentValue.Limit.MaxAttributeValueLength;
		return Encoding.UTF8.GetByteCount(plainValue) <= maxBytes;
	}

	/// <summary>
	/// Validates an attribute name: must match the character set regex AND must not have
	/// backticks at the start/end or consecutive backticks (PennMUSH parity).
	/// </summary>
	private static bool ValidateAttributeName(string name) =>
		ValidAttributeNameRegex().IsMatch(name)
		&& !name.StartsWith('`')
		&& !name.EndsWith('`')
		&& !name.Contains("``");

	[GeneratedRegex("^[!\"#%&\\(\\)\\+,\\-\\./0-9A-Za-z:;\\<\\>=\\?@`_]+$")]
	private static partial Regex ValidAttributeNameRegex();

	[GeneratedRegex("^[^:;\"#\\\\&\\]\\[\\p{C}]+$")]
	private static partial Regex ValidCommandNameRegex();

	private static bool ValidatePlayerAlias(MString value, AnySharpObject target)
	{
		// Player aliases should be non-empty and contain valid characters
		// They're less strict than full player names but still need basic validation
		var plainAlias = value.ToPlainText();

		if (string.IsNullOrWhiteSpace(plainAlias))
		{
			return false;
		}

		// Aliases cannot be magic cookies
		if (MagicCookiesIgnoreCase.Contains(plainAlias))
		{
			return false;
		}

		if (plainAlias.Any(char.IsControl))
		{
			return false;
		}

		return plainAlias.EnumerateRunes().All(x => x.IsAscii);
	}

	private async ValueTask<bool> ValidatePlayerName(MString name, AnySharpObject target)
	{
		var plainName = name.ToPlainText();

		if (!ValidateName(name))
		{
			return false;
		}

		if (name.Length > configuration.CurrentValue.Limit.PlayerNameLen)
		{
			return false;
		}

		if (!configuration.CurrentValue.Cosmetic.PlayerNameSpaces && plainName.Contains(' ') && !await target.IsWizard())
		{
			return false;
		}

		// ok_player_name: a name matching a banned pattern is only for a wizard, or for the player
		// who already has it.
		if (IsBannedName(plainName)
				&& !plainName.Equals(target.Object().Name, StringComparison.OrdinalIgnoreCase)
				&& !await target.IsWizard())
		{
			return false;
		}

		var tryFindPlayerByName = mediator
			.CreateStream(new GetPlayerQuery(plainName))
			.Where(x => x.Object.DBRef != target.Object().DBRef);

		return !await tryFindPlayerByName
			.AnyAsync(x => x.Object.Name.Equals(plainName, StringComparison.InvariantCultureIgnoreCase));
	}

	/// <summary>
	/// PennMUSH's <c>forbidden_name</c> (<c>src/predicat.c:622</c>): whether <paramref name="name"/>
	/// matches any <c>@sitelock/name</c> pattern, as a caseless wildcard match of the whole name.
	/// </summary>
	private bool IsBannedName(string name)
		=> configuration.CurrentValue.BannedNames.BannedNames
			.Any(pattern => MushText.IsWildcardMatch(MarkupText.Plain(name), pattern));

	/// <summary>
	/// A legal object name: at least one character, no leading or trailing space, no control
	/// characters anywhere, and none of <c>[ ] % \ = &amp; |</c>. Interior spaces are fine
	/// ("a red ball"), and so is <c>;</c> — <c>@open</c> splits exit aliases on it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Anchored with <c>\A</c>/<c>\z</c> rather than <c>^</c>/<c>$</c>: in .NET <c>$</c> also
	/// matches immediately before a trailing newline, so <c>$</c> accepts <c>"name\n"</c>.
	/// </para>
	/// <para>
	/// The previous pattern had <c>$</c> but no start anchor at all, and its middle term matched the
	/// forbidden set instead of its complement, so <see cref="Regex.IsMatch"/> could satisfy the
	/// whole expression against the last character or two of any input — every forbidden character
	/// passed as long as it was not at the very end.
	/// </para>
	/// </remarks>
	[GeneratedRegex(@"\A[^ \p{C}\[\]%\\=&\|](?:[^\p{C}\[\]%\\=&\|]*[^ \p{C}\[\]%\\=&\|])?\z")]
	private partial Regex NameRegex();

	private bool ValidateName(MString value)
	{
		var plain = value.ToPlainText();

		if (!NameRegex().IsMatch(plain))
		{
			return false;
		}

		if (MagicCookies.Contains(plain))
		{
			return false;
		}

		return plain.EnumerateRunes().All(x => x.IsAscii);
	}
}