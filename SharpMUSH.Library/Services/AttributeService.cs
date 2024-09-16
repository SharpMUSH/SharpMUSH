using OneOf.Types;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Definitions;

namespace SharpMUSH.Library.Services;

public class AttributeService(ISharpDatabase db, IPermissionService ps) : IAttributeService
{

	public async ValueTask<OneOf<SharpAttribute, None, Error<string>>> GetAttributeAsync(AnySharpObject executor, AnySharpObject obj, string attribute, IAttributeService.AttributeMode mode, bool checkParent = true)
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

		while (true)
		{
			var attr = (await db.GetAttributeAsync(obj.Object().DBRef, attributePath))?.ToArray();

			if (attr?.Length == attributePath.Length)
			{

				if (permissionPredicate(executor, obj, attr))
				{
					return attr.Last();
				}
				else
				{
					return new Error<string>(Errors.ErrorAttrPermissions);
				}
			}

			if (!checkParent)
			{
				return new None();
			}

			var objParent = curObj.Parent();
			if (objParent == null)
			{
				return new None();
			}
			else
			{
				curObj = objParent;
			}
		}
	}

	public ValueTask<OneOf<SharpAttribute[], Error<string>>> GetVisibleAttributesAsync(AnySharpObject executor, AnySharpObject obj)
	{
		throw new NotImplementedException();
	}

	public ValueTask<OneOf<SharpAttribute[], Error<string>>> GetAttributePatternAsync(AnySharpObject executor, AnySharpObject obj, string attributePattern)
	{
		throw new NotImplementedException();
	}
}