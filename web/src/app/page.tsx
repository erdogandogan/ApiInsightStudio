import Link from 'next/link'

export default function HomePage() {
  return (
    <main className="min-h-screen bg-gradient-to-br from-slate-900 via-slate-800 to-slate-900 flex flex-col items-center justify-center p-6 text-center">
      {/* İkon */}
      <div className="inline-flex items-center justify-center w-20 h-20 rounded-3xl bg-indigo-600 shadow-2xl shadow-indigo-500/30 mb-8">
        <svg
          className="w-10 h-10 text-white"
          fill="none"
          stroke="currentColor"
          viewBox="0 0 24 24"
        >
          <path
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth={2}
            d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z"
          />
        </svg>
      </div>

      {/* Başlık */}
      <h1 className="text-5xl sm:text-6xl font-extrabold text-white tracking-tight leading-tight max-w-2xl">
        API Insight{' '}
        <span className="text-indigo-400">Studio</span>
      </h1>

      {/* Açıklama */}
      <p className="mt-6 text-lg sm:text-xl text-slate-400 max-w-xl leading-relaxed">
        API'larınızı keşfedin, test edin ve belgeleyin. Geliştirici araçlarını
        tek bir modern arayüzde birleştiren akıllı çalışma alanı.
      </p>

      {/* Özellik etiketleri */}
      <div className="flex flex-wrap items-center justify-center gap-2 mt-8">
        {['REST API', 'JWT Auth', 'Gerçek Zamanlı', 'Takım Çalışması'].map((tag) => (
          <span
            key={tag}
            className="px-3 py-1 rounded-full text-xs font-medium bg-slate-700/60 border border-slate-600/50 text-slate-300"
          >
            {tag}
          </span>
        ))}
      </div>

      {/* CTA Butonları */}
      <div className="flex flex-col sm:flex-row items-center gap-4 mt-12">
        <Link
          href="/register"
          className="w-48 flex items-center justify-center gap-2 py-3 px-6 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white font-semibold text-sm transition-colors shadow-lg shadow-indigo-500/25"
        >
          <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M18 9v3m0 0v3m0-3h3m-3 0h-3m-2-5a4 4 0 11-8 0 4 4 0 018 0zM3 20a6 6 0 0112 0v1H3v-1z" />
          </svg>
          Kayıt Ol
        </Link>

        <Link
          href="/login"
          className="w-48 flex items-center justify-center gap-2 py-3 px-6 rounded-xl bg-slate-700/60 hover:bg-slate-700 border border-slate-600/50 hover:border-slate-500 text-slate-200 font-semibold text-sm transition-colors"
        >
          <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M11 16l-4-4m0 0l4-4m-4 4h14m-5 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h7a3 3 0 013 3v1" />
          </svg>
          Giriş Yap
        </Link>
      </div>

      {/* Footer */}
      <p className="absolute bottom-6 text-slate-600 text-xs">
        © {new Date().getFullYear()} API Insight Studio
      </p>
    </main>
  )
}
