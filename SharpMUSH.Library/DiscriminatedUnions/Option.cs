using OneOf;
using OneOf.Types;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public partial class Option<T> : OneOfBase<T, None>
{
		public bool IsSome() => IsT0;
		public bool IsNone() => IsT1;
		public T AsValue() => AsT0;

		public static Option<T> FromOption(T some) => new(some);

		public bool TryGetValue(out T? value)
		{
				value = (IsT0 ? AsT0 : default);
				return IsT0;
		}
}