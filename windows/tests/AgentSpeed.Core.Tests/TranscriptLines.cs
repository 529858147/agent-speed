namespace AgentSpeed.Core.Tests;

/// <summary>Builders for synthetic Claude transcript lines.</summary>
internal static class ClaudeLines
{
    public const long T0 = 1_792_490_400_000;

    public static string User(long ms, string contentJson, string cwd = "/home/me/proj", string extra = "") =>
        $$"""{"type":"user","timestamp":"{{Ts.Iso(ms)}}","cwd":"{{cwd}}"{{extra}},"message":{"role":"user","content":{{contentJson}} } }""";

    public static string UserText(long ms, string text) => User(ms, "\"" + text + "\"");

    public static string ApiRequest(long ms) =>
        $$"""{"type":"api-request","timestamp":"{{Ts.Iso(ms)}}","id":"api-{{ms}}"}""";

    public static string Assistant(
        long ms,
        string id,
        long output,
        string stopReasonJson = "\"end_turn\"",
        string model = "claude-opus-5-5",
        long input = 1,
        long cacheCreation = 0,
        long cacheRead = 0,
        long? thinking = null,
        string extra = "")
    {
        var details = thinking is { } t ? $$""","output_tokens_details":{"thinking_tokens":{{t}} }""" : string.Empty;
        return $$"""{"type":"assistant","timestamp":"{{Ts.Iso(ms)}}","requestId":"req_{{id}}","uuid":"uuid-{{id}}-{{ms}}"{{extra}},"message":{"id":"{{id}}","type":"message","role":"assistant","model":"{{model}}","content":[{"type":"text","text":"x"}],"stop_reason":{{stopReasonJson}},"usage":{"input_tokens":{{input}},"cache_creation_input_tokens":{{cacheCreation}},"cache_read_input_tokens":{{cacheRead}},"output_tokens":{{output}}{{details}} } } }""";
    }
}

/// <summary>Builders for synthetic Codex rollout lines.</summary>
internal static class CodexLines
{
    public const long T0 = 1_792_490_400_000;

    public static string Envelope(long ms, string type, string payloadJson) =>
        $$"""{"timestamp":"{{Ts.Iso(ms)}}","type":"{{type}}","payload":{{payloadJson}} }""";

    public static string SessionMeta(long ms, string id = "thread-1", string cwd = "/home/me/rocket", string extra = "") =>
        Envelope(ms, "session_meta", $$"""{"id":"{{id}}","timestamp":"{{Ts.Iso(ms)}}","cwd":"{{cwd}}"{{extra}} }""");

    public static string TurnContext(long ms, string model = "gpt-6.1-sol", string? cwd = null) =>
        Envelope(ms, "turn_context", cwd is null ? $$"""{"model":"{{model}}"}""" : $$"""{"cwd":"{{cwd}}","model":"{{model}}"}""");

    public static string UserMessage(long ms) =>
        Envelope(ms, "response_item", """{"type":"message","role":"user","content":[{"type":"input_text","text":"hi"}]}""");

    public static string TaskStarted(long ms) => Envelope(ms, "event_msg", """{"type":"task_started","turn_id":"t"}""");

    public static string TaskComplete(long ms) => Envelope(ms, "event_msg", """{"type":"task_complete","turn_id":"t"}""");

    public static string FunctionCall(long ms) =>
        Envelope(ms, "response_item", """{"type":"function_call","name":"shell","arguments":"{}","call_id":"c"}""");

    public static string FunctionCallOutput(long ms) =>
        Envelope(ms, "response_item", """{"type":"function_call_output","call_id":"c","output":"ok"}""");

    public static string Usage(long input, long cached, long output, long reasoning, long? total = null) =>
        $$"""{"input_tokens":{{input}},"cached_input_tokens":{{cached}},"output_tokens":{{output}},"reasoning_output_tokens":{{reasoning}},"total_tokens":{{total ?? input + output}} }""";

    public static string UsageRecord(long ms, string? responseId, string usageJson, string threadId = "thread-1") =>
        Envelope(ms, "token_usage_record", responseId is null
            ? $$"""{"thread_id":"{{threadId}}","usage":{{usageJson}} }"""
            : $$"""{"thread_id":"{{threadId}}","response_id":"{{responseId}}","usage":{{usageJson}} }""");

    public static string TokenCount(long ms, string totalJson, string lastJson) =>
        Envelope(ms, "event_msg", $$"""{"type":"token_count","info":{"total_token_usage":{{totalJson}},"last_token_usage":{{lastJson}} } }""");
}
