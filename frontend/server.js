// Thin Express host: serves the static UI and forwards /api/* to the C# backend.
// No scoring or business logic lives here — that all stays server-side in ASP.NET Core.
'use strict';

const express = require('express');
const path = require('path');

const app = express();
const PORT = process.env.PORT || 3000;
const BACKEND_URL = process.env.BACKEND_URL || 'http://localhost:5017';

app.use(express.static(path.join(__dirname, 'public')));

app.use('/api', express.raw({ type: '*/*', limit: '5mb' }));

app.use('/api', async (req, res) => {
  const targetUrl = BACKEND_URL + '/api' + req.url;
  const hasBody = !['GET', 'HEAD'].includes(req.method) && Buffer.isBuffer(req.body) && req.body.length > 0;

  try {
    const response = await fetch(targetUrl, {
      method: req.method,
      headers: hasBody ? { 'Content-Type': req.headers['content-type'] || 'application/json' } : {},
      body: hasBody ? req.body : undefined,
    });
    const text = await response.text();
    res.status(response.status);
    const contentType = response.headers.get('content-type');
    if (contentType) res.set('Content-Type', contentType);
    res.send(text);
  } catch (err) {
    console.error('Proxy error contacting backend:', err.message);
    res.status(502).json({ error: 'Backend unreachable', detail: err.message });
  }
});

app.listen(PORT, () => {
  console.log(`Frontend listening on http://localhost:${PORT}, proxying /api/* to ${BACKEND_URL}`);
});
