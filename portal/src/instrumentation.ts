import { registerOTel } from "@vercel/otel";

// OpenTelemetry tracing (milestone M6.1). Exports over OTLP/HTTP only when OTEL_EXPORTER_OTLP_ENDPOINT is set, and
// propagates W3C trace context to the Management API and the identity provider, so one trace spans the whole request.
export function register() {
  if (!process.env.OTEL_EXPORTER_OTLP_ENDPOINT) {
    return;
  }

  registerOTel({
    serviceName: "dovepeak-portal",
    attributes: { "service.namespace": "dovepeak-identity" },
    instrumentationConfig: {
      fetch: {
        // Trace context goes only to our own backends, never to third parties.
        propagateContextUrls: [process.env.MANAGEMENT_API_URL, process.env.IDENTITY_INTERNAL_URL].filter((url): url is string => Boolean(url)),
      },
    },
  });
}
