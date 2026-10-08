import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

const apiProxyTarget = process.env.DAVETIYE_API_PROXY_TARGET

export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    ...(apiProxyTarget
      ? {
          proxy: {
            '/api': {
              target: apiProxyTarget,
              changeOrigin: true,
              secure: true,
            },
          },
        }
      : {}),
  },
  test: {
    environment: 'jsdom',
    setupFiles: './src/test/setup.ts',
    css: true,
    // Date assertions are written for the product's primary market time zone; pin it so CI (UTC) matches.
    env: { TZ: 'Europe/Istanbul' },
    // Must stay above the Testing Library asyncUtilTimeout set in src/test/setup.ts.
    testTimeout: 20000,
  },
})
