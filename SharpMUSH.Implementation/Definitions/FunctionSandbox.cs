using System.Collections.Frozen;
using System.Collections.Immutable;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Definitions;

/// <summary>
/// The function lists <c>sandbox()</c> puts around an evaluation. Each <c>sandbox()</c> adds a layer that lasts
/// while its expression is evaluated, <c>u()</c> and every other evaluation it reaches included; a call is
/// refused when any layer refuses it, so a sandbox inside another can only take more functions away.
/// </summary>
internal static class FunctionSandbox
{
	/// <summary>One <c>sandbox()</c>'s list.</summary>
	/// <param name="Names">The functions listed, in any case.</param>
	/// <param name="AllowList">True when <see cref="Names"/> are the only functions allowed, false when they are the ones refused.</param>
	/// <param name="AsUnknown">True when a refused function answers as though it did not exist, false when it answers permission denied.</param>
	/// <param name="CoversUserFunctions">True when the list also applies to <c>@function</c> functions.</param>
	internal sealed record Layer(FrozenSet<string> Names, bool AllowList, bool AsUnknown, bool CoversUserFunctions)
	{
		public bool Refuses(string name, string registeredName, bool userFunction)
			=> (!userFunction || CoversUserFunctions)
				&& (Names.Contains(name) || Names.Contains(registeredName)) != AllowList;
	}

	private static readonly AsyncLocal<ImmutableStack<Layer>?> Ambient = new();

	/// <summary>Adds <paramref name="layer"/> until the returned scope is disposed.</summary>
	public static IDisposable Enter(Layer layer)
	{
		var previous = Ambient.Value;
		Ambient.Value = (previous ?? ImmutableStack<Layer>.Empty).Push(layer);
		return new Scope(previous);
	}

	/// <summary>
	/// What a call to <paramref name="name"/> answers in place of running, or null when no layer refuses it.
	/// <paramref name="registeredName"/> is the name the function is registered under, which an alias resolves to.
	/// </summary>
	public static string? Refusal(string name, string registeredName, bool userFunction)
	{
		if (Ambient.Value is not { } layers) return null;
		foreach (var layer in layers)
		{
			if (!layer.Refuses(name, registeredName, userFunction)) continue;
			return layer.AsUnknown
				? string.Format(ErrorMessages.Returns.NoSuchFunction, name.ToUpperInvariant())
				: PermissionRefusal.CannotUse(name.ToUpperInvariant()).ReturnText;
		}

		return null;
	}

	private sealed class Scope(ImmutableStack<Layer>? previous) : IDisposable
	{
		public void Dispose() => Ambient.Value = previous;
	}
}
