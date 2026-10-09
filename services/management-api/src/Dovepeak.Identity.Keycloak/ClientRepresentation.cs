using System.Text.Json.Nodes;

namespace Dovepeak.Identity.Keycloak;

/// <summary>
/// Maps a <see cref="ClientRegistration"/> to a Keycloak client representation with platform security defaults.
/// </summary>
internal static class ClientRepresentation
{
    public static JsonObject Build(ClientRegistration registration)
    {
        var interactive = registration.Kind != ClientKind.Machine;

        var client = new JsonObject
        {
            ["clientId"] = registration.ClientId,
            ["name"] = registration.Name ?? registration.ClientId,
            ["enabled"] = true,
            ["protocol"] = "openid-connect",
            ["publicClient"] = registration.Kind == ClientKind.Public,
            ["clientAuthenticatorType"] = "client-secret",
            ["standardFlowEnabled"] = interactive,
            ["implicitFlowEnabled"] = false,
            ["directAccessGrantsEnabled"] = false,
            ["serviceAccountsEnabled"] = registration.Kind == ClientKind.Machine,
            ["consentRequired"] = false,

            // Tokens contain only roles explicitly mapped to this client (least privilege).
            ["fullScopeAllowed"] = false,

            ["frontchannelLogout"] = false,
            ["redirectUris"] = ToArray(registration.RedirectUris.Select(u => u.OriginalString)),
            ["webOrigins"] = ToArray(registration.WebOrigins.Select(RedirectUriPolicy.ToOriginString)),
            ["attributes"] = new JsonObject
            {
                ["pkce.code.challenge.method"] = "S256",
                ["post.logout.redirect.uris"] = string.Join("##", registration.PostLogoutRedirectUris.Select(u => u.OriginalString)),
                ["use.refresh.tokens"] = interactive ? "true" : "false",
                ["client_credentials.use_refresh_token"] = "false",
                ["oauth2.device.authorization.grant.enabled"] = "false",
                ["oidc.ciba.grant.enabled"] = "false",
            },
        };

        var attributes = (JsonObject)client["attributes"]!;
        foreach (var (key, value) in registration.TokenPolicy.ToAttributes())
        {
            attributes[key] = value;
        }

        // Every client gets a flat "roles" claim with the user's roles for this application, plus one audience
        // mapper per resource server it may call.
        client["protocolMappers"] = ToArray(registration.Audiences.Select(AudienceMapper).Prepend(RolesMapper(registration.ClientId)));

        return client;
    }

    internal static JsonObject RolesMapper(string clientId) => new()
    {
        ["name"] = "dovepeak-roles",
        ["protocol"] = "openid-connect",
        ["protocolMapper"] = "oidc-usermodel-client-role-mapper",
        ["config"] = new JsonObject
        {
            ["usermodel.clientRoleMapping.clientId"] = clientId,
            ["claim.name"] = "roles",
            ["jsonType.label"] = "String",
            ["multivalued"] = "true",
            ["access.token.claim"] = "true",
            ["id.token.claim"] = "false",
            ["userinfo.token.claim"] = "false",
            ["introspection.token.claim"] = "true",
        },
    };

    internal static JsonObject AudienceMapper(string audience) => new()
    {
        ["name"] = $"audience-{audience}",
        ["protocol"] = "openid-connect",
        ["protocolMapper"] = "oidc-audience-mapper",
        ["config"] = new JsonObject
        {
            ["included.custom.audience"] = audience,
            ["access.token.claim"] = "true",
            ["id.token.claim"] = "false",
            ["introspection.token.claim"] = "true",
        },
    };

    private static JsonArray ToArray(IEnumerable<string> values) => new([.. values.Select(v => (JsonNode)v)]);

    private static JsonArray ToArray(IEnumerable<JsonObject> values) => new([.. values]);
}
