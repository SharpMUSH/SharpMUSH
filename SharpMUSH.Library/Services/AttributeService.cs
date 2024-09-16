using OneOf.Types;
using OneOf;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Definitions;

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

		while (curObj != null)
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

	public ValueTask<OneOf<SharpAttribute[], Error<string>>> GetVisibleAttributesAsync(AnySharpObject executor, AnySharpObject obj)
	{
		throw new NotImplementedException();
	}

	public ValueTask<OneOf<SharpAttribute[], Error<string>>> GetAttributePatternAsync(AnySharpObject executor, AnySharpObject obj, string attributePattern)
	{
		throw new NotImplementedException();
	}
}