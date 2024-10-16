﻿using OneOf.Types;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Definitions;
using OneOf;

namespace SharpMUSH.Library.Services;

public class AttributeService(ISharpDatabase db, IPermissionService ps) : IAttributeService
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
		var attributePath = attribute.Split("`");

		Func<AnySharpObject, AnySharpObject, SharpAttribute[], bool> permissionPredicate = mode switch
		{
			IAttributeService.AttributeMode.Read => ps.CanViewAttribute,
			IAttributeService.AttributeMode.Execute => ps.CanExecuteAttribute,
			_ => throw new InvalidOperationException(nameof(IAttributeService.AttributeMode))
		};

		while (curObj is not null)
		{
			var attr = (await db.GetAttributeAsync(obj.Object().DBRef, attributePath))?.ToArray();

			if (attr?.Length == attributePath.Length)
			{
				return permissionPredicate(executor, obj, attr)
					? attr.Last()
					: new Error<string>(Errors.ErrorAttrPermissions);
			}

			if (!checkParent)
			{
				return new None();
			}

			curObj = curObj.Parent();
		}

		return new None();
	}

	public ValueTask<SharpAttributesOrError> GetVisibleAttributesAsync(AnySharpObject executor, AnySharpObject obj)
	{
		var actualObject = obj.Object();
		var attributes = actualObject.Attributes();

		return ValueTask.FromResult((SharpAttributesOrError)attributes.Where(x => ps.CanViewAttribute(executor, obj, x)).ToArray());
	}

	public async ValueTask<SharpAttributesOrError> GetAttributePatternAsync(AnySharpObject executor, AnySharpObject obj, string attributePattern, IAttributeService.AttributePatternMode mode)
	{
		// TODO: Implement Pattern Modes
		// TODO: GetAttributesAsync should return the full Path, not the final attribute.
		// TODO: CanViewAttribute needs to be able to Memoize during a list check, as it's likely to be called multiple times.
		var attributes = mode switch
		{
			IAttributeService.AttributePatternMode.Exact => await db.GetAttributesAsync(obj.Object().DBRef, attributePattern),
			IAttributeService.AttributePatternMode.Wildcard => await db.GetAttributesAsync(obj.Object().DBRef, attributePattern),
			IAttributeService.AttributePatternMode.Regex => await db.GetAttributesAsync(obj.Object().DBRef, attributePattern),
			_ => throw new InvalidOperationException(nameof(IAttributeService.AttributePatternMode))
		};

		if (attributes is null)
		{
			return Enumerable.Empty<SharpAttribute>().ToArray();
		}

		return attributes.Where(x => ps.CanViewAttribute(executor, obj, x)).ToArray();
	}

	public async ValueTask<OneOf<Success,Error<string>>> SetAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, MString value)
	{
		if (!ps.Controls(executor, obj))
		{
			return new Error<string>(Errors.ErrorAttrSetPermissions);
		}

		var attrPath = attribute.Split("`");
		var attr = await db.GetAttributeAsync(obj.Object().DBRef, attrPath);

		// TODO: Fix, object permissions also needed.
		var permission = attr?.All(x => ps.CanSet(executor, obj, x)) ?? true;
		
		if (!permission)
		{
			return new Error<string>(Errors.ErrorAttrSetPermissions);
		}

		await db.SetAttributeAsync(obj.Object().DBRef, attrPath, value.ToString(), executor.Object().Owner());
		
		return new Success();
	}

	public ValueTask<SharpAttributesOrError> ClearAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, IAttributeService.AttributeClearMode mode)
	{
		throw new NotImplementedException();
	}
}