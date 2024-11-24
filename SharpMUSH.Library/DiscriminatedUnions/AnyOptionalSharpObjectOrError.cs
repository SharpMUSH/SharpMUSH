using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.DiscriminatedUnions;

[GenerateOneOf]
public class AnyOptionalSharpObjectOrError : OneOfBase<SharpPlayer, SharpRoom, SharpExit, SharpThing, OneOf.Types.None, OneOf.Types.Error<string>>
{
		public AnyOptionalSharpObjectOrError(OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing, OneOf.Types.None, OneOf.Types.Error<string>> input) : base(input) { }
		public static implicit operator AnyOptionalSharpObjectOrError(SharpPlayer x) => new(x);
		public static implicit operator AnyOptionalSharpObjectOrError(SharpRoom x) => new(x);
		public static implicit operator AnyOptionalSharpObjectOrError(SharpExit x) => new(x);
		public static implicit operator AnyOptionalSharpObjectOrError(SharpThing x) => new(x);
		public static implicit operator AnyOptionalSharpObjectOrError(OneOf.Types.None x) => new(x);
		public static implicit operator AnyOptionalSharpObjectOrError(OneOf.Types.Error<string> x) => new(x);

		public bool IsPlayer => IsT0;
		public bool IsRoom => IsT1;
		public bool IsExit => IsT2;
		public bool IsThing => IsT3;
		public bool IsNone => IsT4;
		public bool IsError => IsT5;

		public SharpPlayer AsPlayer => AsT0;
		public SharpRoom AsRoom => AsT1;
		public SharpExit AsExit => AsT2;
		public SharpThing AsThing => AsT3;
		public OneOf.Types.None AsNone => AsT4;
		public OneOf.Types.Error<string> AsError => AsT5;
}