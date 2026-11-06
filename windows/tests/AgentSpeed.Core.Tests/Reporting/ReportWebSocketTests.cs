using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentSpeed.Core.Reporting;

namespace AgentSpeed.Core.Tests.Reporting;

/// <summary>METRICS 11.9 against report-vectors.json <c>websocket</c>: availability, URL, handshake, messages and their parsing.</summary>
public sealed class ReportWebSocketTests
{
    private static readonly ReportServiceUrl Service = ReportServiceUrl.Parse("https://example.com")!;

    private static JsonElement Vectors => ReportSpec.Vector("websocket");

    private static ReportStreamSettings Settings(JsonElement value) => ReportStreamSettings.FromValues(
        ReportStreamSettings.Fields.Select(f => value.GetProperty(f.Name).GetInt64()).ToArray());

    [Fact]
    public void Discovery()
    {
        foreach (var item in Vectors.GetProperty("discovery").EnumerateArray())
        {
            var name = item.Text("name");
            var document = ReportDiscovery.Parse(ReportSpec.Utf8(item.GetProperty("document").GetRawText()), Service, out var problem);
            Assert.True(document is not null, $"{name}: {problem}");
            var expect = item.GetProperty("expect");
            ReportStreamSettings? expected = expect.ValueKind == JsonValueKind.Null ? null : Settings(expect);
            Assert.True(expected == document.WebSocket, $"{name}: got {document.WebSocket}");

            var stored = ReportDiscovery.Parse(document.ToJson().ToCompactBytes(), Service, out _);
            Assert.True(expected == stored?.WebSocket, $"{name}: the stored copy differs");
        }
    }

    [Fact]
    public void StoredDocumentsOfEarlierBuildsHaveNoWebSocket()
    {
        var json = """{"protocol":"agent-speed-report","versions":[1],"service":{"name":"Old","terms":"https://example.com/t","privacy":"https://example.com/p"},"api":"https://example.com","extensions":{"executionModes":1}}""";
        var document = ReportDiscovery.Parse(ReportSpec.Utf8(json), Service, out _)!;
        Assert.Null(document.WebSocket);
        Assert.False(document.Extensions.ReportWebSocket);
        Assert.DoesNotContain("websocket", document.ToJson().ToCompactString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Url()
    {
        foreach (var item in Vectors.GetProperty("url").EnumerateArray())
        {
            var api = ReportServiceUrl.ParseApi(item.Text("api"), discoveryIsLoopbackHttp: true)!;
            Assert.Equal(item.Text("expect"), ReportWebSocketProtocol.Url(api).AbsoluteUri);
        }
    }

    [Fact]
    public void Handshake()
    {
        var vector = Vectors.GetProperty("handshake");
        var headers = vector.GetProperty("headers").Strings();
        using var key = ReportSpec.Key("device");
        var api = ReportServiceUrl.ParseApi("https://api.example.com/agent-speed", discoveryIsLoopbackHttp: false)!;
        var builder = new ReportRequestBuilder("AgentSpeed/0.0.0-test (Windows)", headers[ReportCanonical.DeviceHeader], ApiKey: null);
        var request = ReportWebSocketProtocol.Handshake(builder, api, key, long.Parse(headers[ReportCanonical.TimestampHeader], System.Globalization.CultureInfo.InvariantCulture),
            headers[ReportCanonical.NonceHeader]);

        Assert.Equal(vector.Text("method"), request.Method);
        Assert.Equal(vector.Text("url"), request.Url.AbsoluteUri);
        Assert.Equal(vector.Text("authority"), ReportUrls.Authority(request.Url));
        Assert.Equal(vector.Text("path"), ReportUrls.RequestPath(request.Url));
        Assert.Empty(request.Body);
        foreach (var name in new[] { ReportCanonical.DeviceHeader, ReportCanonical.TimestampHeader, ReportCanonical.NonceHeader, ReportWebSocketProtocol.SubprotocolHeader })
        {
            Assert.Equal(headers[name], request.Header(name));
        }

        var canonical = ReportSpec.Utf8(vector.Text("canonical"));
        Assert.Equal(vector.Text("canonical"), ReportCanonical.Build("GET", ReportUrls.Authority(request.Url), ReportUrls.RequestPath(request.Url),
            headers[ReportCanonical.TimestampHeader], headers[ReportCanonical.NonceHeader], headers[ReportCanonical.DeviceHeader], []));
        Assert.True(key.PublicKey.Verify(canonical, Base64Url.Decode(request.Header(ReportCanonical.SignatureHeader))!));
        Assert.True(key.PublicKey.Verify(canonical, Base64Url.Decode(headers[ReportCanonical.SignatureHeader])!));
        Assert.Null(request.Header("Authorization"));
    }

    [Fact]
    public void ReportMessage()
    {
        var vector = Vectors.GetProperty("report");
        var body = ReportSpec.Utf8(vector.Text("body"));
        var headers = vector.GetProperty("headers").Strings();
        Assert.Equal(vector.Text("bodySha256"), ReportHash.Sha256Hex(body));
        Assert.Equal(vector.Text("message"), ReportWebSocketProtocol.ReportMessage([.. headers], body));
        Assert.Equal(Encoding.UTF8.GetByteCount(vector.Text("message")), ReportWebSocketProtocol.MessageBytes(body));

        using var key = ReportSpec.Key("device");
        Assert.True(key.PublicKey.Verify(ReportSpec.Utf8(vector.Text("canonical")), Base64Url.Decode(headers[ReportCanonical.SignatureHeader])!));
        var builder = new ReportRequestBuilder("AgentSpeed/0.0.0-test (Windows)", headers[ReportCanonical.DeviceHeader], ApiKey: null);
        var signed = builder.Make("POST", new Uri("https://api.example.com/agent-speed/v1/reports"), body, key,
            long.Parse(headers[ReportCanonical.TimestampHeader], System.Globalization.CultureInfo.InvariantCulture), headers[ReportCanonical.NonceHeader]);
        Assert.True(key.PublicKey.Verify(ReportSpec.Utf8(vector.Text("canonical")), Base64Url.Decode(signed.Header(ReportCanonical.SignatureHeader))!));
        var message = ReportWebSocketProtocol.ReportMessage(signed.Headers, body);
        using var parsed = JsonDocument.Parse(message);
        Assert.Equal(vector.Text("body"), parsed.RootElement.GetProperty("body").GetString());
        Assert.Equal(["X-AgentSpeed-Device", "X-AgentSpeed-Timestamp", "X-AgentSpeed-Nonce", "X-AgentSpeed-Signature"],
            parsed.RootElement.GetProperty("headers").EnumerateObject().Select(p => p.Name));
        Assert.Equal(Encoding.UTF8.GetByteCount(message), ReportWebSocketProtocol.MessageBytes(body));
    }

    [Fact]
    public void BodyEncoding()
    {
        foreach (var item in Vectors.GetProperty("encoding").EnumerateArray())
        {
            var body = ReportSpec.Utf8(item.Text("body"));
            var message = ReportWebSocketProtocol.ReportMessage([.. item.GetProperty("headers").Strings()], body);
            Assert.Equal(item.Text("message"), message);
            Assert.Equal(Encoding.UTF8.GetByteCount(message), ReportWebSocketProtocol.MessageBytes(body));
        }

        var fixedHeaders = Vectors.GetProperty("encoding")[0].GetProperty("headers").Strings();
        var controls = ReportSpec.Utf8("{\"x\":\"\u0001\b\"}");
        Assert.Equal(Encoding.UTF8.GetByteCount(ReportWebSocketProtocol.ReportMessage([.. fixedHeaders], controls)), ReportWebSocketProtocol.MessageBytes(controls));
    }

    [Fact]
    public void Realtime()
    {
        foreach (var item in Vectors.GetProperty("realtime").EnumerateArray())
        {
            var age = item.GetProperty("observationAgeMs");
            var json = ReportWebSocketProtocol.Realtime(item.Text("sourceMode") == "mixed", item.GetProperty("sentAtMs").GetInt64(),
                age.ValueKind == JsonValueKind.Null ? null : age.GetInt64()).ToCompactString();
            var expect = item.GetProperty("expect");
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(expect.GetRawText())), json);
            using var parsed = JsonDocument.Parse(json);
            Assert.Equal(expect.EnumerateObject().Select(p => p.Name), parsed.RootElement.EnumerateObject().Select(p => p.Name));
        }
    }

    [Fact]
    public void ServiceMessages()
    {
        var cases = Vectors.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(30, cases.Count);
        foreach (var item in cases)
        {
            var name = item.Text("name");
            var max = item.TryGetProperty("maxMessageBytes", out var m) ? m.GetInt32() : 65_536;
            var got = ReportServiceMessage.Parse(ReportSpec.Utf8(item.Text("text")), max);
            var expect = item.GetProperty("expect");
            if (expect.ValueKind == JsonValueKind.String)
            {
                Assert.True(got is null, $"{name}: expected invalid, got {got}");
                continue;
            }

            switch (expect.Text("type"))
            {
                case "other":
                    Assert.True(got is ReportServiceMessage.Other, $"{name}: got {got}");
                    break;
                case "ready":
                    var ready = Assert.IsType<ReportServiceMessage.Ready>(got);
                    Assert.Equal(Settings(expect.GetProperty("limits")), ready.Limits);
                    break;
                case "ack":
                    var ack = Assert.IsType<ReportServiceMessage.Ack>(got);
                    Assert.Equal(expect.Text("runId"), ack.RunId);
                    Assert.Equal(expect.GetProperty("seq").GetInt64(), ack.Seq);
                    Assert.Equal(expect.Text("status"), ack.Status == ReportAckStatus.Duplicate ? "duplicate" : "accepted");
                    Assert.Equal(expect.Text("bodySha256"), ack.BodySha256);
                    Assert.Equal(Optional(expect, "rank"), ack.Rank);
                    break;
                case "error":
                    var error = Assert.IsType<ReportServiceMessage.Error>(got);
                    Assert.Equal(expect.OptionalText("runId"), error.RunId);
                    Assert.Equal(Optional(expect, "seq"), error.Seq);
                    Assert.Equal(expect.GetProperty("httpStatus").GetInt32(), error.HttpStatus);
                    Assert.Equal(expect.Text("code"), error.Code);
                    Assert.Equal(Optional(expect, "retryAfterSeconds"), error.RetryAfterSeconds);
                    Assert.Equal(Optional(expect, "serverTime"), error.ServerTime);
                    Assert.Equal(expect.GetProperty("fatal").GetBoolean(), error.Fatal);
                    break;
                default:
                    Assert.Fail(name);
                    break;
            }
        }

        Assert.Null(ReportServiceMessage.Parse([(byte)'{', 0xFF, (byte)'}'], 65_536));
        Assert.Null(ReportServiceMessage.Parse([0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}'], 65_536));
    }

    [Fact]
    public void Rejections()
    {
        foreach (var item in Vectors.GetProperty("rejections").EnumerateArray())
        {
            var body = item.GetProperty("body").ValueKind == JsonValueKind.Null ? [] : ReportSpec.Utf8(item.Text("body"));
            var got = ReportStreamRejection.Classify(item.GetProperty("status").GetInt32(), body, item.OptionalText("retryAfter"));
            var expect = item.GetProperty("expect");
            Assert.Equal(expect.OptionalText("code"), got.Code);
            Assert.Equal(Optional(expect, "retryAfterSeconds"), got.RetryAfterSeconds);
        }
    }

    [Fact]
    public void Latency()
    {
        foreach (var item in Vectors.GetProperty("latency").EnumerateArray())
        {
            var samples = item.GetProperty("samples").EnumerateArray().Select(v => v.GetInt64()).ToList();
            Assert.Equal(Optional(item, "p50"), ReportStreamLatency.Percentile(samples, 50));
            Assert.Equal(Optional(item, "p95"), ReportStreamLatency.Percentile(samples, 95));
        }

        var ring = new ReportStreamLatency();
        for (var i = 0; i < 600; i++)
        {
            ring.Add(i);
        }

        Assert.Equal(ReportStreamLatency.Capacity, ring.Count);
        Assert.Equal(88, ring.Samples[0]);
    }

    private static long? Optional(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : null;
}
