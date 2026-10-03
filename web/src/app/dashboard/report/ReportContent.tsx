'use client'

import { useEffect, useState, useCallback } from 'react'
import { useSearchParams, useRouter } from 'next/navigation'
import Cookies from 'js-cookie'
import apiClient from '@/lib/api'

interface WarningDto {
  message: string
  severity: 'High' | 'Medium' | 'Low' | string
  type: 'Security' | 'Quality' | string
  endpointId?: number
  endpointMethod?: string
  endpointPath?: string
  endpointAiSummary?: string
}

interface TestScenarioDto {
  title: string
  method: string
  path: string
  expectedStatusCode: number
}

interface DashboardReportDto {
  projectId: number
  projectName: string
  toplamEndpointSayisi: number
  kaliteSkoru: number
  warnings: WarningDto[]
  testScenarios: TestScenarioDto[]
}

function ScoreRing({ score }: { score: number }) {
  const radius = 38
  const circumference = 2 * Math.PI * radius
  const progress = Math.min(score / 100, 1) * circumference

  const strokeColor =
    score >= 71 ? 'rgba(74,222,128,0.6)'
    : score >= 41 ? 'rgba(250,204,21,0.6)'
    : 'rgba(248,113,113,0.6)'

  const textColor =
    score >= 71 ? 'text-green-400/70'
    : score >= 41 ? 'text-yellow-400/70'
    : 'text-red-400/70'

  const label = score >= 71 ? 'İyi' : score >= 41 ? 'Orta' : 'Düşük'

  return (
    <div className="relative w-28 h-28 shrink-0">
      <svg className="w-full h-full -rotate-90" viewBox="0 0 100 100">
        <circle cx="50" cy="50" r={radius} fill="none" stroke="rgba(55,65,81,0.5)" strokeWidth="5" />
        <circle
          cx="50" cy="50" r={radius}
          fill="none"
          stroke={strokeColor}
          strokeWidth="5"
          strokeLinecap="round"
          strokeDasharray={`${progress} ${circumference}`}
        />
      </svg>
      <div className="absolute inset-0 flex flex-col items-center justify-center">
        <span className={`text-2xl font-bold leading-none ${textColor}`}>{score}</span>
        <span className={`text-xs font-medium mt-1 ${textColor}`}>{label}</span>
      </div>
    </div>
  )
}

function SeverityBadge({ severity }: { severity: string }) {
  const map: Record<string, string> = {
    High:   'text-red-400/70',
    Medium: 'text-yellow-400/70',
    Low:    'text-gray-500',
  }
  return (
    <span className={`px-2 py-0.5 rounded border border-gray-700 bg-gray-800 text-xs font-medium ${map[severity] ?? 'text-gray-500'}`}>
      {severity}
    </span>
  )
}

function MethodBadge({ method }: { method: string }) {
  const map: Record<string, string> = {
    GET:    'text-green-400/70',
    POST:   'text-blue-400/70',
    PUT:    'text-yellow-400/70',
    PATCH:  'text-purple-400/70',
    DELETE: 'text-red-400/70',
  }
  return (
    <span className={`inline-block px-1.5 py-0.5 rounded border border-gray-700 bg-gray-800 text-xs font-bold font-mono ${map[method.toUpperCase()] ?? 'text-gray-500'}`}>
      {method.toUpperCase()}
    </span>
  )
}

function StatusBadge({ code }: { code: number }) {
  const color = code < 300 ? 'text-green-400' : code < 400 ? 'text-yellow-400' : 'text-red-400'
  return <span className={`text-xs font-mono font-semibold ${color}`}>{code}</span>
}

export default function ReportContent() {
  const searchParams = useSearchParams()
  const id = searchParams.get('id') ?? ''
  const router = useRouter()

  const [report, setReport] = useState<DashboardReportDto | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [loadingEndpointId, setLoadingEndpointId] = useState<number | null>(null)
  const [aiError, setAiError] = useState<string | null>(null)

  interface AiEntry { description: string; method: string; path: string }
  const [generatedDescriptions, setGeneratedDescriptions] = useState<Record<number, AiEntry>>({})

  const storageKey = id ? `ai_desc_project_${id}` : null

  useEffect(() => {
    if (!storageKey) return
    try {
      const stored = localStorage.getItem(storageKey)
      if (stored) setGeneratedDescriptions(JSON.parse(stored))
    } catch {}
  }, [storageKey])

  const fetchReport = useCallback(() => {
    if (!id) { setError('Geçersiz proje ID.'); setLoading(false); return }
    setLoading(true)
    setError(null)
    apiClient
      .get<DashboardReportDto>(`/project/${id}/dashboard`)
      .then(({ data }) => { setReport(data); setLoading(false) })
      .catch((err: unknown) => {
        const status = (err as { response?: { status?: number } })?.response?.status
        if (status === 401) { router.push('/login'); return }
        const msg = (err as { response?: { data?: { message?: string } } })?.response?.data?.message
        setError(msg ?? 'Rapor yüklenirken bir hata oluştu.')
        setLoading(false)
      })
  }, [id, router])

  useEffect(() => { fetchReport() }, [fetchReport])

  function handleLogout() {
    Cookies.remove('token')
    localStorage.removeItem('token')
    router.push('/login')
  }

  function handleDownloadReport() {
    if (!report) return
    const json = JSON.stringify(report, null, 2)
    const blob = new Blob([json], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = `${report.projectName}_AnalizRaporu.json`
    a.click()
    URL.revokeObjectURL(url)
  }

  async function handleGenerateAiDescription(endpointId: number, method: string, path: string) {
    setLoadingEndpointId(endpointId)
    setAiError(null)
    try {
      const { data } = await apiClient.post<{ description: string }>(`/project/${id}/endpoint/${endpointId}/generate-ai-description`)
      if (data.description) {
        setGeneratedDescriptions(prev => {
          const next = { ...prev, [endpointId]: { description: data.description, method, path } }
          if (storageKey) {
            try { localStorage.setItem(storageKey, JSON.stringify(next)) } catch {}
          }
          return next
        })
      }
      fetchReport()
    } catch (err: unknown) {
      const errObj = err as { response?: { data?: { message?: string } }; message?: string }
      setAiError(errObj.response?.data?.message ?? errObj.message ?? 'AI açıklaması oluşturulamadı.')
    } finally {
      setLoadingEndpointId(null)
    }
  }

  const securityWarnings = report?.warnings.filter(w => w.type === 'Security') ?? []
  const qualityWarnings  = report?.warnings.filter(w => w.type === 'Quality') ?? []

  // Sunucuda kayıtlı AI açıklamaları esas alınır; tarayıcı deposundakiler yalnızca yedektir.
  const aiDescriptions: Record<number, AiEntry> = { ...generatedDescriptions }
  for (const w of report?.warnings ?? []) {
    if (w.endpointId !== undefined && w.endpointAiSummary) {
      aiDescriptions[w.endpointId] = {
        description: w.endpointAiSummary,
        method: w.endpointMethod ?? '',
        path: w.endpointPath ?? '',
      }
    }
  }

  return (
    <div className="min-h-screen bg-gray-950 text-gray-100">

      {/* Navbar */}
      <header className="border-b border-gray-800 bg-gray-950 sticky top-0 z-10">
        <div className="max-w-6xl mx-auto px-6 h-14 flex items-center gap-3">
          <button
            onClick={() => router.push('/dashboard')}
            className="flex items-center gap-1.5 text-gray-500 hover:text-gray-200 text-sm transition-colors"
          >
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
            </svg>
            Geri Dön
          </button>

          <span className="text-gray-700">/</span>

          <div className="flex items-center gap-2 min-w-0">
            <div className="w-5 h-5 rounded bg-blue-600 flex items-center justify-center shrink-0">
              <svg className="w-3 h-3 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
              </svg>
            </div>
            <span className="text-gray-200 font-medium text-sm truncate">
              {report?.projectName ?? 'Proje Raporu'}
            </span>
          </div>

          <div className="ml-auto flex items-center gap-3">
            <button
              onClick={handleDownloadReport}
              disabled={!report}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-md bg-gray-800 hover:bg-gray-700 border border-gray-700 text-gray-300 hover:text-gray-100 text-sm font-medium disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
            >
              <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
              </svg>
              Raporu İndir
            </button>
            <span className="text-gray-800">|</span>
            <button
              onClick={handleLogout}
              className="flex items-center gap-1.5 text-gray-500 hover:text-gray-200 text-sm transition-colors"
            >
              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
              </svg>
              Çıkış Yap
            </button>
          </div>
        </div>
      </header>

      <main className="max-w-6xl mx-auto px-6 py-8">

        {/* Yükleniyor */}
        {loading && (
          <div className="flex flex-col items-center justify-center py-40 gap-3">
            <svg className="animate-spin w-7 h-7 text-blue-500" fill="none" viewBox="0 0 24 24">
              <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
              <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
            </svg>
            <span className="text-gray-500 text-sm">Rapor yükleniyor…</span>
          </div>
        )}

        {/* Hata */}
        {!loading && error && (
          <div className="flex flex-col items-center justify-center py-40 gap-4 text-center">
            <div className="w-10 h-10 rounded-lg bg-red-500/10 border border-red-800 flex items-center justify-center">
              <svg className="w-5 h-5 text-red-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
            </div>
            <p className="text-gray-300 text-sm font-medium">{error}</p>
            <button
              onClick={() => router.push('/dashboard')}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-md bg-gray-800 hover:bg-gray-700 border border-gray-700 text-gray-300 text-sm transition-colors"
            >
              <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
              </svg>
              Geri Dön
            </button>
          </div>
        )}

        {/* Rapor */}
        {!loading && report && (
          <div className="space-y-6">

            {/* Özet Kartı */}
            <div className="bg-gray-900 border border-gray-800 rounded-lg p-6 flex flex-col sm:flex-row items-start sm:items-center gap-6">
              <ScoreRing score={report.kaliteSkoru} />
              <div className="flex-1">
                <h1 className="text-xl font-semibold text-gray-100">{report.projectName}</h1>
                <p className="text-gray-600 text-xs mt-0.5">Proje ID: {report.projectId}</p>
                <div className="flex flex-wrap gap-4 mt-4">
                  <div className="flex items-center gap-1.5">
                    <span className="w-1.5 h-1.5 rounded-full bg-blue-500" />
                    <span className="text-gray-400 text-sm"><span className="text-gray-100 font-medium">{report.toplamEndpointSayisi}</span> endpoint</span>
                  </div>
                  <div className="flex items-center gap-1.5">
                    <span className="w-1.5 h-1.5 rounded-full bg-red-500" />
                    <span className="text-gray-400 text-sm"><span className="text-gray-100 font-medium">{securityWarnings.length}</span> güvenlik uyarısı</span>
                  </div>
                  <div className="flex items-center gap-1.5">
                    <span className="w-1.5 h-1.5 rounded-full bg-yellow-500" />
                    <span className="text-gray-400 text-sm"><span className="text-gray-100 font-medium">{qualityWarnings.length}</span> kalite uyarısı</span>
                  </div>
                  <div className="flex items-center gap-1.5">
                    <span className="w-1.5 h-1.5 rounded-full bg-green-500" />
                    <span className="text-gray-400 text-sm"><span className="text-gray-100 font-medium">{report.testScenarios.length}</span> test senaryosu</span>
                  </div>
                </div>
              </div>
            </div>

            {/* Uyarılar */}
            <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">

              {/* Güvenlik Uyarıları */}
              <div className="bg-gray-900 border border-gray-800 rounded-lg p-5">
                <div className="flex items-center justify-between mb-4">
                  <div className="flex items-center gap-2">
                    <svg className="w-4 h-4 text-red-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                    </svg>
                    <h2 className="text-gray-100 font-medium text-sm">Güvenlik Uyarıları</h2>
                  </div>
                  <span className="px-2 py-0.5 rounded bg-red-500/10 text-red-400 text-xs font-medium border border-red-800">
                    {securityWarnings.length}
                  </span>
                </div>

                {securityWarnings.length === 0 ? (
                  <div className="flex items-center gap-2 py-6 justify-center">
                    <svg className="w-4 h-4 text-green-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
                    </svg>
                    <p className="text-gray-500 text-sm">Güvenlik uyarısı bulunamadı.</p>
                  </div>
                ) : (
                  <ul className="space-y-2">
                    {securityWarnings.map((w, i) => (
                      <li key={i} className="bg-gray-950 border border-gray-800 rounded-md p-3">
                        <p className="text-gray-300 text-sm leading-relaxed">{w.message}</p>
                        <div className="flex flex-wrap items-center gap-2 mt-2">
                          <SeverityBadge severity={w.severity} />
                          {w.endpointMethod && w.endpointPath && (
                            <div className="flex items-center gap-1.5">
                              <MethodBadge method={w.endpointMethod} />
                              <span className="text-gray-500 text-xs font-mono">{w.endpointPath}</span>
                            </div>
                          )}
                        </div>
                      </li>
                    ))}
                  </ul>
                )}
              </div>

              {/* Kalite Uyarıları */}
              <div className="bg-gray-900 border border-gray-800 rounded-lg p-5">
                <div className="flex items-center justify-between mb-4">
                  <div className="flex items-center gap-2">
                    <svg className="w-4 h-4 text-yellow-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                    <h2 className="text-gray-100 font-medium text-sm">Kalite Uyarıları</h2>
                  </div>
                  <span className="px-2 py-0.5 rounded bg-yellow-500/10 text-yellow-400 text-xs font-medium border border-yellow-800">
                    {qualityWarnings.length}
                  </span>
                </div>

                {aiError && (
                  <div className="flex items-start gap-2 mb-3 p-3 rounded-md bg-red-500/10 border border-red-800 text-red-400 text-xs">
                    <svg className="w-3.5 h-3.5 shrink-0 mt-0.5" fill="currentColor" viewBox="0 0 20 20">
                      <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
                    </svg>
                    <span className="flex-1">{aiError}</span>
                    <button onClick={() => setAiError(null)} className="text-gray-600 hover:text-gray-400 transition-colors">
                      <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                      </svg>
                    </button>
                  </div>
                )}

                {qualityWarnings.length === 0 ? (
                  <div className="flex items-center gap-2 py-6 justify-center">
                    <svg className="w-4 h-4 text-green-500" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
                    </svg>
                    <p className="text-gray-500 text-sm">Kalite uyarısı bulunamadı.</p>
                  </div>
                ) : (
                  <ul className="space-y-2">
                    {qualityWarnings.map((w, i) => {
                      const isMissingDesc = w.message === 'Endpoint açıklaması eksik'
                      const isGenerating  = loadingEndpointId === w.endpointId
                      return (
                        <li key={i} className="bg-gray-950 border border-gray-800 rounded-md p-3">
                          <p className="text-gray-300 text-sm leading-relaxed">{w.message}</p>
                          <div className="flex flex-wrap items-center gap-2 mt-2">
                            <SeverityBadge severity={w.severity} />
                            {w.endpointMethod && w.endpointPath && (
                              <div className="flex items-center gap-1.5">
                                <MethodBadge method={w.endpointMethod} />
                                <span className="text-gray-500 text-xs font-mono">{w.endpointPath}</span>
                              </div>
                            )}
                            {isMissingDesc && w.endpointId !== undefined && (
                              <button
                                onClick={() => handleGenerateAiDescription(w.endpointId!, w.endpointMethod ?? '', w.endpointPath ?? '')}
                                disabled={isGenerating}
                                className="flex items-center gap-1 px-2 py-0.5 rounded text-xs font-medium
                                  bg-gray-800 hover:bg-gray-700 border border-gray-700 hover:border-gray-600
                                  text-gray-300 hover:text-gray-100
                                  disabled:opacity-50 disabled:cursor-not-allowed
                                  transition-colors"
                              >
                                {isGenerating ? (
                                  <>
                                    <svg className="animate-spin w-3 h-3" fill="none" viewBox="0 0 24 24">
                                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
                                    </svg>
                                    Üretiliyor…
                                  </>
                                ) : (
                                  'AI ile Açıklama Üret'
                                )}
                              </button>
                            )}
                          </div>
                        </li>
                      )
                    })}
                  </ul>
                )}
              </div>
            </div>

            {/* AI Açıklamaları */}
            {Object.keys(aiDescriptions).length > 0 && (
              <div className="bg-gray-900 border border-gray-800 rounded-lg p-5">
                <div className="flex items-center justify-between mb-4">
                  <div className="flex items-center gap-2">
                    <svg className="w-4 h-4 text-blue-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9.663 17h4.673M12 3v1m6.364 1.636l-.707.707M21 12h-1M4 12H3m3.343-5.657l-.707-.707m2.828 9.9a5 5 0 117.072 0l-.548.547A3.374 3.374 0 0014 18.469V19a2 2 0 11-4 0v-.531c0-.895-.356-1.754-.988-2.386l-.548-.547z" />
                    </svg>
                    <h2 className="text-gray-100 font-medium text-sm">AI Açıklamaları</h2>
                  </div>
                  <span className="px-2 py-0.5 rounded bg-blue-500/10 text-blue-400 text-xs font-medium border border-blue-800">
                    {Object.keys(aiDescriptions).length}
                  </span>
                </div>
                <ul className="space-y-2">
                  {Object.entries(aiDescriptions).map(([endpointId, entry]) => (
                    <li key={endpointId} className="bg-gray-950 border border-gray-800 rounded-md p-3">
                      <div className="flex items-center gap-2 mb-1.5">
                        {entry.method && <MethodBadge method={entry.method} />}
                        {entry.path && <span className="text-gray-400 text-xs font-mono">{entry.path}</span>}
                        <span className="px-1.5 py-0.5 rounded bg-blue-500/10 text-blue-400 text-xs border border-blue-800 ml-auto">AI</span>
                      </div>
                      <p className="text-gray-300 text-sm leading-relaxed">{entry.description}</p>
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {/* Test Senaryoları */}
            <div className="bg-gray-900 border border-gray-800 rounded-lg p-5">
              <div className="flex items-center justify-between mb-4">
                <div className="flex items-center gap-2">
                  <svg className="w-4 h-4 text-green-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-6 9l2 2 4-4" />
                  </svg>
                  <h2 className="text-gray-100 font-medium text-sm">Test Senaryoları</h2>
                </div>
                <span className="px-2 py-0.5 rounded bg-green-500/10 text-green-400 text-xs font-medium border border-green-800">
                  {report.testScenarios.length}
                </span>
              </div>

              {report.testScenarios.length === 0 ? (
                <p className="text-gray-500 text-sm text-center py-6">Henüz test senaryosu oluşturulmadı.</p>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-sm">
                    <thead>
                      <tr className="border-b border-gray-800">
                        <th className="text-left text-gray-500 font-medium text-xs pb-2.5 pr-4">Metot</th>
                        <th className="text-left text-gray-500 font-medium text-xs pb-2.5 pr-4">Path</th>
                        <th className="text-left text-gray-500 font-medium text-xs pb-2.5 pr-4">Başlık</th>
                        <th className="text-left text-gray-500 font-medium text-xs pb-2.5">Beklenen</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-800">
                      {report.testScenarios.map((ts, i) => (
                        <tr key={i} className="hover:bg-gray-800/40 transition-colors">
                          <td className="py-2.5 pr-4"><MethodBadge method={ts.method} /></td>
                          <td className="py-2.5 pr-4"><span className="font-mono text-xs text-gray-400">{ts.path}</span></td>
                          <td className="py-2.5 pr-4"><span className="text-gray-300 text-xs">{ts.title}</span></td>
                          <td className="py-2.5"><StatusBadge code={ts.expectedStatusCode} /></td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

          </div>
        )}
      </main>
    </div>
  )
}
