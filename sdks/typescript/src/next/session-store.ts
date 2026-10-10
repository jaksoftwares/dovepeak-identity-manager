/**
 * Server-side storage for sessions and in-flight sign-ins. Tokens live here, never in the browser.
 * Use {@link redisSessionStore} (Redis or Valkey) in production; {@link memorySessionStore} only for a single
 * development process.
 */
export interface SessionStore {
  get(key: string): Promise<string | null>;
  set(key: string, value: string, ttlSeconds: number): Promise<void>;
  /** Reads and deletes atomically: each sign-in transaction can complete only once. */
  take(key: string): Promise<string | null>;
  delete(key: string): Promise<void>;
  /** Sets the key only if absent, with a TTL in milliseconds. True when this caller acquired it (a lock). */
  acquire(key: string, value: string, ttlMs: number): Promise<boolean>;
}

/** The subset of an ioredis (or compatible) client the store needs. */
export interface RedisLike {
  get(key: string): Promise<string | null>;
  // Loosely typed so ioredis' many overloads (and other compatible clients) fit.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  set(key: string, value: string, ...args: any[]): Promise<unknown>;
  getdel(key: string): Promise<string | null>;
  del(key: string): Promise<unknown>;
}

export function redisSessionStore(redis: RedisLike, prefix = "dovepeak:"): SessionStore {
  return {
    get: (key) => redis.get(prefix + key),
    async set(key, value, ttlSeconds) {
      await redis.set(prefix + key, value, "EX", Math.max(1, Math.ceil(ttlSeconds)));
    },
    take: (key) => redis.getdel(prefix + key),
    async delete(key) {
      await redis.del(prefix + key);
    },
    async acquire(key, value, ttlMs) {
      return (await redis.set(prefix + key, value, "PX", ttlMs, "NX")) === "OK";
    },
  };
}

/** In-process store for local development and tests. Sessions are lost on restart and not shared between instances. */
export function memorySessionStore(): SessionStore {
  const data = new Map<string, { value: string; expires: number }>();
  const live = (key: string) => {
    const entry = data.get(key);
    if (entry && entry.expires <= Date.now()) {
      data.delete(key);
      return undefined;
    }

    return entry;
  };

  return {
    async get(key) {
      return live(key)?.value ?? null;
    },
    async set(key, value, ttlSeconds) {
      data.set(key, { value, expires: Date.now() + ttlSeconds * 1000 });
    },
    async take(key) {
      const value = live(key)?.value ?? null;
      data.delete(key);
      return value;
    },
    async delete(key) {
      data.delete(key);
    },
    async acquire(key, value, ttlMs) {
      if (live(key)) return false;
      data.set(key, { value, expires: Date.now() + ttlMs });
      return true;
    },
  };
}
