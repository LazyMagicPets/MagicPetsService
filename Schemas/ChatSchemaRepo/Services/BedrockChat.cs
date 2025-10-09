using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Runtime.CompilerServices;

namespace ChatSchemaRepo;

public class BedrockChat : ILlmClient
{
    private readonly IAmazonBedrockRuntime _bedrockClient;
    private readonly ILogger<BedrockChat> _logger;
    // Use inference profile for Nova models - this is required for on-demand throughput
    private const string ModelId = "us.amazon.nova-pro-v1:0";

    public BedrockChat(IAmazonBedrockRuntime bedrockClient, ILogger<BedrockChat> logger)
    {
        _bedrockClient = bedrockClient;
        _logger = logger;
    }

    public async Task<string> GenerateResponseAsync(List<ChatMessage> conversationHistory)
    {
        try
        {
            _logger.LogInformation("Generating response for conversation with {MessageCount} messages", conversationHistory?.Count ?? 0);

            if (conversationHistory?.Any() != true)
            {
                _logger.LogWarning("No messages in conversation history");
                return "I'm sorry, but I don't see any messages to respond to.";
            }

            // Convert ChatMessage history to Bedrock Converse API format
            var messages = new List<Message>();
            foreach (var msg in conversationHistory)
            {
                messages.Add(new Message
                {
                    Role = msg.Role == ChatMessageRole.User ? ConversationRole.User : ConversationRole.Assistant,
                    Content = new List<ContentBlock>
                    {
                        new ContentBlock { Text = msg.Content }
                    }
                });
            }

            var request = new ConverseRequest
            {
                ModelId = ModelId,
                Messages = messages,
                InferenceConfig = new InferenceConfiguration
                {
                    MaxTokens = 4000,
                    Temperature = 0.7f,
                    TopP = 0.9f
                }
            };

            var response = await _bedrockClient.ConverseAsync(request);

            // Extract text from response
            if (response?.Output?.Message?.Content?.Count > 0)
            {
                var contentBlock = response.Output.Message.Content[0];
                if (!string.IsNullOrEmpty(contentBlock.Text))
                {
                    _logger.LogInformation("Successfully generated response from Bedrock");
                    return contentBlock.Text;
                }
            }

            _logger.LogWarning("Unexpected response format from Bedrock");
            return "I apologize, but I received an unexpected response format. Please try again.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate response from Bedrock");
            return "I apologize, but I encountered an error while processing your request. Please try again later.";
        }
    }

    public async Task<string> GenerateResponseAsync(string userMessage)
    {
        var messageId = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var history = new List<ChatMessage>
        {
            new ChatMessage
            {
                MessageId = messageId,
                Role = ChatMessageRole.User,
                Content = userMessage,
                Timestamp = now
            }
        };

        return await GenerateResponseAsync(history);
    }

    public async IAsyncEnumerable<string> GenerateResponseStreamAsync(List<ChatMessage> conversationHistory, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating streaming response for conversation with {MessageCount} messages", conversationHistory?.Count ?? 0);

        if (conversationHistory?.Any() != true)
        {
            _logger.LogWarning("No messages in conversation history for streaming");
            yield return "I'm sorry, but I don't see any messages to respond to.";
            yield break;
        }

        // Convert ChatMessage history to Bedrock Converse API format
        var messages = new List<Message>();
        foreach (var msg in conversationHistory)
        {
            messages.Add(new Message
            {
                Role = msg.Role == ChatMessageRole.User ? ConversationRole.User : ConversationRole.Assistant,
                Content = new List<ContentBlock>
                {
                    new ContentBlock { Text = msg.Content }
                }
            });
        }

        var request = new ConverseStreamRequest
        {
            ModelId = ModelId,
            Messages = messages,
            InferenceConfig = new InferenceConfiguration
            {
                MaxTokens = 4000,
                Temperature = 0.7f,
                TopP = 0.9f
            }
        };

        ConverseStreamResponse? response = null;

        try
        {
            response = await _bedrockClient.ConverseStreamAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Streaming response was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate streaming response from Bedrock");
            // Can't yield in catch, so we'll return after this block
            response?.Dispose();
            yield break;
        }

        // Stream the response chunks
        await foreach (var streamEvent in response.Stream.WithCancellation(cancellationToken))
        {
            switch (streamEvent)
            {
                case ContentBlockDeltaEvent deltaEvent:
                    var textDelta = deltaEvent.Delta?.Text;
                    if (!string.IsNullOrEmpty(textDelta))
                    {
                        yield return textDelta;
                    }
                    break;

                case ContentBlockStopEvent:
                    _logger.LogDebug("Content block completed");
                    break;

                case MessageStopEvent stopEvent:
                    _logger.LogInformation("Streaming response completed. Stop reason: {StopReason}", stopEvent.StopReason);
                    response?.Dispose();
                    yield break;

                case ConverseStreamMetadataEvent metadataEvent:
                    _logger.LogDebug("Metadata: Usage - InputTokens: {InputTokens}, OutputTokens: {OutputTokens}",
                        metadataEvent.Usage?.InputTokens,
                        metadataEvent.Usage?.OutputTokens);
                    break;
            }
        }

        response?.Dispose();
    }
}
