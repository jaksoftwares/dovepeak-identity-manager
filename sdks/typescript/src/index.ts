// @dovepeak/identity: core OpenID Connect client (browser and server).
export { createBrowserClient, createServerClient } from "./client.js";
export type {
  AuthorizationRequestOptions,
  AuthorizationTransaction,
  DovepeakClient,
  DovepeakServerClient,
  TokenSet,
  UserClaims,
} from "./client.js";
export type { BrowserClientConfig, ServerClientConfig } from "./config.js";
export {
  AuthorizationError,
  ConfigurationError,
  DovepeakError,
  ForbiddenError,
  NetworkError,
  TokenRequestError,
  TokenVerificationError,
} from "./errors.js";
export type { TokenVerificationReason } from "./errors.js";
