/**
 * ⚡ WinoxIT — High-Concurrency Node.js Virtual Server & LAN Gateway
 * Port: 9999 (0.0.0.0 — Accessible from LAN, Wi-Fi, Mobile and Localhost)
 * Zero external dependencies: pure Node.js standard library.
 */

const http = require('http');
const fs = require('fs');
const path = require('path');
const os = require('os');

const PORT = process.env.PORT || 9999;
const HOST = '0.0.0.0';
const WEB_DIR = path.join(__dirname, 'web');

// MIME types dictionary
const MIME_TYPES = {
    '.html': 'text/html; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.js': 'application/javascript; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.jpeg': 'image/jpeg',
    '.gif': 'image/gif',
    '.svg': 'image/svg+xml',
    '.ico': 'image/x-icon',
    '.woff2': 'font/woff2',
    '.woff': 'font/woff',
    '.ttf': 'font/ttf'
};

function getLocalIpAddresses() {
    const interfaces = os.networkInterfaces();
    const addresses = [];
    for (const name of Object.keys(interfaces)) {
        for (const iface of interfaces[name]) {
            if (iface.family === 'IPv4' && !iface.internal) {
                addresses.push({ name, address: iface.address });
            }
        }
    }
    return addresses;
}

const server = http.createServer((req, res) => {
    // CORS headers for local LAN communication
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');

    if (req.method === 'OPTIONS') {
        res.writeHead(204);
        res.end();
        return;
    }

    // Health check endpoint
    if (req.url === '/api/health' || req.url === '/api/status') {
        res.writeHead(200, { 'Content-Type': 'application/json' });
        res.end(JSON.stringify({
            status: 'online',
            platform: 'WinoxIT Virtual Server',
            nodeVersion: process.version,
            uptime: process.uptime(),
            timestamp: new Date().toISOString(),
            hostname: os.hostname(),
            networkInterfaces: getLocalIpAddresses()
        }));
        return;
    }

    // Error reporting endpoint from dashboard self-heal
    if (req.url === '/api/selfheal-report-error' && req.method === 'POST') {
        let body = '';
        req.on('data', chunk => { body += chunk; });
        req.on('end', () => {
            console.log('⚠️ [Self-Heal Telemetry]:', body);
            res.writeHead(200, { 'Content-Type': 'application/json' });
            res.end(JSON.stringify({ received: true }));
        });
        return;
    }

    // Resolve static file path safely
    let reqPath = req.url.split('?')[0];
    if (reqPath === '/' || reqPath === '/dashboard' || reqPath === '/index.html') {
        reqPath = '/dashboard.html';
    }

    const normalizedReq = reqPath.replace(/^\/+/, '');
    const filePath = path.resolve(WEB_DIR, normalizedReq);

    // Prevent directory traversal attacks
    if (!filePath.startsWith(WEB_DIR)) {
        res.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
        res.end('403 Forbidden: Invalid file path');
        return;
    }

    // Block hidden dot files and sensitive file extensions
    const pathParts = normalizedReq.split(/[/\\]/);
    const ext = path.extname(filePath).toLowerCase();
    const BLOCKED_EXTENSIONS = ['.key', '.token', '.env', '.pfx', '.cer', '.db', '.log', '.cs', '.csproj', '.sln', '.suo', '.rs', '.toml', '.lock'];
    if (pathParts.some(p => p.startsWith('.')) || BLOCKED_EXTENSIONS.includes(ext)) {
        res.writeHead(403, { 'Content-Type': 'text/plain; charset=utf-8' });
        res.end('403 Forbidden: Access to sensitive or system files is restricted');
        return;
    }

    fs.stat(filePath, (err, stats) => {
        if (err || !stats.isFile()) {
            // Fallback: serve dashboard.html for SPA-style routing if available (only for non-asset routes)
            if (!ext || ext === '.html') {
                const dashboardFallback = path.join(WEB_DIR, 'dashboard.html');
                if (fs.existsSync(dashboardFallback)) {
                    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
                    fs.createReadStream(dashboardFallback).pipe(res);
                    return;
                }
            }
            res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
            res.end(`404 Not Found: ${reqPath}`);
            return;
        }

        const ext = path.extname(filePath).toLowerCase();
        const contentType = MIME_TYPES[ext] || 'application/octet-stream';

        res.writeHead(200, {
            'Content-Type': contentType,
            'Content-Length': stats.size,
            'Cache-Control': 'no-cache'
        });

        fs.createReadStream(filePath).pipe(res);
    });
});

server.listen(PORT, HOST, () => {
    console.log('================================================================');
    console.log('⚡ WinoxIT Virtual Server & LAN Cockpit Gateway Başlatıldı!');
    console.log('================================================================');
    console.log(`📡 Yerel İstemci (Localhost) : http://localhost:${PORT}/dashboard.html`);
    console.log(`🌐 Dinlenen Port              : ${PORT} (0.0.0.0 - Tüm Ağ Arayüzleri)`);
    console.log('----------------------------------------------------------------');
    console.log('📱 LAN / Mobil / Wi-Fi Ağ Erişim Adresleri:');
    
    const ips = getLocalIpAddresses();
    if (ips.length === 0) {
        console.log(`   http://127.0.0.1:${PORT}/dashboard.html`);
    } else {
        ips.forEach(ip => {
            console.log(`   🔗 [${ip.name}]: http://${ip.address}:${PORT}/dashboard.html`);
        });
    }
    console.log('================================================================');
    console.log('Sunucuyu durdurmak için [CTRL + C] tuşlarına basın.\n');
});
