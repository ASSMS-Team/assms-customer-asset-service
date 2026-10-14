using System.Net;
using System.Net.Http.Headers;

namespace CustomerAssetService.Services;

public enum DispatchDeactivationOutcome
{
    Success,
    HasOpenAssignments,
    NotFound,
    Unavailable
}

public interface IDispatchTechnicianVerifier
{
    Task<int> VerifyAsync(string technicianId, string authorization, CancellationToken cancellationToken);
    Task<DispatchDeactivationOutcome> DeactivateTechnicianAsync(string technicianId, string authorization, CancellationToken cancellationToken) =>
        Task.FromResult(DispatchDeactivationOutcome.Success);
}

public sealed class DispatchTechnicianVerifier(HttpClient client, IConfiguration configuration) : IDispatchTechnicianVerifier
{
    public async Task<int> VerifyAsync(string technicianId, string authorization, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["DispatchService:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == "https" || uri.Scheme == "http")) return 503;
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(uri.AbsoluteUri.TrimEnd('/') + "/api/technicians/" + Uri.EscapeDataString(technicianId)));
        if (!AuthenticationHeaderValue.TryParse(authorization, out var bearer) || bearer.Scheme != "Bearer") return 401;
        request.Headers.Authorization = bearer;
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            return response.StatusCode == HttpStatusCode.OK ? 200 : response.StatusCode == HttpStatusCode.NotFound ? 404 : 503;
        }
        catch (HttpRequestException) { return 503; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return 503; }
    }

    public async Task<DispatchDeactivationOutcome> DeactivateTechnicianAsync(string technicianId, string authorization, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["DispatchService:BaseUrl"];
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == "https" || uri.Scheme == "http")) return DispatchDeactivationOutcome.Unavailable;

        using var request = new HttpRequestMessage(HttpMethod.Post,
            new Uri(uri.AbsoluteUri.TrimEnd('/') + "/api/technicians/" + Uri.EscapeDataString(technicianId) + "/deactivate"));

        if (!AuthenticationHeaderValue.TryParse(authorization, out var bearer) || bearer.Scheme != "Bearer")
            return DispatchDeactivationOutcome.Unavailable;

        request.Headers.Authorization = bearer;

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
                return DispatchDeactivationOutcome.Success;
            if (response.StatusCode == HttpStatusCode.Conflict)
                return DispatchDeactivationOutcome.HasOpenAssignments;
            if (response.StatusCode == HttpStatusCode.NotFound)
                return DispatchDeactivationOutcome.NotFound;

            return DispatchDeactivationOutcome.Unavailable;
        }
        catch (HttpRequestException) { return DispatchDeactivationOutcome.Unavailable; }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return DispatchDeactivationOutcome.Unavailable; }
    }
}
