using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	[SharpFunction(Name = "localfun", MinArgs = 1, MaxArgs = 33, Flags = FunctionFlags.Regular,
		ParameterNames = ["name", "arguments..."])]
	public async ValueTask<CallState> LocalFunction(IMUSHCodeParser parser, SharpFunctionAttribute _)
	{
		ExecutionBudget.Current?.ThrowIfExceeded();
		var caller = await parser.CurrentState.KnownExecutorObject(Mediator);
		var owner = (await caller.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef;
		var name = parser.CurrentState.Arguments["0"].Message!.ToPlainText();
		var registry = parser.ServiceProvider.GetRequiredService<IUserDefinedFunctionService>();
		UserDefinedFunction? entry;
		try
		{
			var raw = registry.Get(name, owner);
			if (Get().IsSystemNameReserved(name) || (raw?.AliasOf is { } alias && Get().IsSystemNameReserved(alias)))
				return new CallState(string.Format(ErrorMessages.Returns.NoSuchFunction, name.ToUpperInvariant()));
			entry = registry.Resolve(name, owner);
		}
		catch (NotSupportedException)
		{
			return new CallState(string.Format(ErrorMessages.Returns.NoSuchFunction, name.ToUpperInvariant()));
		}
		if (entry is null) return new CallState(string.Format(ErrorMessages.Returns.NoSuchFunction, name.ToUpperInvariant()));
		if (await Mediator.Send(new GetObjectNodeQuery(entry.Object), ExecutionBudget.CurrentToken) is not AnySharpObject target
				|| (await target.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken)).Object.DBRef != owner)
		{
			registry.InvalidateLocalDefinitions(entry.Object);
			return new CallState(string.Format(ErrorMessages.Returns.NoSuchFunction, name.ToUpperInvariant()));
		}
		var argumentCount = parser.CurrentState.Arguments.Count - 1;
		if (argumentCount < entry.MinArgs) return new CallState(string.Format(ErrorMessages.Returns.TooFewArguments, name, entry.MinArgs, argumentCount));
		if (argumentCount > entry.MaxArgs) return new CallState(string.Format(ErrorMessages.Returns.TooManyArguments, name, entry.MaxArgs, argumentCount));
		var readable = await AttributeService.GetAttributeAsync(caller, target, entry.Attribute, IAttributeService.AttributeMode.Read, false);
		if (readable is not SharpAttribute[]) return readable.AsCallState;
		var arguments = Enumerable.Range(0, argumentCount).ToDictionary(i => i.ToString(), i => parser.CurrentState.Arguments[(i + 1).ToString()]);
		return await AttributeService.EvaluateAttributeFunctionResultAsync(parser, caller, target,
			entry.Attribute, arguments, evalParent: false);
	}
}
