using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using System.Text.Json;
using ChatSchema;

namespace ChatModule;

public class BedrockChat
{
    private readonly IAmazonBedrockRuntime _bedrockClient;
    private readonly ILogger<BedrockChat> _logger;

    public BedrockChat(IAmazonBedrockRuntime bedrockClient, ILogger<BedrockChat> logger)
    {
        _bedrockClient = bedrockClient;
        _logger = logger;
    }

    public async Task<string> GenerateResponseAsync(string userMessage, List<ChatMessage>? conversationHistory = null)
    {
        try
        {
            _logger.LogInformation("Generating response for user message");

            // Prepare the conversation context
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

            // Add the current user message
            messages.Add(new
            {
                role = "user",
                content = userMessage
            });

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

    public async Task<string> ProcessStreamingResponseAsync(string userMessage, List<ChatMessage>? conversationHistory = null,
        Func<string, Task>? onPartialResponse = null)
    {
        try
        {
            _logger.LogInformation("Processing streaming response for user message");

            // Prepare the conversation context
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

            // Add the current user message
            messages.Add(new
            {
                role = "user",
                content = userMessage
            });

            // Prepare the request payload for Claude with streaming
            var requestPayload = new
            {
                anthropic_version = "bedrock-2023-05-31",
                max_tokens = 4000,
                messages = messages,
                temperature = 0.7,
                top_p = 0.9
            };

            var jsonPayload = JsonSerializer.Serialize(requestPayload);

            var request = new InvokeModelWithResponseStreamRequest
            {
                ModelId = "anthropic.claude-3-sonnet-20240229-v1:0",
                Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(jsonPayload)),
                ContentType = "application/json"
            };

            var response = await _bedrockClient.InvokeModelWithResponseStreamAsync(request);
            var fullResponse = "";

            await foreach (var @event in response.Body)
            {
                if (@event is PayloadPart payloadPart)
                {
                    var chunk = System.Text.Encoding.UTF8.GetString(payloadPart.Bytes.ToArray());
                    var chunkJson = JsonSerializer.Deserialize<JsonElement>(chunk);

                    if (chunkJson.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("text", out var textElement))
                    {
                        var text = textElement.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            fullResponse += text;
                            if (onPartialResponse != null)
                            {
                                await onPartialResponse(text);
                            }
                        }
                    }
                }
            }

            _logger.LogInformation("Successfully processed streaming response from Bedrock");
            return fullResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process streaming response from Bedrock");
            return "I apologize, but I encountered an error while processing your request. Please try again later.";
        }
    }
}