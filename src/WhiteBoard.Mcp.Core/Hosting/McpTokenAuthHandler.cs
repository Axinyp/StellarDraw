using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;
using WhiteBoard.Mcp.Core.Registry;

namespace WhiteBoard.Mcp.Core.Hosting;

public sealed class McpTokenAuthOptions : AuthenticationSchemeOptions
{ }

public sealed class McpTokenAuthHandler : AuthenticationHandler<McpTokenAuthOptions>
{
    private readonly IMcpSessionRegistry _registry;

    public McpTokenAuthHandler(
        IOptionsMonitor<McpTokenAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IMcpSessionRegistry registry)
        : base(options, logger, encoder)
    {
        _registry = registry;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = header["Bearer ".Length..].Trim();
        var binding = await _registry.ResolveAsync(token, "http");

        if (binding is null)
            return AuthenticateResult.Fail("Invalid or expired token");

        var claims = new[] { new Claim("mcp_token", token) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}