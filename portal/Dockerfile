# Dovepeak developer portal: Next.js standalone server.
FROM node:22.20.0-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

FROM node:22.20.0-alpine AS build
WORKDIR /app
ENV NEXT_TELEMETRY_DISABLED=1
COPY --from=deps /app/node_modules ./node_modules
COPY . .
# Configuration is read at runtime; the build only needs the issuer origin for the CSP form-action directive.
ARG PLATFORM_ISSUER=http://localhost:8080/realms/dovepeak-platform
ENV PLATFORM_ISSUER=${PLATFORM_ISSUER}
RUN npm run build

FROM node:22.20.0-alpine AS runtime
WORKDIR /app
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1 PORT=3100 HOSTNAME=0.0.0.0
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
COPY --from=build /app/public ./public
USER node
EXPOSE 3100
CMD ["node", "server.js"]
