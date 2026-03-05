/**
 * Solracer Backend-TS Fastify server
 *
 * Mirrors the frozen Python backend at parity:
 *   GET  /           → API info
 *   GET  /health     → { status: "healthy" }
 *   /api/v1/races/*  → 7 race endpoints
 *   /api/v1/transactions/* → stubs
 */

import Fastify from "fastify";
import cors from "@fastify/cors";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { raceRoutes } from "./routes/races.js";
import { transactionRoutes } from "./routes/transactions.js";
import { payoutRoutes } from "./routes/payouts.js";
import { ghostRoutes } from "./routes/ghost.js";
import { startCleanupInterval, stopCleanupInterval } from "./store/memory.js";
import { startGhostCleanup, stopGhostCleanup } from "./store/ghost.js";

const rawPort = parseInt(process.env.PORT ?? "8001", 10);
const PORT = isNaN(rawPort) ? 8001 : rawPort;
const HOST = process.env.HOST ?? "0.0.0.0";
const API_PREFIX = process.env.API_V1_PREFIX ?? "/api/v1";

const CORS_ORIGIN: string | boolean =
  process.env.CORS_ORIGIN ?? true;

const useHttps = process.env.USE_HTTPS === "true";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const fastifyOptions: any = {
  logger: {
    level: process.env.LOG_LEVEL ?? "info",
    transport:
      process.env.NODE_ENV !== "production"
        ? { target: "pino-pretty", options: { translateTime: "HH:MM:ss Z", ignore: "pid,hostname" } }
        : undefined,
  },
};

if (useHttps) {
  const certsDir = process.env.CERTS_DIR ?? path.resolve(__dirname, "../certs");
  const pfxPath = path.join(certsDir, "cert.pfx");
  const keyPath = path.join(certsDir, "key.pem");
  const certPath = path.join(certsDir, "cert.pem");

  if (fs.existsSync(pfxPath)) {
    // Prefer .pfx — avoids OpenSSL 3 PEM format compatibility issues
    fastifyOptions.https = {
      pfx: fs.readFileSync(pfxPath),
      passphrase: process.env.CERT_PFX_PASSWORD ?? "",
    };
  } else {
    // Fallback to PEM pair
    fastifyOptions.https = {
      key: fs.readFileSync(keyPath),
      cert: fs.readFileSync(certPath),
    };
  }
}

const app = Fastify(fastifyOptions);

async function main(): Promise<void> {
  await app.register(cors, {
    origin: CORS_ORIGIN,
    credentials: true,
    methods: ["GET", "POST", "PUT", "DELETE", "OPTIONS"],
  });

  // Handle empty JSON bodies gracefully (e.g. DELETE with Content-Type: application/json)
  app.addContentTypeParser(
    "application/json",
    { parseAs: "string" },
    (_req, body, done) => {
      try {
        const str = (body as string).trim();
        done(null, str ? JSON.parse(str) : {});
      } catch (err) {
        done(err as Error, undefined);
      }
    }
  );

  app.get("/", async () => ({
    message: "Solracer Backend API",
    version: "1.0.0",
    status: "healthy",
    docs: "/docs",
  }));

  app.get("/health", async () => ({
    status: "healthy",
  }));

  await app.register(raceRoutes, { prefix: API_PREFIX });
  await app.register(transactionRoutes, { prefix: API_PREFIX });
  await app.register(payoutRoutes, { prefix: API_PREFIX });
  await app.register(ghostRoutes, { prefix: API_PREFIX });

  // Background interval: clean up expired races every 60s even under no load.
  // Prevents unbounded memory growth when the server is idle.
  startCleanupInterval(60_000);
  // Ghost positions expire after 30s; clean up every 10s.
  startGhostCleanup();

  try {
    await app.listen({ port: PORT, host: HOST });
    const protocol = useHttps ? "https" : "http";
    console.log(`Solracer Backend-TS listening on ${protocol}://${HOST}:${PORT}`);
  } catch (err) {
    app.log.error(err);
    stopCleanupInterval();
    process.exit(1);
  }
}

async function shutdown(): Promise<void> {
  app.log.info("Shutting down...");
  stopCleanupInterval();
  stopGhostCleanup();
  await app.close();
  process.exit(0);
}

process.on("SIGTERM", () => void shutdown());
process.on("SIGINT", () => void shutdown());

process.on("unhandledRejection", (reason) => {
  console.error("[fatal] Unhandled promise rejection:", reason);
  process.exit(1);
});

main().catch((err) => {
  console.error("[fatal] Server startup failed:", err);
  process.exit(1);
});
