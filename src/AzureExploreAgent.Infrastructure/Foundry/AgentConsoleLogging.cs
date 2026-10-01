using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AzureExploreAgent.Infrastructure.Foundry;

/// <summary>
/// Diagnostic middleware: logs what the agent receives, every tool call (name, arguments, result, duration) and
/// what it returns. Values are truncated so a large Azure CLI payload does not flood the console.
/// </summary>
public static class AgentConsoleLogging
{
    private const int MaxLoggedCharacters = 3_000;

    public static AIAgent WithConsoleLogging(this AIAgent agent, ILogger logger) =>
        agent
            .AsBuilder()
            .Use(
                runFunc: async (messages, session, options, innerAgent, cancellationToken) =>
                {
                    var input = messages.ToList();
                    logger.LogInformation("[agent] RUN input:\n{Input}", Describe(input));

                    try
                    {
                        var response = await innerAgent.RunAsync(input, session, options, cancellationToken);

                        logger.LogInformation(
                            "[agent] RUN output ({MessageCount} messages):\n{Output}",
                            response.Messages.Count,
                            Describe(response.Messages));

                        return response;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "[agent] RUN failed");
                        throw;
                    }
                },
                runStreamingFunc: StreamAsync(logger))
            .Use(async (innerAgent, context, next, cancellationToken) =>
            {
                var name = context.Function.Name;
                var started = System.Diagnostics.Stopwatch.StartNew();

                logger.LogInformation("[tool] CALL {Tool} args: {Arguments}", name, Serialize(context.Arguments));

                try
                {
                    var result = await next(context, cancellationToken);

                    logger.LogInformation(
                        "[tool] RESULT {Tool} in {ElapsedMs} ms: {Result}",
                        name,
                        started.ElapsedMilliseconds,
                        Serialize(result));

                    return result;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "[tool] FAILED {Tool} after {ElapsedMs} ms", name, started.ElapsedMilliseconds);
                    throw;
                }
            })
            .Build();

    private static Func<IEnumerable<ChatMessage>, AgentSession?, AgentRunOptions?, AIAgent, CancellationToken, IAsyncEnumerable<AgentResponseUpdate>> StreamAsync(ILogger logger) =>
        (messages, session, options, innerAgent, cancellationToken) => Stream(messages, session, options, innerAgent, logger, cancellationToken);

    private static async IAsyncEnumerable<AgentResponseUpdate> Stream(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        ILogger logger,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var input = messages.ToList();
        var text = new StringBuilder();
        var updateCount = 0;

        logger.LogInformation("[agent] RUN (streaming) input:\n{Input}", Describe(input));

        try
        {
            await foreach (var update in innerAgent.RunStreamingAsync(input, session, options, cancellationToken))
            {
                text.Append(update.Text);
                updateCount++;

                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextContent:
                            break;
                        case ErrorContent error:
                            logger.LogError("[agent] STREAM error: {Message} (code {Code}) {Details}", error.Message, error.ErrorCode, error.Details);
                            break;
                        case FunctionCallContent call:
                            logger.LogInformation("[agent] STREAM function call requested: {Tool} {Arguments}", call.Name, Serialize(call.Arguments));
                            break;
                        default:
                            logger.LogInformation("[agent] STREAM {ContentType}: {Content}", content.GetType().Name, Serialize(content));
                            break;
                    }
                }

                if (update.FinishReason is not null)
                {
                    logger.LogInformation("[agent] STREAM finish reason: {FinishReason}", update.FinishReason);
                }

                yield return update;
            }
        }
        finally
        {
            logger.LogInformation("[agent] RUN (streaming) output after {UpdateCount} updates:\n{Output}", updateCount, Truncate(text.ToString()));
        }
    }

    private static string Describe(IEnumerable<ChatMessage> messages) =>
        string.Join("\n", messages.Select(message => $"  {message.Role}: {Truncate(Summarize(message))}"));

    private static string Summarize(ChatMessage message) =>
        string.Join(" | ", message.Contents.Select(content => content switch
        {
            TextContent text => text.Text,
            FunctionCallContent call => $"<call {call.Name} {Serialize(call.Arguments)}>",
            FunctionResultContent result => $"<result {Serialize(result.Result)}>",
            _ => $"<{content.GetType().Name}>",
        }));

    private static string Serialize(object? value) =>
        value is string text ? Truncate(text) : Truncate(JsonSerializer.Serialize(value));

    private static string Truncate(string value) =>
        value.Length <= MaxLoggedCharacters ? value : value[..MaxLoggedCharacters] + $"... [{value.Length - MaxLoggedCharacters} more chars]";
}
