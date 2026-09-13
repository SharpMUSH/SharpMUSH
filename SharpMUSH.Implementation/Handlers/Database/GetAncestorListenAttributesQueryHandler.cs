using Mediator;
using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Configuration.Options;
using SharpMUSH.Library.Queries.Database;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Utilities;

namespace SharpMUSH.Implementation.Handlers.Database;

/// <summary>Reads an inherited ancestor phase without caching an aggregate of mutable parent links.</summary>
public class GetAncestorListenAttributesQueryHandler : IQueryHandler<GetAncestorListenAttributesQuery, ListenAttributeCache[]>
{
	private readonly IMediator _mediator;
	private readonly IOptionsWrapper<SharpMUSHOptions>? _configuration;
	public GetAncestorListenAttributesQueryHandler(IMediator mediator) => _mediator = mediator;
	[ActivatorUtilitiesConstructor]
	public GetAncestorListenAttributesQueryHandler(IMediator mediator, IOptionsWrapper<SharpMUSHOptions> configuration)
		: this(mediator) => _configuration = configuration;
	public ValueTask<ListenAttributeCache[]> Handle(GetAncestorListenAttributesQuery request, CancellationToken cancellationToken)
		=> new ListenAttributeSearch().ReadPhaseAsync(_mediator, request.Ancestor,
			_configuration?.CurrentValue.Limit.MaxParents ?? 10, true, cancellationToken);
}
