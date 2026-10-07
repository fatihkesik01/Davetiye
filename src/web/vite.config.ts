import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [react()],
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

