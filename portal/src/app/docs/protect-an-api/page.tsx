export const metadata = { title: "Protect an API" };

export default function ProtectApi() {
  return (
    <article>
      <h1>Protect an API</h1>
      <p>Resource servers validate access tokens locally with the environment&apos;s public keys. Every check matters:</p>
      <ul>
        <li><strong>Signature</strong> with keys from the JWKS (discovered from the issuer), accepting only <code>RS256</code>.</li>
        <li><strong>Issuer</strong> equal to your environment&apos;s issuer.</li>
        <li><strong>Audience</strong> containing your API&apos;s identifier (add it to the calling application&apos;s audiences).</li>
        <li><strong>Expiry</strong>, with at most a small clock skew.</li>
      </ul>
      <h2>ASP.NET Core</h2>
      <pre>{`builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = "<issuer>";
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = "<issuer>",
            ValidAudience = "orders-api",
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("admin", p => p.RequireClaim("roles", "administrator"))
    .AddPolicy("orders:read", p => p.RequireAssertion(ctx =>
        ctx.User.FindAll("scope").SelectMany(c => c.Value.Split(' ')).Contains("orders:read")));`}</pre>
      <p>A complete example lives in <code>examples/dotnet-protected-api</code>.</p>
      <h2>Node.js</h2>
      <pre>{`import { createRemoteJWKSet, jwtVerify } from "jose";

const issuer = "<issuer>";
const jwks = createRemoteJWKSet(new URL(\`\${issuer}/protocol/openid-connect/certs\`));

export async function verify(token) {
  const { payload } = await jwtVerify(token, jwks, { issuer, audience: "orders-api", algorithms: ["RS256"] });
  return payload; // payload.roles, payload.scope
}`}</pre>
      <h2>Roles and scopes</h2>
      <ul>
        <li><code>roles</code>: array of the user&apos;s roles for the calling application (define and assign them on the application&apos;s Roles tab).</li>
        <li><code>scope</code>: space-separated scopes. Define them per environment, grant them to applications, and have applications request them.</li>
      </ul>
      <h2>Revocation</h2>
      <p>Locally validated tokens stay valid until they expire (10 minutes by default). For operations that need immediate revocation, call the token introspection endpoint as well.</p>
    </article>
  );
}
