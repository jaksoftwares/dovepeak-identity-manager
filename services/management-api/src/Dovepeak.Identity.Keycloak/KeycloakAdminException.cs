using System.Net;

namespace Dovepeak.Identity.Keycloak;

public sealed class KeycloakAdminException : Exception
{
    public KeycloakAdminException()
    {
    }

    public KeycloakAdminException(string message)
        : base(message)
    {
    }

    public KeycloakAdminException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public KeycloakAdminException(string message, HttpStatusCode statusCode)
        : base($"{message} (HTTP {(int)statusCode})")
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
