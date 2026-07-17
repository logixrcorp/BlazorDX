import { createApp } from './server.js';
const port = Number(process.env.MOCK_SECURE_BROKER_PORT ?? 4173);
const { app } = createApp();
app.listen(port, () => {
    // eslint-disable-next-line no-console
    console.log(`BlazorDX.MockSecureBroker listening on http://localhost:${port}`);
});
