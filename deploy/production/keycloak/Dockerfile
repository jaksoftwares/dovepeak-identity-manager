# Production Keycloak (milestone M6.2): pre-built ("optimized") image with the Dovepeak theme, PostgreSQL, health,
# metrics, tracing and the distributed Infinispan cache, so two or more nodes form one cluster (discovery through the
# database, jdbc-ping). Build context: repository root.
FROM quay.io/keycloak/keycloak:26.7.5 AS base

# Build-time options. They are set on the final image as well: any kc.sh command run inside a container (for example
# update-compatibility) re-augments with the environment it finds, and without these it would silently persist
# defaults (H2, no tracing) that break the next start.
ENV KC_DB=postgres \
    KC_HEALTH_ENABLED=true \
    KC_METRICS_ENABLED=true \
    KC_HTTP_METRICS_HISTOGRAMS_ENABLED=true \
    KC_FEATURES=client-secret-rotation,user-event-metrics \
    KC_TRACING_ENABLED=true \
    KC_EVENT_METRICS_USER_ENABLED=true \
    KC_EVENT_METRICS_USER_TAGS=realm \
    KC_CACHE=ispn \
    KC_CACHE_STACK=jdbc-ping

FROM base AS builder
COPY identity/keycloak/themes/dovepeak /opt/keycloak/themes/dovepeak
RUN /opt/keycloak/bin/kc.sh build

FROM base
COPY --from=builder /opt/keycloak/ /opt/keycloak/
# kc.sh must stay PID 1: since 26.7 it forwards SIGTERM to Java itself, so stops are graceful.
ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
CMD ["start", "--optimized"]
