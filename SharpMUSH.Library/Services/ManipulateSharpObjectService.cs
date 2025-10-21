using Mediator;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

public class ManipulateSharpObjectService(
	IMediator mediator,
	IPermissionService permissionService,
	IPasswordService passwordService,
	IValidateService validateService,
	INotifyService notifyService,
	IAttributeService attributeService)
	: IManipulateSharpObjectService
{
	public async ValueTask<CallState> SetName(AnySharpObject executor, AnySharpObject obj, MString name, bool notify)
	{
		if (!await permissionService.Controls(executor, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, "You do not have permission to rename that object.");
			}
			return Errors.ErrorPerm;
		}

		if (!await validateService.Valid(IValidateService.ValidationType.Name, name, obj))
		{
			if (notify)
			{
				await notifyService.Notify(executor, $"You cannot name that object {name}.");
			}
			return Errors.ErrorPerm;
		}

		switch (obj)
		{
			case { IsThing: true } or { IsRoom: true }:
				await mediator.Send(new SetNameCommand(obj, name));
				return obj.Object().DBRef;

			case { IsPlayer: true }:
				var tryFindPlayerByName = await (await mediator.Send(new GetPlayerQuery(name.ToPlainText()))).ToArrayAsync();
				if (tryFindPlayerByName.Any(x =>
					    x.Object.Name.Equals(name.ToPlainText(), StringComparison.InvariantCultureIgnoreCase)))
				{
					if (notify)
					{
						await notifyService.Notify(executor, "That player name is already in use.");
					}
					return "#-1 PLAYER NAME ALREADY IN USE.";
				}

				var playerSplit = MModule.split(";", name);

				await mediator.Send(new SetNameCommand(obj, playerSplit.First()));

				if (playerSplit.Length <= 1)
				{
					return obj.Object().DBRef;
				}

				var aliases = playerSplit.Skip(1).Select(x => x.ToPlainText()).ToArray();

				if (tryFindPlayerByName
				    .SelectMany(x => x.Aliases ?? [])
				    .Intersect(aliases, StringComparer.InvariantCultureIgnoreCase)
				    .Any())
				{
					if (notify)
					{
						await notifyService.Notify(executor, "That player alias is already in use.");
					}
					return "#-1 PLAYER ALIAS ALREADY IN USE.";
				}

				await attributeService.SetAttributeAsync(executor, obj, "ALIAS",
					MModule.multipleWithDelimiter(MModule.single(";"), aliases.Select(MModule.single)));

				return obj.Object().DBRef;

			default:
				var split = MModule.split(";", name);
				await mediator.Send(new SetNameCommand(obj, split.First()));
				if (split.Length > 1)
				{
					await attributeService.SetAttributeAsync(executor, obj, "ALIAS",
						MModule.multipleWithDelimiter(MModule.single(";"), split.Skip(1)));
				}

				return obj.Object().DBRef;
		}
	}

	public ValueTask<CallState> SetPassword(AnySharpObject executor, SharpPlayer player, string newPassword, bool notify)
	{
		var _ = passwordService;
		throw new NotImplementedException();
	}

	public ValueTask<CallState> SetFlag(AnySharpObject executor, AnySharpObject obj, string flagOrFlagAlias, bool notify)
	{
		throw new NotImplementedException();
	}

	public ValueTask<CallState> UnsetFlag(AnySharpObject executor, AnySharpObject obj, string flagOrFlagAlias, bool notify)
	{
		throw new NotImplementedException();
	}

	public ValueTask<CallState> SetPower(AnySharpObject executor, AnySharpObject obj, string powerOrPowerAlias, bool notify)
	{
		throw new NotImplementedException();
	}

	public ValueTask<CallState> UnsetPower(AnySharpObject executor, AnySharpObject obj, string powerOrPowerAlias, bool notify)
	{
		throw new NotImplementedException();
	}

	public ValueTask<CallState> SetOwner(AnySharpObject executor, AnySharpObject obj, SharpPlayer newOwner, bool notify)
	{
		throw new NotImplementedException();
	}

	public ValueTask<CallState> SetParent(AnySharpObject executor, AnySharpObject obj, AnySharpObject newParent, bool notify)
	{
		throw new NotImplementedException();
	}

	public ValueTask<CallState> UnsetParent(AnySharpObject executor, AnySharpObject obj, bool notify)
	{
		throw new NotImplementedException();
	}
}