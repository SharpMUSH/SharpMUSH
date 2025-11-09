using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Handlers.Database;

public class GetAttributeQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetAttributeQuery, SharpAttribute>
{
	public IAsyncEnumerable<SharpAttribute> Handle(GetAttributeQuery request,
		CancellationToken cancellationToken) =>
		database.GetAttributeAsync(request.DBRef, request.Attribute.Select(x => x.ToUpper()).ToArray(), cancellationToken)
			.AsTask().GetAwaiter().GetResult()
		?? AsyncEnumerable.Empty<SharpAttribute>();
}

public class GetLazyAttributeQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetLazyAttributeQuery, LazySharpAttribute>
{
	public IAsyncEnumerable<LazySharpAttribute> Handle(GetLazyAttributeQuery request,
		CancellationToken cancellationToken)
		=> database.GetLazyAttributeAsync(request.DBRef, request.Attribute.Select(x => x.ToUpper()).ToArray(), cancellationToken)
		   ?? AsyncEnumerable.Empty<LazySharpAttribute>();
}

public class GetAttributesQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetAttributesQuery, SharpAttribute>
{
	public IAsyncEnumerable<SharpAttribute> Handle(GetAttributesQuery request,
		CancellationToken cancellationToken)
		=> request.Mode switch
		{
			IAttributeService.AttributePatternMode.Exact =>
				database.GetAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken)
					.AsTask().GetAwaiter().GetResult()
				?? AsyncEnumerable.Empty<SharpAttribute>(),
			IAttributeService.AttributePatternMode.Regex =>
				database.GetAttributesByRegexAsync(
						request.DBRef,
						request.Pattern.ToUpper(), cancellationToken)
					.AsTask().GetAwaiter().GetResult()
				?? AsyncEnumerable.Empty<SharpAttribute>(),
			_ => database.GetAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken)
				     .AsTask().GetAwaiter().GetResult()
			     ?? AsyncEnumerable.Empty<SharpAttribute>()
		};
}

public class GetLazyAttributesQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetLazyAttributesQuery, LazySharpAttribute>
{
	public IAsyncEnumerable<LazySharpAttribute> Handle(GetLazyAttributesQuery request,
		CancellationToken cancellationToken)
		=> request.Mode switch
		{
			IAttributeService.AttributePatternMode.Exact =>
				database.GetLazyAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken)
					.AsTask().GetAwaiter().GetResult()
				?? AsyncEnumerable.Empty<LazySharpAttribute>(),
			IAttributeService.AttributePatternMode.Regex =>
				database.GetLazyAttributesByRegexAsync(
						request.DBRef,
						request.Pattern.ToUpper(), cancellationToken)
					.AsTask().GetAwaiter().GetResult()
				?? AsyncEnumerable.Empty<LazySharpAttribute>(),
			_ =>
				database.GetLazyAttributesAsync(request.DBRef, request.Pattern.ToUpper(), cancellationToken)
					.AsTask().GetAwaiter().GetResult()
				?? AsyncEnumerable.Empty<LazySharpAttribute>()
		};
}

public class GetAllAttributeEntriesQueryHandler(ISharpDatabase database)
	: IStreamQueryHandler<GetAllAttributeEntriesQuery, SharpAttributeEntry>
{
	public IAsyncEnumerable<SharpAttributeEntry> Handle(GetAllAttributeEntriesQuery request,
		CancellationToken cancellationToken) =>
		database.GetAllAttributeEntriesAsync(cancellationToken);
}