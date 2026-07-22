import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  base: '/',
  build: { outDir: 'dist', emptyOutDir: true, assetsInlineLimit: 4096 },
  server: { proxy: { '/api': 'http://127.0.0.1:16888', '/hub': { target: 'http://127.0.0.1:16888', ws: true } } }
})

