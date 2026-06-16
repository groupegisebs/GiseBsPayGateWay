using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GiseBsPayGateway.Entities;
using GiseBsPayGateway.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GiseBsPayGateway.Services;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public DTOs.JwtTokenResponse GenerateToken(ClientApplication app)
    {
        var expires = DateTime.UtcNow.AddMinutes(_options.ExpirationMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, app.Id.ToString()),
            new Claim("app_code", app.AppCode),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: credentials);

        return new DTOs.JwtTokenResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            expires,
            "Bearer");
    }
}
