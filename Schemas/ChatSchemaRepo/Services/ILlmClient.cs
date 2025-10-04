namespace ChatSchemaRepo;

/// <summary>
/// Interface for LLM service providers (Bedrock, OpenAI, etc.)
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Generate a response based on conversation history
    /// </summary>
    Task<string> GenerateResponseAsync(List<ChatMessage> conversationHistory);

    /// <summary>
    /// Generate a response for a single user message
    /// </summary>
    Task<string> GenerateResponseAsync(string userMessage);
}
