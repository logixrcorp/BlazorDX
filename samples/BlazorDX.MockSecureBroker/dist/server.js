import express from 'express';
import { AuthTokenError, InvalidClientKeyError, SecureSessionManager } from './SecureSessionManager.js';
import { SecurePayloadWrapper, SessionNotFoundError } from './SecurePayloadWrapper.js';
import { SessionCache } from './SessionCache.js';
import { AuditLog, createTelemetryRouter } from './TelemetryEndpoints.js';
/**
 * Builds the mock broker's Express app. Factored out of index.ts (the same
 * split BlazorDX.MockReportServer uses between its endpoint-mapping
 * extensions and Program.cs) so an E2E test host can mount this in-process
 * via `createApp().app` instead of spawning a separate process, while
 * `npm start` still gets a standalone runnable server.
 */
export function createApp() {
    const cache = new SessionCache();
    const sessionManager = new SecureSessionManager(cache);
    const payloadWrapper = new SecurePayloadWrapper(cache);
    const auditLog = new AuditLog();
    const app = express();
    app.use(express.json({ limit: '1mb' }));
    // MCP tool: initialize_secure_session
    app.post('/mcp/tools/initialize_secure_session', (req, res) => {
        sessionManager
            .initializeSecureSession(req.body ?? {})
            .then((result) => res.status(201).json(result))
            .catch((error) => {
            if (error instanceof AuthTokenError) {
                res.status(401).json({ error: error.message });
            }
            else if (error instanceof InvalidClientKeyError) {
                res.status(400).json({ error: error.message });
            }
            else {
                res.status(500).json({ error: 'Unexpected error initializing secure session' });
            }
        });
    });
    // MCP tool: wrap_secure_payload -- produces the opaque token that would be
    // delivered to BlazorDX's McpProxyEndpoint at POST /mcp-proxy/deliver.
    app.post('/mcp/tools/wrap_secure_payload', (req, res) => {
        const { sessionId, plaintext } = (req.body ?? {});
        if (typeof sessionId !== 'string' || typeof plaintext !== 'string') {
            res.status(400).json({ error: 'sessionId and plaintext are required strings' });
            return;
        }
        payloadWrapper
            .encryptPayload(sessionId, plaintext)
            .then((result) => res.status(200).json(result))
            .catch((error) => {
            if (error instanceof SessionNotFoundError) {
                res.status(404).json({ error: error.message });
            }
            else {
                res.status(500).json({ error: 'Unexpected error wrapping payload' });
            }
        });
    });
    app.use('/api/telemetry', createTelemetryRouter(cache, auditLog));
    app.get('/', (_req, res) => {
        res.type('text/plain').send([
            'Mock Secure Broker -- external provider side of the Zero-Trust Ephemeral Chat conduit',
            '  POST /mcp/tools/initialize_secure_session   (client pubkey + auth token -> sessionId, server pubkey)',
            '  POST /mcp/tools/wrap_secure_payload          ({ sessionId, plaintext } -> { token })',
            '  POST /api/telemetry/access                   (client proof that a payload was read)',
            '  POST /api/telemetry/destruction               (client proof that a payload was zeroed)',
            '',
            "  This is BlazorDX's dependency, not its host: BlazorDX is the CLIENT of this",
            '  server. The token this returns is what BlazorDX.McpBrokerClient would relay',
            '  onward to BlazorDX.McpProxyEndpoint at POST /mcp-proxy/deliver.',
            '',
        ].join('\n'));
    });
    return { app, cache, sessionManager, payloadWrapper, auditLog };
}
