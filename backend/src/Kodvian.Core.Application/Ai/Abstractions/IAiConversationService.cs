using Kodvian.Core.Application.Ai.Dtos;
using Kodvian.Core.Application.Common.Models;

namespace Kodvian.Core.Application.Ai.Abstractions;

public interface IAiConversationService
{
    Task<AiConversationDto> CreateAsync(AiExecutionContext actor, AiConversationCreateRequest request, CancellationToken cancellationToken);
    Task<PagedResultDto<AiConversationDto>> GetPagedAsync(AiExecutionContext actor, PagedRequestDto request, CancellationToken cancellationToken);
    Task<AiConversationDetailDto?> GetByIdAsync(AiExecutionContext actor, Guid conversationId, CancellationToken cancellationToken);
    Task<AiSendMessageResultDto> SendMessageAsync(AiExecutionContext actor, Guid conversationId, AiSendMessageRequest request, CancellationToken cancellationToken);
    Task<AiSendMessageResultDto> SendMessageAsync(AiExecutionContext actor, Guid conversationId, AiSendMessageRequest request, IAiRunObserver? observer, CancellationToken cancellationToken)
        => SendMessageAsync(actor, conversationId, request, cancellationToken);
    Task<AiActionResolutionDto> ConfirmActionAsync(AiExecutionContext actor, Guid conversationId, Guid actionId, CancellationToken cancellationToken);
    Task<AiActionBatchResolutionDto> ConfirmAllActionsAsync(AiExecutionContext actor, Guid conversationId, Guid messageId, CancellationToken cancellationToken);
    Task<AiActionResolutionDto> UndoActionAsync(AiExecutionContext actor, Guid conversationId, Guid actionId, CancellationToken cancellationToken);
    Task<AiActionResolutionDto> CancelActionAsync(AiExecutionContext actor, Guid conversationId, Guid actionId, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(AiExecutionContext actor, Guid conversationId, CancellationToken cancellationToken);
}
