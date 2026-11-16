using System.Net.Http.Headers;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace MedApp.Api.Fhir;

/// <summary>
/// Attaches an Azure AD access token (managed identity in Azure, developer credentials locally)
/// for scope "{Fhir:Url}/.default". Only enabled when <c>Fhir:UseManagedIdentity</c> is true.
/// The token is only ever sent to the configured FHIR host.
/// </summary>
internal sealed class ManagedIdentityTokenHandler : DelegatingHandler
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly TokenCredential _credential;
    private readonly Uri _fhirBase;
    private readonly TokenRequestContext _context;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AccessToken _cached;

    public ManagedIdentityTokenHandler(TokenCredential credential, IOptions<FhirOptions> options)
    {
        _credential = credential;
        _fhirBase = new Uri(options.Value.Url);
        _context = new TokenRequestContext([$"{_fhirBase.GetLeftPart(UriPartial.Authority)}/.default"]);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var target = request.RequestUri;
        if (target is null
            || !string.Equals(target.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || Uri.Compare(target, _fhirBase, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
        {
            throw new InvalidOperationException("Refusing to send a FHIR access token to a host other than the configured HTTPS FHIR server.");
        }

        var token = await GetTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_cached.ExpiresOn - RefreshMargin > DateTimeOffset.UtcNow)
        {
            return _cached.Token;
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_cached.ExpiresOn - RefreshMargin <= DateTimeOffset.UtcNow)
            {
                _cached = await _credential.GetTokenAsync(_context, ct).ConfigureAwait(false);
            }
            return _cached.Token;
        }
        finally
        {
            _lock.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _lock.Dispose();
        }
        base.Dispose(disposing);
    }
}
