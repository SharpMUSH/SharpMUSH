using System.Collections.Immutable;
using DotNext;
using Mediator;
using NaturalSort.Extension;
using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using AsyncEnumerable = System.Linq.AsyncEnumerable;

namespace SharpMUSH.Library.Services;

public class AttributeService(
	IMediator mediator,
	IPermissionService ps,
	ILocateService locateService,
	IValidateService validateService, 
	INotifyService notifyService)
	: IAttributeService
{
	private readonly NaturalSortComparer _attributeSort = new NaturalSortComparer(StringComparison.CurrentCulture);
	
	public async ValueTask<OptionalSharpAttributeOrError> GetAttributeAsync(
		AnySharpObject executor,
		AnySharpObject obj,
		string attribute,
		IAttributeService.AttributeMode mode,
		bool checkParent = true)
	{
		var curObj = obj;
		var attributePath = attribute.Split('`');

		if (!await validateService.Valid(IValidateService.ValidationType.AttributeName, MModule.single(attribute), obj))
		{
			return new Error<string>(Errors.ErrorObjectAttributeString);
		}
		
		Func<AnySharpObject, AnySharpObject, SharpAttribute[], ValueTask<bool>> permissionPredicate = mode switch
		{
			IAttributeService.AttributeMode.Read => ps.CanViewAttribute,
			IAttributeService.AttributeMode.Execute => ps.CanExecuteAttribute,
			IAttributeService.AttributeMode.Set => ps.CanExecuteAttribute,
			IAttributeService.AttributeMode.SystemSet => (_, _, _) => ValueTask.FromResult(true),
			_ => throw new InvalidOperationException(nameof(IAttributeService.AttributeMode))
		};
		var permissionFailureType = mode switch
		{
			IAttributeService.AttributeMode.Read => Errors.ErrorAttrPermissions,
			IAttributeService.AttributeMode.Execute => Errors.ErrorAttrEvalPermissions,
			IAttributeService.AttributeMode.Set => Errors.ErrorAttrSetPermissions,
			IAttributeService.AttributeMode.SystemSet => string.Empty,
			_ => throw new InvalidOperationException(nameof(IAttributeService.AttributeMode))
		};

		// Walk through parent hierarchy first: object -> parent -> grandparent -> etc.
		// Then check zone hierarchy for each level
		var currentObj = obj;
		
		// First pass: check object and all parents
		while (true)
		{
			var attr = mediator.CreateStream(new GetAttributeQuery(currentObj.Object().DBRef, attributePath));
			var attrArr = await attr.ToArrayAsync();

			if (attrArr.Length == attributePath.Length)
			{
				return await permissionPredicate(executor, currentObj, attrArr)
					? attrArr
					: new Error<string>(permissionFailureType);
			}

			// Move to parent if available and checkParent is enabled
			if (!checkParent)
			{
				break;
			}

			var parent = await currentObj.Object().Parent.WithCancellation(CancellationToken.None);
			if (parent.IsNone)
			{
				break;
			}

			currentObj = parent.Known;
		}
		
		// Second pass: check zones for object and all parents
		if (checkParent)
		{
			currentObj = obj;
			
			while (true)
			{
				var zone = await currentObj.Object().Zone.WithCancellation(CancellationToken.None);
				
				// Check this level's zone chain
				while (!zone.IsNone)
				{
					var zoneAttr = mediator.CreateStream(new GetAttributeQuery(zone.Known.Object().DBRef, attributePath));
					var zoneAttrArr = await zoneAttr.ToArrayAsync();
					
					if (zoneAttrArr.Length == attributePath.Length)
					{
						return await permissionPredicate(executor, zone.Known, zoneAttrArr)
							? zoneAttrArr
							: new Error<string>(permissionFailureType);
					}
					
					// Walk up the zone chain
					zone = await zone.Known.Object().Zone.WithCancellation(CancellationToken.None);
				}
				
				// Move to parent to check parent's zones
				var parent = await currentObj.Object().Parent.WithCancellation(CancellationToken.None);
				if (parent.IsNone)
				{
					break;
				}
				
				currentObj = parent.Known;
			}
		}
		
		return new None();
	}

	public async ValueTask<OptionalLazySharpAttributeOrError> LazilyGetAttributeAsync(AnySharpObject executor,
		AnySharpObject obj, string attribute,
		IAttributeService.AttributeMode mode, bool checkParent = true)
	{
		if (!await validateService.Valid(IValidateService.ValidationType.AttributeName, MModule.single(attribute), obj))
		{
			return new Error<string>(Errors.ErrorObjectAttributeString);
		}

		var curObj = obj;
		var attributePath = attribute.Split('`');

		Func<AnySharpObject, AnySharpObject, LazySharpAttribute[], ValueTask<bool>> permissionPredicate = mode switch
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

		// Walk through parent hierarchy first: object -> parent -> grandparent -> etc.
		// Then check zone hierarchy for each level
		var currentObj = obj;
		
		// First pass: check object and all parents
		while (true)
		{
			// Try to find attribute on current object
			var attr = mediator.CreateStream(new GetLazyAttributeQuery(currentObj.Object().DBRef, attributePath));
			var attrArr = await attr.ToArrayAsync(CancellationToken.None);
			
			if (attrArr.Length == attributePath.Length)
			{
				return await permissionPredicate(executor, currentObj, attrArr)
					? attrArr
					: new Error<string>(permissionFailureType);
			}

			// Move to parent if available and checkParent is enabled
			if (!checkParent)
			{
				break;
			}

			var parent = await currentObj.Object().Parent.WithCancellation(CancellationToken.None);
			if (parent.IsNone)
			{
				break;
			}

			currentObj = parent.Known;
		}
		
		// Second pass: check zones for object and all parents
		if (checkParent)
		{
			currentObj = obj;
			
			while (true)
			{
				var zone = await currentObj.Object().Zone.WithCancellation(CancellationToken.None);
				
				// Check this level's zone chain
				while (!zone.IsNone)
				{
					var zoneAttr = mediator.CreateStream(new GetLazyAttributeQuery(zone.Known.Object().DBRef, attributePath));
					var zoneAttrArr = await zoneAttr.ToArrayAsync(CancellationToken.None);
					
					if (zoneAttrArr.Length == attributePath.Length)
					{
						return await permissionPredicate(executor, zone.Known, zoneAttrArr)
							? zoneAttrArr
							: new Error<string>(permissionFailureType);
					}
					
					// Walk up the zone chain
					zone = await zone.Known.Object().Zone.WithCancellation(CancellationToken.None);
				}
				
				// Move to parent to check parent's zones
				var parent = await currentObj.Object().Parent.WithCancellation(CancellationToken.None);
				if (parent.IsNone)
				{
					break;
				}
				
				currentObj = parent.Known;
			}
		}
		
		return new None();
	}

	public async ValueTask<MString> EvaluateAttributeFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject obj,
		string attribute, Dictionary<string, CallState> args, bool evalParent = true, bool ignorePermissions = false)
	{
		if (!await validateService.Valid(IValidateService.ValidationType.AttributeName, MModule.single(attribute), obj))
		{
			return MModule.single(Errors.ErrorObjectAttributeString);
		}
		
		var realExecutor = executor;

		if (ignorePermissions)
		{
			var maybeOne = await mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
			realExecutor = maybeOne.Known;
		}

		var attr = await GetAttributeAsync(realExecutor, obj, attribute, IAttributeService.AttributeMode.Execute,
			evalParent);
		if (attr.IsError)
		{
			return MModule.single(attr.AsError.Value);
		}

		if (attr.IsNone)
		{
			return MModule.single(Errors.ErrorNoSuchAttribute);
		}

		var result = await parser.With(s =>
				s with
				{
					Arguments = args,
					EnvironmentRegisters = args,
					CurrentEvaluation = new DBAttribute(obj.Object().DBRef, attr.AsAttribute.Last().LongName!),
					// Preserve the invocation tracking fields to maintain recursion and invocation tracking
					FunctionCallStack = s.FunctionCallStack,
					FunctionRecursionDepths = s.FunctionRecursionDepths,
					TotalInvocations = s.TotalInvocations
				},
			async newParser =>
				await newParser.FunctionParse(attr.AsAttribute.Last().Value));

		return result!.Message!;
	}

	public async ValueTask<SharpAttributesOrError> GetVisibleAttributesAsync(AnySharpObject executor, AnySharpObject obj,
		int depth = 1)
	{
		var actualObject = obj.Object();
		var attributes = actualObject.Attributes.Value;

		return depth <= 1
			? await attributes
				.Where(async (x, _) => await ps.CanViewAttribute(executor, obj, x))
				.ToArrayAsync(CancellationToken.None)
			: (await GetVisibleAttributesAsync(attributes, executor, obj, depth))
			.ToArray();
	}

	public async ValueTask<LazySharpAttributesOrError> LazilyGetVisibleAttributesAsync(AnySharpObject executor,
		AnySharpObject obj, int depth = 1)
	{
		await ValueTask.CompletedTask;
		var actualObject = obj.Object();
		var attributes = actualObject.LazyAttributes.Value;

		return depth <= 1
			? LazySharpAttributesOrError.FromAsync(attributes.Where(async (x, _) =>
				await ps.CanViewAttribute(executor, obj, x)))
			: LazySharpAttributesOrError.FromAsync(GetVisibleLazyAttributesAsync(attributes, executor, obj, depth));
	}

	public async ValueTask<MString> EvaluateAttributeFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor,
		MString objAndAttribute,
		Dictionary<string, CallState> args, bool evalParent = true, bool ignorePermissions = false,
		bool ignoreLambda = false)
	{
		var split = MModule.split("/", objAndAttribute);
		var obj = split.First();
		var attribute = MModule.multiple(split.Skip(1))!;
		var applyPredicate = obj.ToPlainText().StartsWith("#APPLY", StringComparison.InvariantCultureIgnoreCase);
		var lambdaPredicate = obj.ToPlainText().StartsWith("#LAMBDA", StringComparison.InvariantCultureIgnoreCase);

		if (!await validateService.Valid(IValidateService.ValidationType.AttributeName, attribute, new None()))
		{
			return MModule.single(Errors.ErrorObjectAttributeString);
		}
		
		var realExecutor = executor;
		
		if (ignorePermissions)
		{
			var maybeOne = await mediator.Send(new GetObjectNodeQuery(new DBRef(1)));
			realExecutor = maybeOne.Known;
		}
		
		// #apply evaluations. 
		if (applyPredicate && !ignoreLambda)
		{
			var argN = 1;
			if (!string.IsNullOrWhiteSpace(attribute.ToPlainText()) && !int.TryParse(attribute.ToPlainText(), out argN))
			{
				// Invalid argument to #apply 
				return MModule.single(string.Format(Errors.ErrorBadArgumentFormat, "#APPLY"));
			}

			var slimArgs = Enumerable
				.Range(0, argN)
				.ToDictionary(argK => argK.ToString(), argK => args[argK.ToString()]);

			if (parser.FunctionLibrary.TryGetValue(obj.ToPlainText().Remove(0, 6).ToLower(), out var applyFunction))
			{
				// Check function permission flags
				var functionFlags = applyFunction.LibraryInformation.Attribute.Flags;
				
				// Check wizard/admin/god restrictions
				if (functionFlags.HasFlag(FunctionFlags.GodOnly) && !await realExecutor.IsRoyalty())
				{
					return MModule.single(Errors.ErrorAttrEvalPermissions);
				}
				if (functionFlags.HasFlag(FunctionFlags.AdminOnly) && !await realExecutor.IsRoyalty())
				{
					return MModule.single(Errors.ErrorAttrEvalPermissions);
				}
				if (functionFlags.HasFlag(FunctionFlags.WizardOnly) && !await realExecutor.IsWizard())
				{
					return MModule.single(Errors.ErrorAttrEvalPermissions);
				}
				if (functionFlags.HasFlag(FunctionFlags.NoGuest) && await realExecutor.IsGuest())
				{
					return MModule.single(Errors.ErrorAttrEvalPermissions);
				}
				
				// Check custom restrictions
				if (applyFunction.LibraryInformation.Attribute.Restrict.Length > 0)
				{
					var hasRestriction = false;
					foreach (var restriction in applyFunction.LibraryInformation.Attribute.Restrict)
					{
						if (await realExecutor.HasPower(restriction))
						{
							hasRestriction = true;
							break;
						}
					}
					if (!hasRestriction)
					{
						return MModule.single(Errors.ErrorAttrEvalPermissions);
					}
				}
				
				var result = await parser.With(
					s => s with { 
						Arguments = slimArgs, 
						EnvironmentRegisters = slimArgs,
						// Preserve the invocation tracking fields to maintain recursion and invocation tracking
						FunctionCallStack = s.FunctionCallStack,
						FunctionRecursionDepths = s.FunctionRecursionDepths,
						TotalInvocations = s.TotalInvocations
					},
					async np => await applyFunction.LibraryInformation.Function.Invoke(np)
				);

				return result.Message!;
			}

			// Check if proper function name in the attribute section.
			// Check if enough arguments are being passed to the function based on the number after #apply.
			// This is where we really need a proper attribute library access layer, similar to commands.

			// CallFunction must be Exposed by IMUSHCodeParser.
			// Further work is needed before this can be implemented properly.
		}

		// LAMBDA.
		if (lambdaPredicate && !ignoreLambda)
		{
			var result = await parser.With(s => s with { 
				Arguments = args,
				// Preserve the invocation tracking fields to maintain recursion and invocation tracking
				FunctionCallStack = s.FunctionCallStack,
				FunctionRecursionDepths = s.FunctionRecursionDepths,
				TotalInvocations = s.TotalInvocations
			},
				async np => await np.FunctionParse(attribute));
			return result!.Message!;
		}

		// Standard Object/Attribute evaluation
		var maybeObject =
			await locateService.LocateAndNotifyIfInvalidWithCallState(parser, executor, executor, obj.ToPlainText(),
				LocateFlags.All);

		return maybeObject switch
		{
			{ IsError: true } => maybeObject.AsError.Message!,
			_ => await EvaluateAttributeFunctionAsync(parser, executor, maybeObject.AsSharpObject, attribute.ToPlainText(),
				args, evalParent, ignorePermissions)
		};
	}

	private async ValueTask<ImmutableList<SharpAttribute>> GetVisibleAttributesAsync(
		IAsyncEnumerable<SharpAttribute> attributes, AnySharpObject executor, AnySharpObject obj, int depth = 1)
	{
		if (depth == 0) return [];

		var visibleList = (await attributes.Where((x, _) => ps.CanViewAttribute(executor, obj, x))
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

	private async IAsyncEnumerable<LazySharpAttribute> GetVisibleLazyAttributesAsync(
		IAsyncEnumerable<LazySharpAttribute> attributes, AnySharpObject executor, AnySharpObject obj, int currentDepth = 1)
	{
		var attrs = attributes;
		var stagingAttrs = new List<IAsyncEnumerable<LazySharpAttribute>>();

		const int maxDepth = 0;

		while (currentDepth > maxDepth)
		{
			var visibleAttributes = attrs
				.Where(async (x, _)
					=> await ps.CanViewAttribute(executor, obj, x));

			// Multiple Iteration that may be able to be optimized away.
			await foreach (var attr in visibleAttributes)
			{
				yield return attr;
				stagingAttrs.AddRange(await attr.Leaves.WithCancellation(CancellationToken.None));
			}

			attrs = visibleAttributes
				.Select<LazySharpAttribute, IAsyncEnumerable<LazySharpAttribute>>(async (x, _) =>
					await x.Leaves.WithCancellation(CancellationToken.None))
				.SelectMany(x => x);

			currentDepth++;
		}
	}

	/// <summary>
	/// Get attributes matching a pattern. Supports exact match, wildcard, and regex modes.
	/// </summary>
	/// <param name="executor">The object requesting the attributes</param>
	/// <param name="obj">The object whose attributes to retrieve</param>
	/// <param name="attributePattern">The pattern to match (exact name, wildcard pattern, or regex)</param>
	/// <param name="checkParents">Whether to check parent objects</param>
	/// <param name="mode">Pattern matching mode: Exact, Wildcard, or Regex</param>
	/// <returns>Array of matching attributes or error</returns>
	public async ValueTask<SharpAttributesOrError> GetAttributePatternAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attributePattern,
		bool checkParents,
		IAttributeService.AttributePatternMode mode)
	{
		// Create stream of attributes matching the pattern
		var attributes = mediator.CreateStream(
			new GetAttributesQuery(obj.Object().DBRef, attributePattern.ToUpper(), checkParents, mode));

		// Filter based on permissions and return sorted results
		// Permission check is done per-attribute for fine-grained access control
		return await attributes
			.Where(async (x, _) => await ps.CanViewAttribute(executor, obj, x))
			.OrderBy(x => x.LongName, _attributeSort)
			.ToArrayAsync();
	}

	/// <summary>
	/// Lazily get attributes matching a pattern. More efficient for large result sets.
	/// </summary>
	/// <param name="executor">The object requesting the attributes</param>
	/// <param name="obj">The object whose attributes to retrieve</param>
	/// <param name="attributePattern">The pattern to match</param>
	/// <param name="checkParents">Whether to check parent objects</param>
	/// <param name="mode">Pattern matching mode</param>
	/// <returns>Lazy enumerable of matching attributes</returns>
	public LazySharpAttributesOrError LazilyGetAttributePatternAsync(AnySharpObject executor,
		AnySharpObject obj, string attributePattern,
		bool checkParents, IAttributeService.AttributePatternMode mode = IAttributeService.AttributePatternMode.Exact)
	{
		// Create lazy stream of attributes
		var attributes = mediator.CreateStream(
			new GetLazyAttributesQuery(obj.Object().DBRef, attributePattern.ToUpper(), checkParents, mode));

		// Return lazy-evaluated, permission-filtered, sorted results
		return LazySharpAttributesOrError
			.FromAsync(attributes
				.OrderBy(x => x.LongName, _attributeSort)
				.Where(async (x, _) => await ps.CanViewAttribute(executor, obj, x)));
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
			return new Error<string>(Errors.ErrorObjectAttributeString);
		}

		var allFlags = mediator.CreateStream(new GetAttributeFlagsQuery());
		var returnedFlag = await allFlags
			.FirstOrDefaultAsync(x => x.Name == flag || x.Symbol == flag);

		if (returnedFlag is null)
		{
			return new Error<string>("Flag Found");
		}

		// Check if the flag is already set to avoid redundant operations
		var currentFlags = returnedAttribute.AsAttribute.Last().Flags;
		if (currentFlags.Contains(returnedFlag))
		{
			await notifyService.Notify(executor,
				$"Flag {returnedFlag.Name} is already set on attribute {returnedAttribute.AsAttribute.Last().LongName}", obj);
			return new Success();
		}

		await mediator.Send(new SetAttributeFlagCommand(obj.Object().DBRef, returnedAttribute.AsAttribute.Last(),
			returnedFlag));

		await notifyService.Notify(executor,
			$"Flag {returnedFlag.Name} set on attribute {returnedAttribute.AsAttribute.Last().LongName}", obj);

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
			return new Error<string>(Errors.ErrorObjectAttributeString);
		}

		var allFlags = mediator.CreateStream(new GetAttributeFlagsQuery());
		var returnedFlag = await allFlags.FirstOrDefaultAsync(x => x.Name == flag || x.Symbol == flag);

		if (returnedFlag is null)
		{
			return new Error<string>("Flag Found");
		}

		// Check if the flag is actually set before unsetting
		var currentFlags = returnedAttribute.AsAttribute.Last().Flags;
		if (!currentFlags.Contains(returnedFlag))
		{
			await notifyService.Notify(executor,
				$"Flag {returnedFlag.Name} is not set on attribute {returnedAttribute.AsAttribute.Last().LongName}", obj);
			return new Success();
		}

		await mediator.Send(new UnsetAttributeFlagCommand(obj.Object().DBRef, returnedAttribute.AsAttribute.Last(),
			returnedFlag));

		await notifyService.Notify(executor,
			$"Flag {returnedFlag.Name} unset from attribute {returnedAttribute.AsAttribute.Last().LongName}", obj);

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
		var attr = mediator.CreateStream(new GetAttributeQuery(obj.Object().DBRef, attrPath));

		// Check both attribute permissions AND object permissions
		// Attribute permissions: executor must be able to set each attribute in the path
		// Object permissions: executor must control the object
		var permission = await attr.AllAsync(async (x, _) => await ps.CanSet(executor, obj, x));

		if (!permission)
		{
			return new Error<string>(Errors.ErrorAttrSetPermissions);
		}

		await mediator.Send(new SetAttributeCommand(obj.Object().DBRef, attrPath, value,
			await executor.Object().Owner.WithCancellation(CancellationToken.None)));

		await notifyService.Notify(executor,
			$"Attribute {string.Join("`", attrPath)} SET.", obj);

		return new Success();
	}

	/// <summary>
	/// Sets the value of an attribute to string.Empty
	/// </summary>
	/// <param name="executor"></param>
	/// <param name="obj"></param>
	/// <param name="attributePattern"></param>
	/// <param name="patternMode"></param>
	/// <param name="clearMode"></param>
	/// <returns></returns>
	public async ValueTask<OneOf<Success, Error<string>>> ClearAttributeAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attributePattern,
		IAttributeService.AttributePatternMode patternMode,
		IAttributeService.AttributeClearMode clearMode)
	{
		await ValueTask.CompletedTask;

		if (!await ps.Controls(executor, obj))
		{
			return new Error<string>(Errors.ErrorAttrSetPermissions);
		}

		var attr = mediator.CreateStream(new GetAttributesQuery(obj.Object().DBRef, attributePattern, false, patternMode));
		
		var attrArr = await attr.ToArrayAsync();
		
		if (attrArr.IsNullOrEmpty() 
		    || !await attrArr.ToAsyncEnumerable().AllAsync(async (x, _) => await ps.CanSet(executor, obj, x)))
		{
			return new Error<string>(Errors.ErrorAttrSetPermissions);
		}

		await mediator.Send(new ClearAttributeCommand(obj.Object().DBRef, attrArr.Select(x => x.LongName!).ToArray()));

		foreach (var attrDone in attrArr)
		{
			await notifyService.Notify(executor,
				$"Attribute {attrDone.LongName} CLEARED.", obj);
		}

		return new Success();
	}
}