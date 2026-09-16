using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Kodvian.Core.Application.Auth.Abstractions;
using Kodvian.Core.Application.Auth.Dtos;
using Kodvian.Core.Application.Common.Security;
using Kodvian.Core.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Kodvian.Core.Infrastructure.Services;

public class TokenService : ITokenService
{
    private readonly JwtOptions _jwtOptions;

    public TokenService(IOptions<JwtOptions> jwtOptions)
    {
        _jwtOptions = jwtOptions.Value;
    }

    public GeneratedTokenDto GenerateToken(TokenGenerationDto tokenGeneration)
    {
        var expiration = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpirationMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, tokenGeneration.UserId.ToString()),
            new(JwtRegisteredClaimNames.Email, tokenGeneration.Email),
            new(ClaimTypes.NameIdentifier, tokenGeneration.UserId.ToString()),
            new(ClaimTypes.Name, tokenGeneration.FullName),
            new(CustomClaimTypes.SessionVersion, tokenGeneration.SessionVersion.ToString())
        };
        claims.AddRange(tokenGeneration.Roles.Distinct().Select(role => new Claim(ClaimTypes.Role, role)));

        if (tokenGeneration.DeveloperId.HasValue)
        {
            claims.Add(new Claim(CustomClaimTypes.DeveloperId, tokenGeneration.DeveloperId.Value.ToString()));
        }

        claims.AddRange(tokenGeneration.Permissions.Select(permission => new Claim(CustomClaimTypes.Permission, permission)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var tokenDescriptor = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: expiration,
            signingCredentials: credentials
        );

        return new GeneratedTokenDto
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor),
            ExpiresAtUtc = expiration
        };
    }
}
