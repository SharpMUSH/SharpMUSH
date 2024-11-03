﻿using OneOf;
using OneOf.Types;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public partial class Option<T> : OneOfBase<T, None>
{
	public bool IsSome() => IsT0;
	public bool IsNone() => IsT1;
	public T AsValue() => AsT0;

	public bool TryGetValue(out T? value)
	{
		value = (IsT0 ? AsT0 : default);
		return IsT0;
	}
}