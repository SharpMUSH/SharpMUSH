using System.Collections.Immutable;
using Mediator;
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
	ICommandDiscoveryService cs,
	ILocateService locateService,
	INotifyService notifyService)
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

		var curObj = obj;
		var attributePath = attribute.Split('`');

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

		// TODO: This code doesn't quite look right. It does not correctly walk the parent chain.
		while (true)
		{
			var attr = await mediator.Send(new GetAttributeQuery(obj.Object().DBRef, attributePath));

			if (attr is null)
			{
				return new None();
			}

			var attrArr = await attr.ToArrayAsync();

			if (attrArr.Length == attributePath.Length)
			{
				return await permissionPredicate(executor, obj, attrArr)
					? attrArr
					: new Error<string>(permissionFailureType);
			}

			if (!checkParent)
			{
				return new None();
			}

			var parent = await curObj.Object().Parent.WithCancellation(CancellationToken.None);
			if (parent.IsNone)
			{
				return new None();
			}

			curObj = parent.Known;
		}

		// TODO: Currently this only returns the last piece. We should return the full path.
	}

	public async ValueTask<OptionalLazySharpAttributeOrError> LazilyGetAttributeAsync(AnySharpObject executor,
		AnySharpObject obj, string attribute,
		IAttributeService.AttributeMode mode, bool checkParent = true)
	{
		// TODO: Check if that is a valid attribute format.

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

		// TODO: This code doesn't quite look right. It does not correctly walk the parent chain.
		while (true)
		{
			var attr = await mediator.Send(new GetLazyAttributeQuery(obj.Object().DBRef, attributePath));

			if (attr is null)
			{
				return new None();
			}

			var attrArr = await attr.ToArrayAsync(CancellationToken.None);

			if (attrArr.Length == attributePath.Length)
			{
				return await permissionPredicate(executor, obj, attrArr)
					? attrArr
					: new Error<string>(permissionFailureType);
			}

			if (!checkParent)
			{
				return new None();
			}

			var parent = await curObj.Object().Parent.WithCancellation(CancellationToken.None);
			if (parent.IsNone)
			{
				return new None();
			}

			curObj = parent.Known;
		}

		// TODO: Currently this only returns the last piece. We should return the full path.
	}

	public async ValueTask<MString> EvaluateAttributeFunctionAsync(IMUSHCodeParser parser, AnySharpObject executor,
		AnySharpObject obj,
		string attribute, Dictionary<string, CallState> args, bool evalParent = true, bool ignorePermissions = false)
	{
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
					CurrentEvaluation = new DBAttribute(obj.Object().DBRef, attr.AsAttribute.Last().LongName!),
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

			// TODO: This is skipping function permission checks.
			if (parser.FunctionLibrary.TryGetValue(obj.ToPlainText().Remove(0, 6).ToLower(), out var applyFunction))
			{
				var result = await parser.With(
					s => s with { Arguments = slimArgs, EnvironmentRegisters = slimArgs },
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
			var result = await parser.With(s => s with { Arguments = args },
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

	public async ValueTask<SharpAttributesOrError> GetAttributePatternAsync(AnySharpObject executor,
		AnySharpObject obj,
		string attributePattern,
		bool checkParents,
		IAttributeService.AttributePatternMode mode)
	{
		// TODO: Implement Pattern Modes
		// TODO: GetAttributesAsync should return the full Path, not the final attribute.
		// TODO: CanViewAttribute needs to be able to Memoize during a list check, as it's likely to be called multiple times.
		var attributes = await mediator.Send(
			new GetAttributesQuery(obj.Object().DBRef, attributePattern, checkParents, mode));

		return attributes is null
			? Enumerable.Empty<SharpAttribute>().ToArray()
			: await AsyncEnumerable.ToArrayAsync(attributes
				.Where(async (x, _) => await ps.CanViewAttribute(executor, obj, x)));
	}

	public async ValueTask<LazySharpAttributesOrError> LazilyGetAttributePatternAsync(AnySharpObject executor,
		AnySharpObject obj, string attributePattern,
		bool checkParents, IAttributeService.AttributePatternMode mode = IAttributeService.AttributePatternMode.Exact)
	{
		// TODO: Implement Pattern Modes
		// TODO: GetAttributesAsync should return the full Path, not the final attribute.
		// TODO: CanViewAttribute needs to be able to Memoize during a list check, as it's likely to be called multiple times.
		var attributes = await mediator.Send(
			new GetLazyAttributesQuery(obj.Object().DBRef, attributePattern, checkParents, mode));

		return attributes is null
			? LazySharpAttributesOrError.FromAsync(Enumerable.Empty<LazySharpAttribute>().ToArray().ToAsyncEnumerable())
			: LazySharpAttributesOrError.FromAsync(attributes
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
			// TODO: Do this better
			return new Error<string>("Not Found");
		}

		var allFlags = await mediator.Send(new GetAttributeFlagsQuery());
		var returnedFlag = await allFlags
			.FirstOrDefaultAsync(x => x.Name == flag || x.Symbol == flag);

		if (returnedFlag is null)
		{
			return new Error<string>("Flag Found");
		}

		// TODO: What if it's already set?
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
			// TODO: Do this better
			return new Error<string>("Not Found");
		}

		var allFlags = await mediator.Send(new GetAttributeFlagsQuery());
		var returnedFlag = await allFlags.FirstOrDefaultAsync(x => x.Name == flag || x.Symbol == flag);

		if (returnedFlag is null)
		{
			return new Error<string>("Flag Found");
		}

		// TODO: What if it's not already set?
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
		var attr = await mediator.Send(new GetAttributeQuery(obj.Object().DBRef, attrPath));

		// TODO: Fix, object permissions also needed.
		var permission = attr is null ||
		                 await attr.AllAsync(async (x, _) => await ps.CanSet(executor, obj, x));

		if (!permission)
		{
			return new Error<string>(Errors.ErrorAttrSetPermissions);
		}

		cs.InvalidateCache(obj.Object().DBRef);
		await mediator.Send(new SetAttributeCommand(obj.Object().DBRef, attrPath, value,
			await executor.Object().Owner.WithCancellation(CancellationToken.None)));

		await notifyService.Notify(executor,
			$"Attribute {attrPath} SET.", obj);

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
	/// <exception cref="NotImplementedException"></exception>
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

		var attr = await mediator.Send(new GetAttributesQuery(obj.Object().DBRef, attributePattern, false, patternMode));

		if (attr is null)
		{
			return new Error<string>(Errors.ErrorAttrSetPermissions);
		}

		var attrArr = await AsyncEnumerable.ToArrayAsync(attr);

		if (!await attrArr.ToAsyncEnumerable().AllAsync(async (x, _) => await ps.CanSet(executor, obj, x)))
		{
			return new Error<string>(Errors.ErrorAttrSetPermissions);
		}

		cs.InvalidateCache(obj.Object().DBRef);

		await mediator.Send(new ClearAttributeCommand(obj.Object().DBRef, attrArr.Select(x => x.LongName!).ToArray()));

		foreach (var attrDone in attrArr)
		{
			await notifyService.Notify(executor,
				$"Attribute {attrDone.LongName} CLEARED.", obj);
		}

		return new Success();
	}
}