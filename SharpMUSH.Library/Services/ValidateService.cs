using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text;
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
	// Thread-safe cache for compiled regexes for attribute validation
	private readonly ConcurrentDictionary<string, Regex> _regexCache = new();
	// Thread-safe cache for compiled glob patterns
	private readonly ConcurrentDictionary<string, Regex> _globCache = new();
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
			
			// Extract the attribute name after "User:" using Span
			var colonIndex = lockTypeName.IndexOf(':');
			if (colonIndex >= 0 && colonIndex < lockTypeName.Length - 1)
			{
				var attributeName = lockTypeName.AsSpan(colonIndex + 1).ToString().ToUpper();
				
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
		// Thread-safe cache access for compiled regexes
		var reg = _regexCache.GetOrAdd(name, _ => new Regex(regex, RegexOptions.Compiled));
		return reg.IsMatch(value);
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
		// Check attribute value byte length using configured limit
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
	/// Checks if a value matches any of the enum patterns, supporting glob wildcards (* and ?).
	/// Uses thread-safe caching to avoid recompiling regex patterns.
	/// </summary>
	/// <param name="value">The value to check</param>
	/// <param name="enumPatterns">Array of allowed patterns (can include * and ? wildcards)</param>
	/// <returns>True if value matches any pattern</returns>
	private bool MatchesEnumWithGlobbing(string value, string[] enumPatterns)
	{
		foreach (var pattern in enumPatterns)
		{
			// If pattern has no wildcards, do exact match for performance
			if (!pattern.Contains('*') && !pattern.Contains('?'))
			{
				if (value.Equals(pattern, StringComparison.Ordinal))
				{
					return true;
				}
				continue;
			}
			
			// Thread-safe cache access for compiled regex patterns
			var regex = _globCache.GetOrAdd(pattern, p =>
			{
				// Convert glob pattern to regex
				var regexPattern = "^" + Regex.Escape(p)
					.Replace("\\*", ".*")  // * matches any characters
					.Replace("\\?", ".")   // ? matches single character
					+ "$";
				
				return new Regex(regexPattern, RegexOptions.Compiled);
			});
			
			if (regex.IsMatch(value))
			{
				return true;
			}
		}
		
		return false;
	}

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