// Hand-written (not generated). Shared by AppHost and LocalWebService - LocalWebService links this
// file from its User.props - so the deployed host and the local one enforce the same rules.
// Explicit usings throughout: LocalWebService does not enable ImplicitUsings.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace MagicPets.Hosting;

/// <summary>
/// Which Cognito pools may call each module, enforced before any module controller runs
/// (security note M0-1, Platform/SECURITY-NOTE-M0-1-auth-bypass.md).
///
/// All five modules sit behind one AppApi that accepts BOTH pools, and consumerauth allows self
/// sign-up. Without this, an unauthenticated request reached every controller (the generated
/// middleware logs a failed validation and carries on), and any consumer's genuinely valid token
/// reached the Admin module, which grants Admin to every caller.
///
/// Each module controller gets an AuthorizeFilter whose policy names the schemes it accepts. The
/// filter authenticates with exactly those schemes, replaces HttpContext.User with the result, and
/// answers 401 when none of them validates the token. LazyMagic's LzAuthorization then takes the
/// caller's identity from that principal and never from a raw header.
///
/// A filter rather than endpoint metadata because it runs inside MVC, so it needs no
/// UseAuthorization() in the pipeline - which LocalWebService's generated pipeline does not have.
/// </summary>
public static class ModuleAuthEnforcement
{
    public const string TenantAuth = "tenantauth";
    public const string ConsumerAuth = "consumerauth";

    /// <summary>
    /// Module (its controllers' namespace) -> the authenticator schemes whose tokens it accepts.
    /// Empty means anonymous. A module controller missing from this table fails at startup instead
    /// of being served unguarded.
    /// </summary>
    private static readonly Dictionary<string, string[]> ModuleSchemes = new(StringComparer.Ordinal)
    {
        // Every tenantauth caller is Admin for now: AdminModuleAuthorization grants it to all, so
        // this pool binding is the whole of the Admin rule.
        ["AdminModule"] = [TenantAuth],
        ["StoreModule"] = [TenantAuth],
        ["ChatModule"] = [TenantAuth, ConsumerAuth],
        ["ConsumerModule"] = [ConsumerAuth],
        ["PublicModule"] = [],
    };

    /// <summary>
    /// Controllers that authenticate on their own and are left alone: the BFF's /bff, /cbff and
    /// /abff endpoints establish the session, so they cannot require one.
    /// </summary>
    private static readonly string[] SelfAuthenticating = ["LazyMagic.OIDC.Bff"];

    public static IServiceCollection AddModuleAuthEnforcement(this IServiceCollection services)
    {
        services.AddAuthorization();
        services.Configure<MvcOptions>(o => o.Conventions.Add(new ModuleConvention()));
        services.AddSingleton<IPostConfigureOptions<JwtBearerOptions>, StampAuthName>();
        return services;
    }

    private sealed class ModuleConvention : IControllerModelConvention
    {
        public void Apply(ControllerModel controller)
        {
            var ns = controller.ControllerType.Namespace ?? string.Empty;
            if (SelfAuthenticating.Any(s => ns == s || ns.StartsWith(s + ".", StringComparison.Ordinal)))
                return;
            if (!ModuleSchemes.TryGetValue(ns, out var schemes))
                throw new InvalidOperationException(
                    $"{controller.ControllerType.FullName} is in no module that ModuleAuthEnforcement knows. " +
                    "Add its namespace to ModuleSchemes with the pools that may call it.");
            if (schemes.Length == 0)
                return;
            controller.Filters.Add(new AuthorizeFilter(
                new AuthorizationPolicyBuilder(schemes).RequireAuthenticatedUser().Build()));
        }
    }

    /// <summary>
    /// Makes lz-authname name the pool that VALIDATED the token. LzAuthorization copies the header
    /// into CallerInfo.Authname and Chat picks the AppSync Events API from it, but a caller can send
    /// any value and the generated middleware only fills it in when absent. Overwriting it from the
    /// scheme whose validation just succeeded makes it true whenever the caller is authenticated.
    /// </summary>
    private sealed class StampAuthName : IPostConfigureOptions<JwtBearerOptions>
    {
        public void PostConfigure(string? name, JwtBearerOptions options)
        {
            if (string.IsNullOrEmpty(name))
                return;
            options.Events ??= new JwtBearerEvents();
            var inner = options.Events.OnTokenValidated;
            options.Events.OnTokenValidated = async context =>
            {
                await inner(context);
                context.HttpContext.Request.Headers["lz-authname"] = name;
            };
        }
    }

    /// <summary>
    /// For a host that does not register the pools itself (LocalWebService): one JwtBearer scheme
    /// per LZ_AUTH_{NAME}_USERPOOLID (region from LZ_AUTH_{NAME}_REGION, else AWS_REGION), validated
    /// the way AppHost's generated Program.g.cs validates them.
    /// </summary>
    public static IServiceCollection AddCognitoSchemesFromEnvironment(this IServiceCollection services)
    {
        var defaultRegion = Environment.GetEnvironmentVariable("AWS_REGION") ?? "us-east-1";
        var authentication = services.AddAuthentication();
        var registered = 0;
        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
        {
            var match = Regex.Match(variable.Key.ToString() ?? string.Empty,
                "^LZ_AUTH_(?<name>[A-Z0-9_]+)_USERPOOLID$", RegexOptions.IgnoreCase);
            var userPoolId = variable.Value?.ToString();
            if (!match.Success || string.IsNullOrEmpty(userPoolId))
                continue;

            var name = match.Groups["name"].Value.ToLowerInvariant();
            var region = Environment.GetEnvironmentVariable($"LZ_AUTH_{name.ToUpperInvariant()}_REGION") ?? defaultRegion;
            var authority = $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}";
            authentication.AddJwtBearer(name, options =>
            {
                options.Authority = authority;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = authority,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true
                };
            });
            registered++;
        }

        if (registered == 0)
            throw new InvalidOperationException(
                "No authenticators configured. Set LZ_AUTH_TENANTAUTH_USERPOOLID and " +
                "LZ_AUTH_CONSUMERAUTH_USERPOOLID to the environment's Cognito pool ids.");
        return services;
    }
}
