# WorkPartner Desktop

WorkPartner Desktop is the Electron shell for the existing QianYuan Web UI and API.

## Development

Start the Web UI in one terminal:

```bash
cd src/QianYuan.Web
npm run dev -- --host 127.0.0.1
```

Start the desktop shell in another terminal:

```bash
cd src/QianYuan.Desktop
npm install
npm run dev
```

The Electron main process starts `QianYuan.Api` on `http://127.0.0.1:5050` by default and opens the renderer at `http://127.0.0.1:5173`.

Useful environment variables:

- `WORKPARTNER_API_PORT`: API port, default `5050`.
- `WORKPARTNER_RENDERER_URL`: renderer URL for development, default `http://127.0.0.1:5173`.
- `WORKPARTNER_RENDERER_PORT`: packaged local static server port, default `5180`.

## Packaging

Build the Web UI first:

```bash
cd src/QianYuan.Web
npm run build
```

Then package the desktop shell:

```bash
cd src/QianYuan.Desktop
npm run pack
```

`electron-builder.yml` is configured for macOS DMG and Windows NSIS targets. The first packaged MVP includes the Web `dist` files as resources. Bundling a self-contained published `QianYuan.Api` binary under `resources/api` is the next packaging step.