import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
// `BASE_PATH` lets the portal be served from a sub-path (for example the
// GitHub Pages project site at /Nakshatra/); it defaults to the site root.
export default defineConfig({
  base: process.env.BASE_PATH ?? '/',
  plugins: [react()],
})
