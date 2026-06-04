import { useState } from 'react';
import { useLanguage } from '../context/LanguageContext';
import { Logo } from './Logo';
import { login } from '../services/authService';

interface LoginScreenProps {
  onLogin: () => void;
}

export function LoginScreen({ onLogin }: LoginScreenProps) {
  const { language, setLanguage, t } = useLanguage();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');

  const handleLogin = async () => {
    if (!username.trim() || !password.trim()) return;
    try {
      await login(username.trim(), password);
      onLogin();
    } catch (err) {
      console.error(err);
    }
  };

  return (
    <div className="min-h-screen bg-neutral-50 flex flex-col">
      {/* Language Toggle */}
      <div className="p-4 flex justify-end">
        <div className="inline-flex bg-white border border-neutral-300 rounded-lg overflow-hidden">
          <button
            onClick={() => setLanguage('en')}
            className={`px-4 py-2 text-sm font-medium ${
              language === 'en'
                ? 'bg-neutral-800 text-white'
                : 'bg-white text-neutral-700 hover:bg-neutral-50'
            }`}
          >
            EN
          </button>
          <button
            onClick={() => setLanguage('he')}
            className={`px-4 py-2 text-sm font-medium ${
              language === 'he'
                ? 'bg-neutral-800 text-white'
                : 'bg-white text-neutral-700 hover:bg-neutral-50'
            }`}
          >
            HE
          </button>
          <button
            onClick={() => setLanguage('th')}
            className={`px-4 py-2 text-sm font-medium ${
              language === 'th'
                ? 'bg-neutral-800 text-white'
                : 'bg-white text-neutral-700 hover:bg-neutral-50'
            }`}
          >
            TH
          </button>
        </div>
      </div>

      {/* Login Form */}
      <div className="flex-1 flex items-center justify-center px-6">
        <div className="w-full max-w-sm">
          {/* Logo Placeholder */}
          
          <div className="absolute inset-0 flex items-center justify-center pointer-events-none overflow-hidden">
            <img
              src="/mt-logo-clear.svg"
              alt="background logo"

            />
          </div>

          <div className="mb-8 text-center">
            <div className="flex justify-center mb-4">
              <Logo size="md" isHome={true} />
            </div>
          </div>

          {/* Username Input */}
          <div className="space-y-4">
            <div>
              <label className="block text-sm font-medium text-neutral-700 mb-2">
                {t('login.username')}
              </label>
              <input
                type="text"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                onKeyPress={(e) => e.key === 'Enter' && handleLogin()}
                className="w-full px-4 py-3 bg-white border border-neutral-300 rounded-lg text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 focus:border-transparent"
                placeholder={t('login.username')}
              />
            </div>

            <div>
              <label className="block text-sm font-medium text-neutral-700 mb-2">
                {t('login.password')}
              </label>
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                onKeyPress={(e) => e.key === 'Enter' && handleLogin()}
                className="w-full px-4 py-3 bg-white border border-neutral-300 rounded-lg text-neutral-900 placeholder:text-neutral-400 focus:outline-none focus:ring-2 focus:ring-neutral-800 focus:border-transparent"
                placeholder={t('login.password')}
              />
            </div>

            {/* Login Button */}
            <button
              onClick={handleLogin}
              className="w-full py-3 bg-neutral-800 text-white font-medium rounded-lg hover:bg-neutral-900 active:bg-neutral-950 transition-colors"
            >
              {t('login.login')}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}