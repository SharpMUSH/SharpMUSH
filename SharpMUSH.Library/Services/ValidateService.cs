using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using System.Buffers;
using System.Collections.Concurrent;
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
	private readonly ConcurrentDictionary<string, Regex> _regexCache = new();

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

	private bool CheckAttributeRegex(string name, string regex, string value)
	{
		var reg = _regexCache.GetOrAdd(name, _ => SoftcodeRegex.Create(regex, RegexOptions.Compiled));
		return SoftcodeRegex.IsMatch(reg, value);
	}

	/// <summary>
	/// Checks if an attribute value is valid against a SharpAttributeEntry.
	/// Supports enum validation with wildcard globbing patterns.
	/// Enforces maximum attribute value length from configuration.
	/// </summary>
	/// <param name="value">Value</param>
	/// <param name="attribute">Attribute Entry</param>
	/// <returns>True or false</returns>
	private bool ValidateAttributeValue(MString value, SharpAttributeEntry attribute)
	{
		// Convert to plain text and measure UTF-8 bytes for multi-byte character support
		var plainValue = value.ToPlainText();
		var maxBytes = (int)configuration.CurrentValue.Limit.MaxAttributeValueLength;

		if (Encoding.UTF8.GetByteCount(plainValue) > maxBytes)
		{
			return false;
		}

		return attribute switch
		{
			{ Limit: null } and { Enum: null } => true,
			{ Limit: not null } and { Enum: not null }
				=> MatchesEnumWithGlobbing(plainValue, attribute.Enum)
					 && CheckAttributeRegex(attribute.Name, attribute.Limit, plainValue),
			{ Enum: not null }
				=> MatchesEnumWithGlobbing(plainValue, attribute.Enum),
			{ Limit: not null }
				=> CheckAttributeRegex(attribute.Name, attribute.Limit, plainValue),
			_ => false
		};
	}

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
	/// Whether <paramref name="value"/> matches any of <paramref name="enumPatterns"/>, each a general
	/// MUSH wildcard (<see cref="SoftcodeRegex.Wildcard"/>) matched case-sensitively. A pattern with no
	/// <c>*</c>, <c>?</c> or <c>\</c> is compared ordinally without compiling anything.
	/// </summary>
	/// <remarks>
	/// Globbing enum entries is SharpMUSH's own. PennMUSH's <c>@attribute/enum</c> is a delimited list
	/// matched case-insensitively by prefix, storing the entry's own case (<c>src/atr_tab.c</c>).
	/// </remarks>
	private static bool MatchesEnumWithGlobbing(string value, string[] enumPatterns)
		=> enumPatterns.Any(pattern => pattern.AsSpan().IndexOfAny(GlobMetacharacters) < 0
			? value.Equals(pattern, StringComparison.Ordinal)
			: SoftcodeRegex.IsMatch(SoftcodeRegex.Wildcard(pattern, caseSensitive: true), value));

	private static readonly SearchValues<char> GlobMetacharacters = SearchValues.Create("*?\\");

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

		// Check against forbidden names list (would require configuration: forbidden_player_names)
		// For now, allow all names that pass other validation

		var tryFindPlayerByName = mediator
			.CreateStream(new GetPlayerQuery(plainName))
			.Where(x => x.Object.DBRef != target.Object().DBRef);

		return !await tryFindPlayerByName
			.AnyAsync(x => x.Object.Name.Equals(plainName, StringComparison.InvariantCultureIgnoreCase));
	}

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