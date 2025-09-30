using System.ComponentModel;
using System.Text.RegularExpressions;
using Mediator;
using Microsoft.Extensions.Options;
using OneOf;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public partial class ValidateService(
	IMediator mediator,
	IOptionsMonitor<PennMUSHOptions> configuration,
	ILockService lockService)
	: IValidateService
{
	public async ValueTask<bool> Valid(IValidateService.ValidationType type, MString value,
		OneOf<AnySharpObject, SharpAttributeEntry>? target = null)
		=> type switch
		{
			_ when value.Length == 0
				=> false,
			IValidateService.ValidationType.Name
				=> ValidateName(value),
			IValidateService.ValidationType.PlayerName when target is { IsT0: true }
				=> await ValidatePlayerName(value, target.Value.AsT0),
			IValidateService.ValidationType.PlayerAlias when target is { IsT0: true }
				=> ValidatePlayerAlias(value, target.Value.AsT0),
			IValidateService.ValidationType.AttributeName
				=> ValidAttributeNameRegex().IsMatch(value.ToPlainText()),
			IValidateService.ValidationType.AttributeValue when target is { IsT1: true }
				=> ValidateAttributeValue(value, target.Value.AsT1),
			IValidateService.ValidationType.ColorName
				=> true,
			IValidateService.ValidationType.AnsiCode
				=> true,
			IValidateService.ValidationType.CommandName
				=> ValidCommandNameRegex().IsMatch(value.ToPlainText()),
			IValidateService.ValidationType.LockKey when target is { IsT0: true }
				=> lockService.Validate(value.ToPlainText(), target.Value.AsT0),
			IValidateService.ValidationType.LockType
				=> ValidateLockType(value),
			IValidateService.ValidationType.BoolExp
				=> ValidateLockType(value),
			IValidateService.ValidationType.FlagName
				=> ValidAttributeNameRegex().IsMatch(value.ToPlainText()),
			IValidateService.ValidationType.PowerName
				=> ValidAttributeNameRegex().IsMatch(value.ToPlainText()),
			IValidateService.ValidationType.ChannelName
				=> ChannelNameRegex().IsMatch(value.ToPlainText()),
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
		/*
/** Check to see that the lock type is a valid type. Valid types are either:
 * 1) in the lock table of standard lock types,
 * 2) prefixed with "user:", a valid attr name and don't contain '|' chars
 * 3) the name of an already-existing lock set on 'thing'
 * It was previously stated that '|' in lock names interfered with the db
 * reading routines; I (MG) don't believe that's the case any longer, though.
 * \param player the enactor, for notification.
 * \param thing object on which to check the lock.
 * \param silent skip specific error notifications to player?
 * \param name name of lock type.
 * \retval NULL invalid lock type
 * \retval locktype the name of the valid lock type, minus user: prefix
 * /
lock_type
check_lock_type(dbref player, dbref thing, lock_type name, bool silent)
{
  lock_type ll;
  char *user_name;
  char *colon;

  /* Special-case for basic locks. * /
  if (!name || !*name)
    return Basic_Lock;

  /* Normal locks. * /
  ll = match_lock(name);
  if (ll != NULL)
    return ll;

  /* If the lock is set, it's allowed, whether it exists normally or not. * /
  if (getlock(thing, name) != TRUE_BOOLEXP)
    return name;

  /* Check to see if it's a well-formed user-defined lock. * /
  if (!string_prefix(name, "User:")) {
    if (!silent)
      notify(player, T("Unknown lock type."));
    return NULL;
  }
  if (strchr(name, '|')) {
    if (!silent)
      notify(player, T("The character \'|\' may not be used in lock names."));
    return NULL;
  }
  colon = strchr(name, ':') + 1;
  user_name = strupper(colon);

  if (!good_atr_name(user_name)) {
    if (!silent)
      notify(player, T("That is not a valid lock name."));
    return NULL;
  }

  return colon;
}*/
		throw new NotImplementedException();
	}

	[GeneratedRegex(@"^\P{C}+$")]
	private partial Regex ChannelNameRegex();

	[GeneratedRegex(@"^\P{C}+$")]
	private partial Regex PasswordRegex();

	[GeneratedRegex(@"^[^:;""#\\&\]\p{C}]\P{C}*$")]
	private partial Regex FunctionNameRegex();

	private static bool CheckAttributeRegex(string name, string regex, string value)
	{
		// TODO: Cache by name.
		var reg = new Regex(regex);
		return reg.IsMatch(value);
	}

	/// <summary>
	/// Checks if an attribute value is valid against a SharpAttributeEntry.
	/// TODO: Caching & ensuring enum can do globbing.
	/// CONSIDER: Probably should do a LENGTH check as well.
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

	[GeneratedRegex("^[!\"#%&()&+,\\-./0-9A-Z:;<>=?@`_]+$")]
	private static partial Regex ValidAttributeNameRegex();

	[GeneratedRegex("^[^:;\"#\\\\&\\]\\[\\p{C}]+$")]
	private static partial Regex ValidCommandNameRegex();

	private static bool ValidatePlayerAlias(MString value, AnySharpObject target)
	{
		throw new NotImplementedException();
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

		// TODO: Forbidden names

		var tryFindPlayerByName = (await mediator.Send(new GetPlayerQuery(plainName)))
			.Where(x => x.Object.DBRef != target.Object().DBRef);

		if (tryFindPlayerByName.Any(x => x.Object.Name.Equals(plainName, StringComparison.InvariantCultureIgnoreCase)))
		{
			return false;
		}

		return true;
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