using System.Collections.Frozen;
using System.Collections.Immutable;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.ParserInterfaces;

/// <summary>
/// The function lists <c>sandbox()</c> puts around an evaluation. Each <c>sandbox()</c> adds a layer that lasts
/// while its expression is evaluated, <c>u()</c> and every other evaluation it reaches included; a call is
/// refused when any layer refuses it, so a sandbox inside another can only take more functions away.
/// </summary>
public static class FunctionSandbox
{
	/// <summary>One <c>sandbox()</c>'s list.</summary>
	/// <param name="Names">The functions listed, in any case.</param>
	/// <param name="AllowList">True when <see cref="Names"/> are the only functions allowed, false when they are the ones refused.</param>
	/// <param name="AsUnknown">True when a refused function answers as though it did not exist, false when it answers permission denied.</param>
	/// <param name="CoversUserFunctions">True when the list also applies to <c>@function</c> functions.</param>
	public sealed record Layer(FrozenSet<string> Names, bool AllowList, bool AsUnknown, bool CoversUserFunctions)
	{
		/// <summary>Whether the layer refuses a call known by any of <paramref name="names"/>.</summary>
		public bool Refuses(IEnumerable<string> names, bool userFunction)
			=> (!userFunction || CoversUserFunctions) && names.Any(Names.Contains) != AllowList;
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
	/// <paramref name="otherNames"/> are the other names the call goes by: the function a dialect name stands for,
	/// and the name the function is registered under, which an alias resolves to. A layer that lists any of them
	/// refuses the call.
	/// </summary>
	public static string? Refusal(string name, bool userFunction, params ReadOnlySpan<string> otherNames)
		=> Ambient.Value is null ? null : Refusal(name, userFunction, otherNames.ToArray());

	/// <inheritdoc cref="Refusal(string, bool, ReadOnlySpan{string})"/>
	public static string? Refusal(string name, bool userFunction, params string[] otherNames)
	{
		if (Ambient.Value is not { } layers) return null;
		foreach (var layer in layers)
		{
			if (!layer.Refuses(otherNames.Prepend(name), userFunction)) continue;
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
