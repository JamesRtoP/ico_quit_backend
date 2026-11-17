using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MedApp.Api.Tests.Support;

/// <summary>
/// Mints tokens the same way <c>dotnet user-jwts</c> does (symmetric key configured under
/// Authentication:Schemes:Bearer), so tests exercise the real LocalJwt validation path.
/// </summary>
public sealed class TestTokens
{
    public const string Issuer = "medapp-tests";
    public const string Audience = "medapp-api";

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public IReadOnlyDictionary<string, string> Settings => new Dictionary<string, string>
    {
        ["Authentication:Schemes:Bearer:ValidIssuer"] = Issuer,
        ["Authentication:Schemes:Bearer:ValidAudiences:0"] = Audience,
        ["Authentication:Schemes:Bearer:SigningKeys:0:Issuer"] = Issuer,
        ["Authentication:Schemes:Bearer:SigningKeys:0:Value"] = Convert.ToBase64String(_key),
    };

    public string For(string? oid, TimeSpan? lifetime = null, byte[]? signingKey = null, string? clientId = null)
    {
        var now = DateTime.UtcNow;
        var life = lifetime ?? TimeSpan.FromMinutes(10);
        var claims = new List<Claim> { new("sub", oid ?? "no-oid-user") };
        if (oid is not null)
        {
            claims.Add(new Claim("oid", oid));
        }
        if (clientId is not null)
        {
            claims.Add(new Claim("azp", clientId)); // client app the token was issued to (on-behalf-of: the chatbot)
        }

        var notBefore = life > TimeSpan.Zero ? now.AddMinutes(-1) : now.Add(life).AddMinutes(-10);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = notBefore,
            NotBefore = notBefore,
            Expires = now.Add(life),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(signingKey ?? _key), SecurityAlgorithms.HmacSha256),
        });
    }
}
