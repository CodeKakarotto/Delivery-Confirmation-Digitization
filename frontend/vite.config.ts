/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    // During local development, forward /api calls to the ASP.NET Core
    // backend (http profile in launchSettings.json). Production uses
    // VITE_API_BASE_URL instead (set in Step 10).
    proxy: {
      '/api': 'http://localhost:5170',
    },
  },
  test: {
    environment: 'jsdom',
  },
})
