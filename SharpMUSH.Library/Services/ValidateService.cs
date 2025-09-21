using System.ComponentModel;
using System.Text.RegularExpressions;
using Mediator;
using Microsoft.Extensions.Options;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public partial class ValidateService(IMediator mediator, IOptionsMonitor<PennMUSHOptions> configuration)
	: IValidateService
{
	public async ValueTask<bool> Valid(IValidateService.ValidationType type, MString value,
		OneOf.OneOf<AnySharpObject, SharpAttributeEntry>? target = null)
	{
		return type switch
		{
			IValidateService.ValidationType.Name
				=> ValidateName(value),
			IValidateService.ValidationType.PlayerName when target is { IsT0: true }
				=> await ValidatePlayerName(value, target.Value.AsT0),
			IValidateService.ValidationType.PlayerAlias when target is { IsT0: true }
				=> ValidatePlayerAlias(value, target.Value.AsT0),
			IValidateService.ValidationType.AttributeName
				=> ValidAttributeName(value),
			IValidateService.ValidationType.AttributeValue when target is { IsT1: true }
				=> ValidateAttributeValue(value, target.Value.AsT1),
			IValidateService.ValidationType.ColorName
				=> true,
			IValidateService.ValidationType.AnsiCode
				=> true,
			IValidateService.ValidationType.CommandName
				=> true,
			IValidateService.ValidationType.LockKey
				=> true,
			IValidateService.ValidationType.LockType
				=> true,
			IValidateService.ValidationType.FlagName
				=> true,
			IValidateService.ValidationType.PowerName
				=> true,
			IValidateService.ValidationType.ChannelName
				=> true,
			IValidateService.ValidationType.Password
				=> true,
			IValidateService.ValidationType.QRegisterName
				=> true,
			IValidateService.ValidationType.Timezone
				=> TimeZoneInfo.TryFindSystemTimeZoneById(value.ToPlainText(), out _),
			IValidateService.ValidationType.FunctionName
				=> true,
			_
				=> throw new InvalidEnumArgumentException(type.ToString())
		};
	}

	private bool ValidateAttributeValue(MString value, SharpAttributeEntry attribute)
	{
		throw new NotImplementedException();
	}

	[GeneratedRegex("^[ !\"#%&()&+,\\-./0-9A-Z:;<>=?@`_]$")]
	private static partial Regex ValidAttributeNameRegex();

	private bool ValidAttributeName(MString value)
		=> ValidAttributeNameRegex().IsMatch(value.ToPlainText());

	private bool ValidatePlayerAlias(MString value, AnySharpObject target)
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

	private bool ValidateName(MString value)
	{
		/*
  if (!name || !*name)
    return 0;

  /* No leading spaces * /
  if (isspace(*name))
    return 0;

  /* only printable characters * /
  for (p = name; p && *p; p++) {
    if (!char_isprint(*p))
      return 0;
    if (ONLY_ASCII_NAMES && *p > 127)
      return 0;
    if (strchr("[]%\\=&|", *p))
      return 0;
  }

  /* No trailing spaces * /
  p--;
  if (isspace(*p))
    return 0;

  /* Not too long * /
  if (strlen(name) >= OBJECT_NAME_LIMIT)
    return 0;

  /* No magic cookies * /
  return (name && *name && *name != LOOKUP_TOKEN && *name != NUMBER_TOKEN &&
          *name != NOT_TOKEN && (is_exit || strcasecmp(name, "me")) &&
          strcasecmp(name, "home") && strcasecmp(name, "here"));
*/

		throw new NotImplementedException();
	}
}