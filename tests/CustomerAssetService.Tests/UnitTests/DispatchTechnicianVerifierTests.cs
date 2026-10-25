using System.Net;
using CustomerAssetService.Services;
using Microsoft.Extensions.Configuration;

namespace CustomerAssetService.Tests.UnitTests;

public sealed class DispatchTechnicianVerifierTests
{
    private const string TechnicianId = "5d1b6f0e-4f7d-4d9b-9a52-2b6d0f5e1a77";
    private const string Bearer = "Bearer staff-token";

    private static DispatchTechnicianVerifier Verifier(RecordingHttpHandler handler, string? baseUrl = "http://dispatch.local:5055/") =>
        new(new HttpClient(handler), new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DispatchService:BaseUrl"] = baseUrl }).Build());

    [Theory]
    [InlineData(HttpStatusCode.OK, 200)]
    [InlineData(HttpStatusCode.NotFound, 404)]
    [InlineData(HttpStatusCode.InternalServerError, 503)]
    [InlineData(HttpStatusCode.Forbidden, 503)]
    public async Task Verify_MapsDispatchResponse(HttpStatusCode dispatchStatus, int expected)
    {
        var handler = RecordingHttpHandler.Returning(dispatchStatus);

        var status = await Verifier(handler).VerifyAsync(TechnicianId, Bearer, CancellationToken.None);

        Assert.Equal(expected, status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal($"http://dispatch.local:5055/api/technicians/{TechnicianId}", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("staff-token", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Verify_EscapesTheTechnicianIdInThePath()
    {
        var handler = RecordingHttpHandler.Returning(HttpStatusCode.OK);

        await Verifier(handler, "https://dispatch.example.com").VerifyAsync("a b/c", Bearer, CancellationToken.None);

        Assert.Equal("https://dispatch.example.com/api/technicians/a%20b%2Fc", Assert.Single(handler.Requests).RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://dispatch.local")]
    [InlineData("/relative/path")]
    public async Task Verify_Returns503WithoutCalling_WhenBaseUrlIsMissingOrNotHttp(string? baseUrl)
    {
        var handler = RecordingHttpHandler.Returning(HttpStatusCode.OK);

        Assert.Equal(503, await Verifier(handler, baseUrl).VerifyAsync(TechnicianId, Bearer, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Basic dXNlcjpwYXNz")]
    public async Task Verify_Returns401WithoutCalling_WhenAuthorizationIsNotABearerToken(string authorization)
    {
        var handler = RecordingHttpHandler.Returning(HttpStatusCode.OK);

        var status = await Verifier(handler).VerifyAsync(TechnicianId, authorization, CancellationToken.None);

        Assert.Equal(401, status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Verify_Returns503_WhenDispatchIsUnreachable()
    {
        var handler = RecordingHttpHandler.Returning(HttpStatusCode.OK);
        handler.Throw = new HttpRequestException("connection refused");

        Assert.Equal(503, await Verifier(handler).VerifyAsync(TechnicianId, Bearer, CancellationToken.None));
    }

    [Fact]
    public async Task Verify_Returns503_WhenTheRequestTimesOut()
    {
        var handler = RecordingHttpHandler.Returning(HttpStatusCode.OK);
        handler.Throw = new TaskCanceledException("timeout");

        Assert.Equal(503, await Verifier(handler).VerifyAsync(TechnicianId, Bearer, CancellationToken.None));
    }

    [Fact]
    public async Task Verify_PropagatesCancellation_RequestedByTheCaller()
    {
        var handler = RecordingHttpHandler.Returning(HttpStatusCode.OK);
        handler.Throw = new TaskCanceledException("cancelled");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<TaskCanceledException>(() => Verifier(handler).VerifyAsync(TechnicianId, Bearer, cts.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, DispatchDeactivationOutcome.Success)]
    [InlineData(HttpStatusCode.Conflict, DispatchDeactivationOutcome.HasOpenAssignments)]
    [InlineData(HttpStatusCode.NotFound, DispatchDeactivationOutcome.NotFound)]
    [InlineData(HttpStatusCode.BadGateway, DispatchDeactivationOutcome.Unavailable)]
    public async Task Deactivate_MapsDispatchResponse(HttpStatusCode dispatchStatus, DispatchDeactivationOutcome expected)
    {
        var handler = RecordingHttpHandler.Returning(dispatchStatus);

        var outcome = await Verifier(handler).DeactivateTechnicianAsync(TechnicianId, Bearer, CancellationToken.None);

        Assert.Equal(expected, outcome);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal($"http://dispatch.local:5055/api/technicians/{TechnicianId}/deactivate", request.RequestUri!.ToString());
        Assert.Equal("staff-token", request.Headers.Authorization!.Parameter);
        Assert.Equal("Bearer", request.Headers.Authorization.Scheme);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ftp://dispatch.local")]
    [InlineData("nonsense")]
    public async Task Deactivate_IsUnavailableWithoutCalling_WhenBaseUrlIsInvalid(string? baseUrl)
    {
        var handler = RecordingHttpHandler.Returning(HttpStatusCode.OK);

        Assert.Equal(DispatchDeactivationOutcome.Unavailable,
            await Verifier(handler, baseUrl).DeactivateTechnicianAsync(TechnicianId, Bearer, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Basic dXNlcjpwYXNz")]
    public async Task Deactivate_IsUnavailableWithoutCalling_WhenAuthorizationIsNotBearer(string authorization)
    {
        var handler = RecordingHttpHandler.Returning(HttpStatusCode.OK);

        Assert.Equal(DispatchDeactivationOutcome.Unavailable,
            await Verifier(handler).DeactivateTechnicianAsync(TechnicianId, authorization, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Deactivate_IsUnavailable_WhenDispatchIsUnreachableOrTimesOut()
    {
        var unreachable = RecordingHttpHandler.Returning(HttpStatusCode.OK);
        unreachable.Throw = new HttpRequestException("down");
        var timeout = RecordingHttpHandler.Returning(HttpStatusCode.OK);
        timeout.Throw = new TaskCanceledException("timeout");

        Assert.Equal(DispatchDeactivationOutcome.Unavailable, await Verifier(unreachable).DeactivateTechnicianAsync(TechnicianId, Bearer, CancellationToken.None));
        Assert.Equal(DispatchDeactivationOutcome.Unavailable, await Verifier(timeout).DeactivateTechnicianAsync(TechnicianId, Bearer, CancellationToken.None));
    }

    [Fact]
    public async Task Deactivate_PropagatesCancellation_RequestedByTheCaller()
    {
        var handler = RecordingHttpHandler.Returning(HttpStatusCode.OK);
        handler.Throw = new TaskCanceledException("cancelled");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<TaskCanceledException>(() => Verifier(handler).DeactivateTechnicianAsync(TechnicianId, Bearer, cts.Token));
    }

    [Fact]
    public async Task InterfaceDefaultDeactivation_ReportsSuccess()
    {
        IDispatchTechnicianVerifier verifier = new VerifyOnly();

        Assert.Equal(DispatchDeactivationOutcome.Success, await verifier.DeactivateTechnicianAsync(TechnicianId, Bearer, CancellationToken.None));
    }

    private sealed class VerifyOnly : IDispatchTechnicianVerifier
    {
        public Task<int> VerifyAsync(string technicianId, string authorization, CancellationToken cancellationToken) => Task.FromResult(200);
    }
}
