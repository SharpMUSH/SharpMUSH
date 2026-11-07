using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

public partial class Functions
{
	/// <summary>
	/// parents(): an object's parent, its parent's parent and so on, without the object itself; objids when the
	/// second argument is a nonzero number. The chain stops at an object you cannot examine, unless you control
	/// the object you asked about. <c>#-1</c> when there is none or the object is not found.
	/// </summary>
	[SharpFunction(Name = "parents", MinArgs = 1, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "objids"])]
	public async ValueTask<CallState> Parents(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var args = parser.CurrentState.ArgumentsOrdered;
		var objids = args.Count > 1 && RhostInteger(args["1"].Message.ToPlainText()) != 0;
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		if (await LocateService.Locate(parser, executor, executor, args["0"].Message.ToPlainText(), LocateFlags.All)
				is not AnySharpObject found)
		{
			return new CallState("#-1");
		}

		var followAll = await PermissionService.Controls(executor, found);
		var parents = new List<string>();
		var current = found;
		while ((followAll || await PermissionService.CanExamine(executor, current))
			&& await current.Object().Parent.WithCancellation(CancellationToken.None) is AnySharpObject parent
			&& parents.Count < MaxParentChain)
		{
			parents.Add(objids ? parent.Object().DBRef.ToString() : $"#{parent.Object().DBRef.Number}");
			current = parent;
		}

		return new CallState(parents.Count == 0 ? "#-1" : string.Join(' ', parents));
	}

	/// <summary>How far parents() follows a chain, as a guard against a loop.</summary>
	private const int MaxParentChain = 100;
}
