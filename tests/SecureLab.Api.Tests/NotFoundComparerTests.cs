using System.Net;

namespace SecureLab.Api.Tests;

public sealed class NotFoundComparerTests
{
    private const string ProblemJson = "application/problem+json";
    private const string TraceForeign = "00-24f77fa4915ed09a527ca7e3900eddc9-d8ebfbbb891cb12b-00";
    private const string TraceUnknown = "00-1655c23bdb00c3c71a27dc10d202eab7-8487063818d59028-00";

    // Копія реальної відповіді helper; extra додає поле перед traceId.
    private static string Body(string traceId, string extra = "") =>
        "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.5\",\"title\":\"Інцидент не знайдено\",\"status\":404"
        + extra + ",\"traceId\":\"" + traceId + "\"}";

    private static ProblemSample Sample(string mediaType, string body) =>
        new ProblemSample(HttpStatusCode.NotFound, mediaType, body);

    private static (ProblemSample Foreign, ProblemSample Unknown) Pair(string name) => name switch
    {
        "trace-only" => (Sample(ProblemJson, Body(TraceForeign)), Sample(ProblemJson, Body(TraceUnknown))),
        "owner-hint" => (Sample(ProblemJson, Body(TraceForeign, ",\"ownerHint\":\"bob\"")), Sample(ProblemJson, Body(TraceUnknown))),
        "content-type" => (Sample(ProblemJson, Body(TraceForeign)), Sample("application/json", Body(TraceUnknown))),
        "detail" => (Sample(ProblemJson, Body(TraceForeign, ",\"detail\":\"A\"")), Sample(ProblemJson, Body(TraceUnknown, ",\"detail\":\"B\""))),
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    [Theory]
    [InlineData("trace-only", true, true)]
    [InlineData("owner-hint", false, true)]
    [InlineData("content-type", false, true)]
    [InlineData("detail", false, false)]
    public void SyntheticPair_MatchesPrediction(string pair, bool expectedStrict, bool expectedLegacy)
    {
        var (foreign, unknown) = Pair(pair);

        Assert.Equal(expectedStrict, ProblemComparer.AreSame(foreign, unknown));
        Assert.Equal(expectedLegacy, ProblemComparer.LegacyAreSame(foreign, unknown));
    }

    [Fact]
    public void CorrelationAllowlist_IsExactlyTraceIdAndRequestId()
    {
        Assert.Equal(new[] { "requestId", "traceId" }, ProblemComparer.CorrelationFields.Order().ToArray());
    }
}