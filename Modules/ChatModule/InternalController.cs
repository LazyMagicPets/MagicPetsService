using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace ChatModule;

/// <summary>
/// Internal controller for ChatModule infrastructure endpoints.
/// These endpoints are not part of the public API and are used for internal operations.
/// </summary>
[ApiController]
[Route("ChatModule/internal")]
public class InternalController : ControllerBase
{
    private readonly ChatManagerService _chatManagerService;
    private readonly ILogger<InternalController> _logger;

    public InternalController(
        ChatManagerService chatManagerService,
        ILogger<InternalController> logger)
    {
        _chatManagerService = chatManagerService;
        _logger = logger;
    }

    // Keep-alive feature is currently disabled in ChatManagerService
    // Uncomment this endpoint if the feature is re-enabled in the future

    // /// <summary>
    // /// Keep-alive endpoint that holds an HTTP request open for the lifetime of a chat.
    // /// This represents active background processing as concurrent requests to App Runner,
    // /// preventing premature scale-down of instances that are actively processing messages.
    // /// </summary>
    // /// <param name="chatId">The ID of the chat to keep alive</param>
    // /// <param name="cancellationToken">Cancellation token for the request</param>
    // /// <returns>200 OK when chat closes normally, 404 if chat not found, 499 if cancelled</returns>
    // [HttpPost("keepalive/{chatId}")]
    // public async Task<IActionResult> KeepAlive(string chatId, CancellationToken cancellationToken)
    // {
    //     var semaphore = _chatManagerService.GetKeepAliveSemaphore(chatId);
    //
    //     if (semaphore == null)
    //     {
    //         _logger.LogWarning("Keep-alive request for non-existent chat {ChatId}", chatId);
    //         return NotFound(new { error = "Chat not found" });
    //     }
    //
    //     _logger.LogDebug("Keep-alive request blocking for chat {ChatId}", chatId);
    //
    //     try
    //     {
    //         // Block here until the chat closes (semaphore is released) or request is cancelled
    //         await semaphore.WaitAsync(cancellationToken);
    //
    //         _logger.LogDebug("Keep-alive request completed normally for chat {ChatId}", chatId);
    //         return Ok(new { message = "Chat closed normally", chatId });
    //     }
    //     catch (OperationCanceledException)
    //     {
    //         _logger.LogDebug("Keep-alive request cancelled for chat {ChatId}", chatId);
    //         // 499 is a non-standard status code meaning "Client Closed Request"
    //         return StatusCode(499, new { message = "Request cancelled", chatId });
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.LogError(ex, "Keep-alive request failed for chat {ChatId}", chatId);
    //         return StatusCode(500, new { error = "Internal server error", chatId });
    //     }
    // }
}
