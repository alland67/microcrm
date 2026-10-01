/// <reference types="vitest/config" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

const apiTarget = process.env.API_URL ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: { '/api': { target: apiTarget, changeOrigin: true } },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    restoreMocks: true,
  },
})
