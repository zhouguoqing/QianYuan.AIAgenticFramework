import fs from 'node:fs';
import http from 'node:http';
import path from 'node:path';
const contentTypes = {
    '.html': 'text/html; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.svg': 'image/svg+xml',
    '.png': 'image/png',
    '.jpg': 'image/jpeg',
    '.jpeg': 'image/jpeg',
    '.webp': 'image/webp',
    '.ico': 'image/x-icon',
};
export async function startStaticServer(root, port) {
    const server = http.createServer((req, res) => serveFile(root, req.url ?? '/', res));
    await new Promise((resolve, reject) => {
        server.once('error', reject);
        server.listen(port, '127.0.0.1', () => resolve());
    });
    return {
        url: `http://127.0.0.1:${port}`,
        stop: () => closeServer(server),
    };
}
function serveFile(root, requestUrl, res) {
    const url = new URL(requestUrl, 'http://127.0.0.1');
    const decoded = decodeURIComponent(url.pathname);
    const normalized = path.normalize(decoded).replace(/^[/\\]+/, '');
    let filePath = path.join(root, normalized);
    if (!filePath.startsWith(root)) {
        res.writeHead(403).end('Forbidden');
        return;
    }
    if (!fs.existsSync(filePath) || fs.statSync(filePath).isDirectory())
        filePath = path.join(root, 'index.html');
    const ext = path.extname(filePath).toLowerCase();
    res.writeHead(200, { 'Content-Type': contentTypes[ext] ?? 'application/octet-stream' });
    fs.createReadStream(filePath).pipe(res);
}
async function closeServer(server) {
    await new Promise(resolve => server.close(() => resolve()));
}
