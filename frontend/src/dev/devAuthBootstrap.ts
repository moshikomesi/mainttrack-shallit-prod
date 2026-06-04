import { isAuthenticated } from '../auth/authSession';
import { login } from '../services/authService';

export async function initDevAuth(): Promise<void> {
  if (import.meta.env.PROD) {
    return;
  }
  if (isAuthenticated()) {
    return;
  }
  try {
    await login('admin', 'admin');
  } catch (err) {
    console.error('Dev auto-login failed:', err);
  }
}
