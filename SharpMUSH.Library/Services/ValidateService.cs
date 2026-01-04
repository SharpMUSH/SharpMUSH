using System.ComponentModel;
using System.Text.RegularExpressions;
using Mediator;
using Microsoft.Extensions.Options;
using OneOf;
using OneOf.Types;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public partial class ValidateService(
	IMediator mediator,
	IOptionsWrapper<SharpMUSHOptions> configuration,
	ILockService lockService)
	: IValidateService
{
	// Cache compiled regexes for attribute validation
	private readonly Dictionary<string, Regex> _regexCache = new();
	public async ValueTask<bool> Valid(IValidateService.ValidationType type, MString value,
		OneOf<AnySharpObject, SharpAttributeEntry, SharpChannel, None> target)
		=> type switch
		{
			_ when value.Length == 0
				=> false,
			IValidateService.ValidationType.Name
				=> ValidateName(value),
			IValidateService.ValidationType.PlayerName when target is { IsT0: true }
				=> await ValidatePlayerName(value, target.AsT0),
			IValidateService.ValidationType.PlayerAlias when target is { IsT0: true }
				=> ValidatePlayerAlias(value, target.AsT0),
			IValidateService.ValidationType.AttributeName
				=> ValidAttributeNameRegex().IsMatch(value.ToPlainText()),
			IValidateService.ValidationType.AttributeValue when target is { IsT1: true }
				=> ValidateAttributeValue(value, target.AsT1),
			IValidateService.ValidationType.ColorName
				=> true,
			IValidateService.ValidationType.AnsiCode
				=> true,
			IValidateService.ValidationType.CommandName
				=> ValidCommandNameRegex().IsMatch(value.ToPlainText()),
			IValidateService.ValidationType.LockKey when target is { IsT0: true }
				=> lockService.Validate(value.ToPlainText(), target.AsT0),
			IValidateService.ValidationType.LockType
				=> ValidateLockType(value),
			IValidateService.ValidationType.BoolExp
				=> ValidateLockType(value),
			IValidateService.ValidationType.FlagName
				=> ValidAttributeNameRegex().IsMatch(value.ToPlainText()),
			IValidateService.ValidationType.PowerName
				=> ValidAttributeNameRegex().IsMatch(value.ToPlainText()),
			IValidateService.ValidationType.ChannelName when target is { IsT3: true }
				=> ChannelNameRegex().IsMatch(value.ToPlainText()),
			IValidateService.ValidationType.ChannelName when target is { IsT2: true, AsT2: var channel}
				=> channel.Name.ToPlainText() == value.ToPlainText() 
				   ||  ChannelNameRegex().IsMatch(value.ToPlainText()),
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
		
		// Check if it's a standard lock type (try to parse the enum)
		if (Enum.TryParse<LockType>(lockTypeName, ignoreCase: true, out _))
		{
			return true;
		}
		
		// Check for user-defined locks (format: "User:AttributeName")
		if (lockTypeName.StartsWith("User:", StringComparison.OrdinalIgnoreCase))
		{
			// Cannot contain pipe character
			if (lockTypeName.Contains('|'))
			{
				return false;
			}
			
			// Extract the attribute name after "User:"
			var colonIndex = lockTypeName.IndexOf(':');
			if (colonIndex >= 0 && colonIndex < lockTypeName.Length - 1)
			{
				var attributeName = lockTypeName.Substring(colonIndex + 1).ToUpper();
				
				// Validate as attribute name
				return ValidAttributeNameRegex().IsMatch(attributeName);
			}
			
			return false;
		}
		
		// Unknown lock type
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
		// Cache regex by attribute name for performance
		if (!_regexCache.TryGetValue(name, out var reg))
		{
			reg = new Regex(regex, RegexOptions.Compiled);
			_regexCache[name] = reg;
		}
		return reg.IsMatch(value);
	}

	/// <summary>
	/// Checks if an attribute value is valid against a SharpAttributeEntry.
	/// TODO: Ensure enum can do globbing.
	/// TODO: Add length check for attribute values.
	/// </summary>
	/// <param name="value">Value</param>
	/// <param name="attribute">Attribute Entry</param>
	/// <returns>True or false</returns>
	private bool ValidateAttributeValue(MString value, SharpAttributeEntry attribute) =>
		attribute switch
		{
			{ Limit: null } and { Enum: null } => true,
			{ Limit: not null } and { Enum: not null }
				=> attribute.Enum.Contains(value.ToPlainText())
				   && CheckAttributeRegex(attribute.Name, attribute.Limit, value.ToPlainText()),
			{ Enum: not null }
				=> attribute.Enum.Contains(value.ToPlainText()),
			{ Limit: not null }
				=> CheckAttributeRegex(attribute.Name, attribute.Limit, value.ToPlainText()),
			_ => false
		};

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
		var magicCookie = new HashSet<string>((string[])["me", "here", "!", "home"]);
		if (magicCookie.Contains(plainAlias.ToLower()))
		{
			return false;
		}
		
		// Aliases should not contain control characters
		if (plainAlias.Any(c => char.IsControl(c)))
		{
			return false;
		}
		
		// Aliases must be ASCII
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

	[GeneratedRegex(@"[^ \[\]%\\=&\|][\[\]%\\=&\|]*?[^ \[\]%\\=&\|]?$")]
	private partial Regex NameRegex();

	private bool ValidateName(MString value)
	{
		var plain = value.ToPlainText();
		var magicCookie = new HashSet<string>((string[])["me", "here", "!", "home"]);

		if (!NameRegex().IsMatch(plain))
		{
			return false;
		}

		if (magicCookie.Contains(plain))
		{
			return false;
		}

		return plain.EnumerateRunes().All(x => x.IsAscii);
	}
}