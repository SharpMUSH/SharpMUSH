using Mediator;
using SharpMUSH.Library.Commands.Database.Wiki;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models.Wiki;
using SharpMUSH.Library;

namespace SharpMUSH.Implementation.Handlers.Database.Wiki;

public sealed class CreateWikiPageCommandHandler(IWikiStore store) : ICommandHandler<CreateWikiPageCommand, Result<WikiPage>>
{
	public ValueTask<Result<WikiPage>> Handle(CreateWikiPageCommand command, CancellationToken cancellationToken)
		=> new(store.CreatePageAsync(command.Page, command.EditSummary));
}

public sealed class UpdateWikiPageBodyCommandHandler(IWikiStore store) : ICommandHandler<UpdateWikiPageBodyCommand, Found<WikiPage>>
{
	public ValueTask<Found<WikiPage>> Handle(UpdateWikiPageBodyCommand command, CancellationToken cancellationToken)
		=> new(store.UpdatePageBodyAsync(command.Id, command.Body, command.EditorDbref, command.EditSummary, command.At));
}

public sealed class DeleteWikiPageCommandHandler(IWikiStore store) : ICommandHandler<DeleteWikiPageCommand, Found<None>>
{
	public ValueTask<Found<None>> Handle(DeleteWikiPageCommand command, CancellationToken cancellationToken)
		=> new(store.DeletePageAsync(command.Id));
}

public sealed class SetWikiRequirementsCommandHandler(IWikiStore store) : ICommandHandler<SetWikiRequirementsCommand, Found<None>>
{
	public ValueTask<Found<None>> Handle(SetWikiRequirementsCommand command, CancellationToken cancellationToken)
		=> new(store.SetRequirementsAsync(command.Set));
}

public sealed class SetWikiPageTitleCommandHandler(IWikiStore store) : ICommandHandler<SetWikiPageTitleCommand, Found<WikiPage>>
{
	public ValueTask<Found<WikiPage>> Handle(SetWikiPageTitleCommand command, CancellationToken cancellationToken)
		=> new(store.SetPageTitleAsync(command.Id, command.Title));
}

public sealed class SetWikiPageMetadataCommandHandler(IWikiStore store) : ICommandHandler<SetWikiPageMetadataCommand, Found<WikiPage>>
{
	public ValueTask<Found<WikiPage>> Handle(SetWikiPageMetadataCommand command, CancellationToken cancellationToken)
		=> new(store.SetPageMetadataAsync(command.Id, command.Categories, command.Published));
}

public sealed class SetWikiCategoryPinnedCommandHandler(IWikiStore store) : ICommandHandler<SetWikiCategoryPinnedCommand, bool>
{
	public ValueTask<bool> Handle(SetWikiCategoryPinnedCommand command, CancellationToken cancellationToken)
		=> new(store.SetCategoryPinnedAsync(command.Category, command.Pinned));
}

public sealed class WriteWikiTranslationCommandHandler(IWikiStore store) : ICommandHandler<WriteWikiTranslationCommand, TranslationWriteResult>
{
	public ValueTask<TranslationWriteResult> Handle(WriteWikiTranslationCommand command, CancellationToken cancellationToken)
		=> new(store.WriteTranslationAsync(command.PageId, command.Locale, command.Title, command.Body, command.EditorDbref, command.EditSummary, command.Published, command.ExpectedRevisionNumber, command.At));
}

public sealed class DeleteWikiTranslationCommandHandler(IWikiStore store) : ICommandHandler<DeleteWikiTranslationCommand, Found<None>>
{
	public ValueTask<Found<None>> Handle(DeleteWikiTranslationCommand command, CancellationToken cancellationToken)
		=> new(store.DeleteTranslationAsync(command.PageId, command.Locale));
}
