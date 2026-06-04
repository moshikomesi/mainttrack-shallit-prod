
import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react-swc';
import path from 'path';
import { securityHeadersPlugin } from './vite-security-headers';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, __dirname, '');
  const apiUrl = env.VITE_API_URL ?? '';

  return {
    plugins: [react(), securityHeadersPlugin(apiUrl, mode)],
    resolve: {
      extensions: ['.js', '.jsx', '.ts', '.tsx', '.json'],
      alias: {
        '@': path.resolve(__dirname, './src'),
      },
    },
    build: {
      target: 'esnext',
      outDir: 'build',
    },
    server: {
      port: 3000,
      open: true,
      proxy: {
        '/api': {
          target: 'http://localhost:5062',
          changeOrigin: true,
        },
        '/health': {
          target: 'http://localhost:5062',
          changeOrigin: true,
        },
      },
    },
  };
});
