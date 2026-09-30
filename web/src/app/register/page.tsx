'use client'

import { useState, FormEvent } from 'react'
import { useRouter } from 'next/navigation'
import Link from 'next/link'
import apiClient from '@/lib/api'

export default function RegisterPage() {
  const router = useRouter()
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      await apiClient.post('/auth/register', { name, email, password })
      router.push('/login?registered=true')
    } catch (err: unknown) {
      const axiosErr = err as { response?: { status?: number; data?: { message?: string } }; message?: string }
      const message =
        axiosErr?.response?.data?.message ??
        (axiosErr?.response?.status ? `Sunucu hatası: ${axiosErr.response.status}` : null) ??
        axiosErr?.message ??
        'Kayıt sırasında bir hata oluştu. Lütfen tekrar deneyin.'
      setError(message)
    } finally {
      setLoading(false)
    }
  }

  return (
    <main className="min-h-screen bg-gray-950 flex items-center justify-center p-4">
      <div className="w-full max-w-sm">

        {/* Logo */}
        <div className="flex flex-col items-center mb-8">
          <div className="w-10 h-10 rounded-lg bg-blue-600 flex items-center justify-center mb-4">
            <svg className="w-5 h-5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
            </svg>
          </div>
          <h1 className="text-xl font-semibold text-gray-100">API Insight Studio</h1>
          <p className="text-gray-500 text-sm mt-1">Yeni hesap oluşturun</p>
        </div>

        {/* Kart */}
        <div className="bg-gray-900 border border-gray-800 rounded-lg p-6">
          <form onSubmit={handleSubmit} noValidate className="space-y-4">

            <div>
              <label htmlFor="name" className="block text-xs font-medium text-gray-400 mb-1.5">
                Ad Soyad
              </label>
              <input
                id="name"
                type="text"
                autoComplete="name"
                required
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="Adınız Soyadınız"
                className="w-full px-3 py-2 rounded-md bg-gray-950 border border-gray-700 text-gray-100 placeholder-gray-600 text-sm focus:outline-none focus:ring-1 focus:ring-blue-600 focus:border-blue-600 transition"
              />
            </div>

            <div>
              <label htmlFor="email" className="block text-xs font-medium text-gray-400 mb-1.5">
                Email
              </label>
              <input
                id="email"
                type="email"
                autoComplete="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="ornek@sirket.com"
                className="w-full px-3 py-2 rounded-md bg-gray-950 border border-gray-700 text-gray-100 placeholder-gray-600 text-sm focus:outline-none focus:ring-1 focus:ring-blue-600 focus:border-blue-600 transition"
              />
            </div>

            <div>
              <label htmlFor="password" className="block text-xs font-medium text-gray-400 mb-1.5">
                Şifre
              </label>
              <input
                id="password"
                type="password"
                autoComplete="new-password"
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="••••••••"
                className="w-full px-3 py-2 rounded-md bg-gray-950 border border-gray-700 text-gray-100 placeholder-gray-600 text-sm focus:outline-none focus:ring-1 focus:ring-blue-600 focus:border-blue-600 transition"
              />
            </div>

            {error && (
              <div className="flex items-start gap-2 bg-red-500/10 border border-red-800 text-red-400 rounded-md px-3 py-2.5 text-sm">
                <svg className="w-4 h-4 mt-0.5 shrink-0" fill="currentColor" viewBox="0 0 20 20">
                  <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                </svg>
                <span>{error}</span>
              </div>
            )}

            <button
              type="submit"
              disabled={loading}
              className="w-full flex items-center justify-center gap-2 py-2 px-4 rounded-md bg-blue-600 hover:bg-blue-500 disabled:opacity-50 disabled:cursor-not-allowed text-white font-semibold text-sm transition-colors mt-2"
            >
              {loading ? (
                <>
                  <svg className="animate-spin w-4 h-4" fill="none" viewBox="0 0 24 24">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
                  </svg>
                  Kayıt yapılıyor…
                </>
              ) : 'Kayıt Ol'}
            </button>
          </form>

          <div className="border-t border-gray-800 mt-5 pt-5">
            <p className="text-center text-gray-500 text-sm">
              Zaten hesabınız var mı?{' '}
              <Link href="/login" className="text-blue-400 hover:text-blue-300 font-medium transition-colors">
                Giriş yapın
              </Link>
            </p>
          </div>
        </div>

        <p className="text-center text-gray-700 text-xs mt-5">
          © {new Date().getFullYear()} API Insight Studio
        </p>
      </div>
    </main>
  )
}
