using SharpMUSH.Library;
using SharpMUSH.Library.Attributes;
using SharpMUSH.Library.Authorization;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.DatabaseConversion.Rhost;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Functions;

/// <summary>
/// RhostMUSH's depower functions. A depower is a deny override here, on the permissions its import lands on
/// (<see cref="RhostNameTables.Depowers"/>), so an object holds a depower when its own or its account's overrides deny
/// every one of them.
/// </summary>
public partial class Functions
{
	/// <summary>
	/// <c>hasdepower(&lt;object&gt;, &lt;depower&gt;)</c>: 1 when the object holds the RhostMUSH depower, else 0.
	/// <c>#-1 NO SUCH DEPOWER</c> for a name RhostMUSH does not have.
	/// </summary>
	[SharpFunction(Name = "hasdepower", MinArgs = 2, MaxArgs = 2, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object", "depower"])]
	public async ValueTask<CallState> HasDepower(IMUSHCodeParser parser, SharpFunctionAttribute _2)
	{
		var name = parser.CurrentState.ArgumentList[1].Message.ToPlainText().Trim();
		if (RhostNameTables.Depowers.FirstOrDefault(depower => depower.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
				is not { } depower)
		{
			return new CallState(ErrorMessages.Returns.NoSuchDepower);
		}

		return await WithStoredGrants(parser, grants => new CallState(Holds(grants, depower)));
	}

	/// <summary><c>ldepowers(&lt;object&gt;)</c>: the RhostMUSH depowers the object holds, by name, in RhostMUSH's order.</summary>
	[SharpFunction(Name = "ldepowers", MinArgs = 1, MaxArgs = 1, Flags = FunctionFlags.Regular | FunctionFlags.StripAnsi,
		ParameterNames = ["object"])]
	public async ValueTask<CallState> ListDepowers(IMUSHCodeParser parser, SharpFunctionAttribute _2)
		=> await WithStoredGrants(parser, grants => new CallState(string.Join(' ',
			RhostNameTables.Depowers.Where(depower => Holds(grants, depower)).Select(depower => depower.Name))));

	/// <summary>
	/// Whether the object's own or its account's overrides deny every permission <paramref name="depower"/> lands on.
	/// A depower SharpMUSH does not import lands on none, and is never held.
	/// </summary>
	private static bool Holds(ObjectGrants grants, RhostPowerMapping depower)
		=> depower.Scopes.Length > 0 && depower.Scopes.All(scope =>
			Denies(grants.Context.ObjectOverrides, scope) || Denies(grants.Context.Overrides, scope));

	private static bool Denies(IReadOnlyDictionary<string, PermissionState> overrides, string scope)
		=> overrides.Any(pair => pair.Value == PermissionState.Deny && pair.Key.Equals(scope, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// Locates the first argument and answers from its grants as stored: a depower is what was done to the object, so a
	/// privilege ceiling or an app acting as it does not change the answer.
	/// </summary>
	private async ValueTask<CallState> WithStoredGrants(IMUSHCodeParser parser, Func<ObjectGrants, CallState> answer)
	{
		var executor = await parser.CurrentState.KnownExecutorObject(Mediator);
		return await LocateService.LocateAndNotifyIfInvalidWithCallStateFunction(parser, executor, executor,
			parser.CurrentState.ArgumentList[0].Message.ToPlainText(), LocateFlags.All,
			async found => answer(await found.Object().Grants.WithCancellation(ExecutionBudget.CurrentToken)));
	}
}
