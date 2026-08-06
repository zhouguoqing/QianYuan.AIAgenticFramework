import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  base: './',
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5050', changeOrigin: true },
      '/hubs': { target: 'http://localhost:5050', changeOrigin: true, ws: true }
    }
  },
  build: { outDir: 'dist', sourcemap: true }
})
