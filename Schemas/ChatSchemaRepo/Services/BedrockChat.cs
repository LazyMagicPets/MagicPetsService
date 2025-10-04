using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text.Json;

namespace ChatSchemaRepo;

public class BedrockChat : ILlmClient
{
    private readonly IAmazonBedrockRuntime _bedrockClient;
    private readonly ILogger<BedrockChat> _logger;

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

            // Prepare the conversation context from all messages in history
            var messages = new List<object>();

            if (conversationHistory?.Any() == true)
            {
                foreach (var msg in conversationHistory)
                {
                    messages.Add(new
                    {
                        role = msg.Role.ToString().ToLowerInvariant(),
                        content = msg.Content
                    });
                }
            }

            // Ensure we have at least one message
            if (!messages.Any())
            {
                _logger.LogWarning("No messages in conversation history");
                return "I'm sorry, but I don't see any messages to respond to.";
            }

            // Prepare the request payload for Claude
            var requestPayload = new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 4000,
                messages = messages,
                temperature = 0.7,
                top_p = 0.9
            };

            var jsonPayload = JsonSerializer.Serialize(requestPayload);

            var request = new InvokeModelRequest
            {
                ModelId = "anthropic.claude-3-sonnet-20240229-v1:0", // Claude 3 Sonnet
                Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonPayload)),
                ContentType = "application/json"
            };

            var response = await _bedrockClient.InvokeModelAsync(request);

            var responseBody = System.Text.Encoding.UTF8.GetString(response.Body.ToArray());
            var responseJson = JsonSerializer.Deserialize<JsonElement>(responseBody);

            if (responseJson.TryGetProperty("content", out var contentArray) &&
                contentArray.GetArrayLength() > 0)
            {
                var firstContent = contentArray[0];
                if (firstContent.TryGetProperty("text", out var textElement))
                {
                    var assistantResponse = textElement.GetString();
                    _logger.LogInformation("Successfully generated response from Bedrock");
                    return assistantResponse ?? "I apologize, but I couldn't generate a response at this time.";
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
}
