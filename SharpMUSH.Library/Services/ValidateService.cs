using Mediator;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Authorization;
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

	public async ValueTask<bool> Valid(IValidateService.ValidationType type, MString value,
		ValidationTarget target)
		=> type switch
		{
			_ when value.Length == 0
				=> false,
			IValidateService.ValidationType.Name
				=> ValidateName(value),
			// fun_valid asks ok_player_name(name, target, target); with no target, a new player's name.
			IValidateService.ValidationType.PlayerName when target is AnySharpObject player
				=> await ValidPlayerName(value, player, player),
			IValidateService.ValidationType.PlayerName when target is None
				=> await ValidPlayerName(value, new None(), new None()),
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
			IValidateService.ValidationType.RoleName
				=> RoleNames.IsValidShortName(value.ToPlainText()),
			IValidateService.ValidationType.RoleCategory
				=> Categories.IsValidName(value.ToPlainText()),
			IValidateService.ValidationType.Permission
				=> CustomPermissions.IsValidName(value.ToPlainText()),
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

	[GeneratedRegex("^" + HelperFunctions.AttributeNameCharacters + "+$")]
	private static partial Regex ValidAttributeNameRegex();

	[GeneratedRegex("^[^:;\"#\\\\&\\]\\[\\p{C}]+$")]
	private static partial Regex ValidCommandNameRegex();

	public async ValueTask<bool> ValidPlayerName(MString name, AnyOptionalSharpObject player, AnyOptionalSharpObject thing)
	{
		var plainName = name.ToPlainText();

		if (!ValidateName(name) || name.Length > configuration.CurrentValue.Limit.PlayerNameLen)
		{
			return false;
		}

		var privileged = player is AnySharpObject asker && await asker.IsWizard();
		if (!configuration.CurrentValue.Cosmetic.PlayerNameSpaces && plainName.Contains(' ') && !privileged)
		{
			return false;
		}

		// lookup_player: the player list holds every player's name and every alias, so a name another
		// player answers to by alias is as taken as one it is called.
		DBRef? carrier = thing is AnySharpObject named ? named.Object().DBRef : null;
		var holders = await mediator.CreateStream(new GetPlayerQuery(plainName))
			.Select(x => x.Object.DBRef)
			.ToArrayAsync();

		// A player may only change to a banned name if they are already using it.
		if (IsBannedName(plainName) && !privileged && !(carrier is { } own && holders.Contains(own)))
		{
			return false;
		}

		return holders.All(holder => holder == carrier);
	}

	/// <summary>
	/// PennMUSH's <c>forbidden_name</c> (<c>src/predicat.c:622</c>): whether <paramref name="name"/>
	/// matches any <c>@sitelock/name</c> pattern, as a caseless wildcard match of the whole name.
	/// </summary>
	private bool IsBannedName(string name)
		=> configuration.CurrentValue.BannedNames.BannedNames
			.Any(pattern => MushText.IsWildcardMatch(MarkupText.Plain(name), pattern));

	private static bool ValidateName(MString value) => ObjectNames.IsLegal(value.ToPlainText());
}
