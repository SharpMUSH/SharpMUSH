﻿using System.Collections.Immutable;
using Mediator;
using Microsoft.FSharp.Core;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Library.Services;

public class AttributeService(IMediator mediator, IPermissionService ps, ICommandDiscoveryService cs)
	: IAttributeService
{
	public async ValueTask<OptionalSharpAttributeOrError> GetAttributeAsync(
		AnySharpObject executor,
		AnySharpObject obj,
		string attribute,
		IAttributeService.AttributeMode mode,
		bool checkParent = true)
	{
		// TODO: Check if that is a valid attribute format.

		var curObj = obj.Object();
		var attributePath = attribute.Split('`');

		Func<AnySharpObject, AnySharpObject, SharpAttribute[], ValueTask<bool>> permissionPredicate = mode switch
		{
			IAttributeService.AttributeMode.Read => ps.CanViewAttribute,
			IAttributeService.AttributeMode.Execute => ps.CanExecuteAttribute,
			_ => throw new InvalidOperationException(nameof(IAttributeService.AttributeMode))
		};
		var permissionFailureType = mode switch
		{
			IAttributeService.AttributeMode.Read => Errors.ErrorAttrPermissions,
			IAttributeService.AttributeMode.Execute => Errors.ErrorAttrEvalPermissions,
			_ => throw new InvalidOperationException(nameof(IAttributeService.AttributeMode))
		};

		while (curObj is not null)
		{
			var attr = await mediator.Send(new GetAttributeQuery(obj.Object().DBRef, attributePath));
			var attrArr = attr?.ToArray();

			if (attrArr?.Length == attributePath.Length)
			{
				return await permissionPredicate(executor, obj, attrArr)
					? attrArr.Last()
					: new Error<string>(permissionFailureType);
			}

			if (!checkParent)
			{
				return new None();
			}

			curObj = await curObj.Parent.WithCancellation(CancellationToken.None);
		}

		return new None();
	}

	public async ValueTask<SharpAttributesOrError> GetVisibleAttributesAsync(AnySharpObject executor, AnySharpObject obj,
		int depth = 1)
	{
		var actualObject = obj.Object();
		var attributes = await actualObject.Attributes.WithCancellation(CancellationToken.None);

		return depth <= 1
			? await attributes.ToAsyncEnumerable().WhereAwait(async x => await ps.CanViewAttribute(executor, obj, x))
				.ToArrayAsync()
			: (await GetVisibleAttributesAsync(attributes, executor, obj, depth))
			.ToArray();
	}

	public async ValueTask<ImmutableList<SharpAttribute>> GetVisibleAttributesAsync(
		IEnumerable<SharpAttribute> attributes, AnySharpObject executor, AnySharpObject obj, int depth = 1)
	{
		if (depth == 0) return [];

		var visibleList = (await attributes.ToAsyncEnumerable().WhereAwait(x => ps.CanViewAttribute(executor, obj, x))
				.ToListAsync())
			.ToImmutableList();

		foreach (var attribute in visibleList)
		{
			var subAttributes =
				await GetVisibleAttributesAsync(await attribute.Leaves.WithCancellation(CancellationToken.None), executor, obj,
					depth - 1);
			visibleList = visibleList.AddRange(subAttributes);
		}

		return visibleList;
	}

	public async ValueTask<SharpAttributesOrError> GetAttributePatternAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attributePattern,
		IAttributeService.AttributePatternMode mode)
	{
		// TODO: Implement Pattern Modes
		// TODO: GetAttributesAsync should return the full Path, not the final attribute.
		// TODO: CanViewAttribute needs to be able to Memoize during a list check, as it's likely to be called multiple times.
		var attributes = mode switch
		{
			IAttributeService.AttributePatternMode.Exact => await mediator.Send(
				new GetAttributesQuery(obj.Object().DBRef, attributePattern)),
			IAttributeService.AttributePatternMode.Wildcard => await mediator.Send(
				new GetAttributesQuery(obj.Object().DBRef, attributePattern)),
			IAttributeService.AttributePatternMode.Regex => await mediator.Send(
				new GetAttributesQuery(obj.Object().DBRef, attributePattern)),
			_ => throw new InvalidOperationException(nameof(IAttributeService.AttributePatternMode))
		};

		return attributes is null
			? Enumerable.Empty<SharpAttribute>().ToArray()
			: await attributes.ToAsyncEnumerable().WhereAwait(async x => await ps.CanViewAttribute(executor, obj, x)).ToArrayAsync();
	}

	public async ValueTask<OneOf<Success, Error<string>>> SetAttributeFlagAsync(AnySharpObject executor,
		AnySharpObject obj, string attribute, string flag)
	{
		var returnedAttribute = await GetAttributeAsync(executor, obj, attribute, IAttributeService.AttributeMode.Execute);
		if (returnedAttribute.IsError)
		{
			return returnedAttribute.AsError;
		}

		if (returnedAttribute.IsNone)
		{
			// TODO: Do this better
			return new Error<string>("Not Found");
		}

		var returnedFlag =
			(await mediator.Send(new GetAttributeFlagsQuery())).Where(x => x.Name == flag || x.Symbol == flag);
		if (!returnedFlag.Any())
		{
			return new Error<string>("Flag Found");
		}

		// TODO: What if it's already set?
		await mediator.Send(new SetAttributeFlagCommand(returnedAttribute.AsAttribute, returnedFlag.First()));

		return new Success();
	}

	public async ValueTask<OneOf<Success, Error<string>>> UnsetAttributeFlagAsync(AnySharpObject executor,
		AnySharpObject obj, string attribute, string flag)
	{
		var returnedAttribute = await GetAttributeAsync(executor, obj, attribute, IAttributeService.AttributeMode.Execute);
		if (returnedAttribute.IsError)
		{
			return returnedAttribute.AsError;
		}

		if (returnedAttribute.IsNone)
		{
			// TODO: Do this better
			return new Error<string>("Not Found");
		}

		var returnedFlag =
			(await mediator.Send(new GetAttributeFlagsQuery())).Where(x => x.Name == flag || x.Symbol == flag);
		if (!returnedFlag.Any())
		{
			return new Error<string>("Flag Found");
		}

		// TODO: What if it's already set?
		await mediator.Send(new UnsetAttributeFlagCommand(returnedAttribute.AsAttribute, returnedFlag.First()));

		return new Success();
	}

	public async ValueTask<OneOf<Success, Error<string>>> SetAttributeAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attribute,
		MString value)
	{
		if (!await ps.Controls(executor, obj))
		{
			return new Error<string>(Errors.ErrorAttrSetPermissions);
		}

		var attrPath = attribute.Split('`');
		var attr = await mediator.Send(new GetAttributeQuery(obj.Object().DBRef, attrPath));

		// TODO: Fix, object permissions also neede  d.
		var permission = attr == null || await attr.ToAsyncEnumerable().AllAwaitAsync(async x => await ps.CanSet(executor, obj, x));

		if (!permission)
		{
			return new Error<string>(Errors.ErrorAttrSetPermissions);
		}

		cs.InvalidateCache(obj.Object().DBRef);
		await mediator.Send(new SetAttributeCommand(obj.Object().DBRef, attrPath, value,
			await executor.Object().Owner.WithCancellation(CancellationToken.None)));

		return new Success();
	}

	/// <summary>
	/// Sets the value of an attribute to string.Empty
	/// </summary>
	/// <param name="executor"></param>
	/// <param name="obj"></param>
	/// <param name="attributePattern"></param>
	/// <param name="mode"></param>
	/// <returns></returns>
	/// <exception cref="NotImplementedException"></exception>
	public async ValueTask<OneOf<Success, Error<string>>> ClearAttributeAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attributePattern,
		IAttributeService.AttributeClearMode mode)
	{
		await ValueTask.CompletedTask;

		if (!await ps.Controls(executor, obj))
		{
			return new Error<string>(Errors.ErrorAttrSetPermissions);
		}

		var attr = await mediator.Send(new GetAttributesQuery(obj.Object().DBRef, attributePattern));
		var attrArr = attr?.ToArray();

		var permission = attrArr == null || await attrArr.ToAsyncEnumerable().AllAwaitAsync(async x => await ps.CanSet(executor, obj, x));

		if (!permission)
		{
			return new Error<string>(Errors.ErrorAttrSetPermissions);
		}

		cs.InvalidateCache(obj.Object().DBRef);
		await mediator.Send(new ClearAttributeCommand(obj.Object().DBRef, attrArr!.Select(x => x.LongName!).ToArray()));

		return new Success();
	}
}