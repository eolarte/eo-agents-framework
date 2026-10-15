import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ command }) => ({
  plugins: [react()],
  base: command === 'build' ? '/customer-ui/' : '/',
  server: {
    proxy: {
      '/api': 'http://localhost:5000',
    },
  },
  build: {
    outDir: 'wwwroot/customer-ui',
    emptyOutDir: true,
  },
}));
