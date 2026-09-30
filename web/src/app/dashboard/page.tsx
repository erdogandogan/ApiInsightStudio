'use client'

import { useEffect, useState, useCallback, useRef } from 'react'
import { useRouter } from 'next/navigation'
import Link from 'next/link'
import Cookies from 'js-cookie'
import apiClient from '@/lib/api'

interface Project {
  id: string | number
  name: string
  description?: string
  createdAt?: string
  endpointCount?: number
}

type FetchState = 'loading' | 'success' | 'error'

interface Toast {
  type: 'success' | 'error'
  message: string
}

export default function DashboardPage() {
  const router = useRouter()

  const [projects, setProjects] = useState<Project[]>([])
  const [fetchState, setFetchState] = useState<FetchState>('loading')
  const [fetchError, setFetchError] = useState<string | null>(null)

  const [swaggerJson, setSwaggerJson] = useState('')
  const [uploading, setUploading] = useState(false)
  const [uploadError, setUploadError] = useState<string | null>(null)
  const [selectedFileName, setSelectedFileName] = useState<string | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const [selectMode, setSelectMode] = useState(false)
  const [selectedIds, setSelectedIds] = useState<Set<string | number>>(new Set())
  const [deleting, setDeleting] = useState(false)

  const [toast, setToast] = useState<Toast | null>(null)
  const toastTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  function showToast(type: Toast['type'], message: string) {
    if (toastTimer.current) clearTimeout(toastTimer.current)
    setToast({ type, message })
    toastTimer.current = setTimeout(() => setToast(null), 4000)
  }

  const fetchProjects = useCallback(() => {
    setFetchState('loading')
    setFetchError(null)
    apiClient
      .get<Project[]>('/project')
      .then(({ data }) => {
        setProjects(data)
        setFetchState('success')
      })
      .catch((err: unknown) => {
        const axiosErr = err as { response?: { status?: number; data?: unknown }; message?: string }
        console.error('Proje çekme hatası:', axiosErr.response?.data || axiosErr.message)
        const status = axiosErr.response?.status
        if (status === 401) { router.push('/login'); return }
        setFetchError('Projeler yüklenirken bir hata oluştu.')
        setFetchState('error')
      })
  }, [router])

  useEffect(() => { fetchProjects() }, [fetchProjects])

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (!file) return
    setSelectedFileName(file.name)
    setUploadError(null)
    const reader = new FileReader()
    reader.onload = (event) => {
      setSwaggerJson((event.target?.result as string) ?? '')
    }
    reader.onerror = () => setUploadError('Dosya okunurken bir hata oluştu.')
    reader.readAsText(file, 'UTF-8')
    e.target.value = ''
  }

  async function handleUpload() {
    setUploadError(null)
    if (!swaggerJson.trim()) {
      setUploadError('Lütfen bir Swagger JSON metni girin.')
      return
    }
    let parsed: unknown
    try {
      parsed = JSON.parse(swaggerJson)
    } catch {
      setUploadError('Geçersiz JSON formatı. Lütfen kontrol edin.')
      return
    }
    setUploading(true)
    try {
      await apiClient.post('/project/upload', { openApiContent: swaggerJson })
      setSwaggerJson('')
      setSelectedFileName(null)
      showToast('success', 'Proje başarıyla analiz edildi.')
      fetchProjects()
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { message?: string } } })?.response?.data?.message ??
        'Analiz sırasında bir hata oluştu.'
      setUploadError(msg)
      showToast('error', msg)
    } finally {
      setUploading(false)
    }
  }

  function toggleSelectMode() {
    setSelectMode(v => !v)
    setSelectedIds(new Set())
  }

  function toggleSelect(id: string | number) {
    setSelectedIds(prev => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  async function handleDeleteSelected() {
    if (selectedIds.size === 0) return
    setDeleting(true)
    try {
      await Promise.all(
        Array.from(selectedIds).map(id => apiClient.delete(`/project/${id}`))
      )
      showToast('success', `${selectedIds.size} proje silindi.`)
      setSelectMode(false)
      setSelectedIds(new Set())
      fetchProjects()
    } catch {
      showToast('error', 'Silme sırasında bir hata oluştu.')
    } finally {
      setDeleting(false)
    }
  }

  function handleLogout() {
    Cookies.remove('token')
    localStorage.removeItem('token')
    router.push('/login')
  }

  return (
    <div className="min-h-screen bg-gray-950 text-gray-100">

      {/* Navbar */}
      <header className="border-b border-gray-800 bg-gray-950 sticky top-0 z-10">
        <div className="max-w-6xl mx-auto px-6 h-14 flex items-center justify-between">
          <div className="flex items-center gap-2.5">
            <div className="w-7 h-7 rounded-md bg-blue-600 flex items-center justify-center">
              <svg className="w-4 h-4 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
              </svg>
            </div>
            <span className="text-gray-100 font-semibold text-sm">API Insight Studio</span>
          </div>
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
      </header>

      {/* Toast */}
      {toast && (
        <div className={`fixed bottom-6 right-6 z-50 flex items-center gap-3 px-4 py-3 rounded-lg border text-sm font-medium
          ${toast.type === 'success'
            ? 'bg-gray-900 border-green-700/60 text-green-400'
            : 'bg-gray-900 border-red-700/60 text-red-400'
          }`}
        >
          {toast.type === 'success' ? (
            <svg className="w-4 h-4 shrink-0" fill="currentColor" viewBox="0 0 20 20">
              <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
            </svg>
          ) : (
            <svg className="w-4 h-4 shrink-0" fill="currentColor" viewBox="0 0 20 20">
              <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
            </svg>
          )}
          {toast.message}
          <button onClick={() => setToast(null)} className="ml-1 text-gray-500 hover:text-gray-300 transition-colors">
            <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
      )}

      <main className="max-w-6xl mx-auto px-6 py-8 space-y-8">

        {/* ── Yeni API Analizi ── */}
        <section className="bg-gray-900 border border-gray-800 rounded-lg p-6">
          <div className="flex items-center gap-3 mb-5">
            <svg className="w-4 h-4 text-gray-400 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M7 16a4 4 0 01-.88-7.903A5 5 0 1115.9 6L16 6a5 5 0 011 9.9M15 13l-3-3m0 0l-3 3m3-3v12" />
            </svg>
            <div>
              <h2 className="text-gray-100 font-semibold text-sm">Yeni API Analizi</h2>
              <p className="text-gray-500 text-xs mt-0.5">Swagger / OpenAPI JSON içeriğini yapıştırın veya dosya seçin.</p>
            </div>
          </div>

          <div className="flex items-center gap-3 mb-3">
            <input
              ref={fileInputRef}
              type="file"
              accept=".json,application/json"
              className="hidden"
              onChange={handleFileChange}
            />
            <button
              type="button"
              onClick={() => fileInputRef.current?.click()}
              className="flex items-center gap-2 px-3 py-1.5 rounded-md bg-gray-800 hover:bg-gray-700 border border-gray-700 hover:border-gray-600 text-gray-300 hover:text-gray-100 text-sm font-medium transition-colors shrink-0"
            >
              <svg className="w-3.5 h-3.5 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15.172 7l-6.586 6.586a2 2 0 102.828 2.828l6.414-6.586a4 4 0 00-5.656-5.656l-6.415 6.585a6 6 0 108.486 8.486L20.5 13" />
              </svg>
              Dosya Seç (.json)
            </button>
            {selectedFileName ? (
              <div className="flex items-center gap-2 min-w-0">
                <svg className="w-3.5 h-3.5 text-green-500 shrink-0" fill="currentColor" viewBox="0 0 20 20">
                  <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zm3.707-9.293a1 1 0 00-1.414-1.414L9 10.586 7.707 9.293a1 1 0 00-1.414 1.414l2 2a1 1 0 001.414 0l4-4z" clipRule="evenodd" />
                </svg>
                <span className="text-green-400 text-sm truncate">{selectedFileName}</span>
                <button
                  type="button"
                  onClick={() => { setSelectedFileName(null); setSwaggerJson(''); setUploadError(null) }}
                  className="text-gray-600 hover:text-gray-400 transition-colors shrink-0"
                >
                  <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </div>
            ) : (
              <span className="text-gray-600 text-xs">veya aşağıya yapıştırın</span>
            )}
          </div>

          <textarea
            value={swaggerJson}
            onChange={(e) => { setSwaggerJson(e.target.value); setUploadError(null); setSelectedFileName(null) }}
            placeholder='{ "openapi": "3.0.0", "info": { ... }, "paths": { ... } }'
            rows={7}
            spellCheck={false}
            className="w-full px-3 py-2.5 rounded-md bg-gray-950 border border-gray-700 text-gray-200 placeholder-gray-700 text-xs font-mono leading-relaxed resize-y focus:outline-none focus:ring-1 focus:ring-blue-600 focus:border-blue-600 transition"
          />

          {uploadError && (
            <p className="flex items-center gap-1.5 mt-2 text-red-400 text-xs">
              <svg className="w-3.5 h-3.5 shrink-0" fill="currentColor" viewBox="0 0 20 20">
                <path fillRule="evenodd" d="M18 10a8 8 0 11-16 0 8 8 0 0116 0zm-7 4a1 1 0 11-2 0 1 1 0 012 0zm-1-9a1 1 0 00-1 1v4a1 1 0 102 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
              </svg>
              {uploadError}
            </p>
          )}

          <div className="flex items-center justify-between mt-4">
            <p className="text-gray-600 text-xs">OpenAPI 2.0 / 3.0 (JSON)</p>
            <button
              onClick={handleUpload}
              disabled={uploading}
              className="flex items-center gap-2 px-4 py-2 rounded-md bg-blue-600 hover:bg-blue-500 disabled:bg-blue-600/40 disabled:cursor-not-allowed text-white font-semibold text-sm transition-colors"
            >
              {uploading ? (
                <>
                  <svg className="animate-spin w-3.5 h-3.5" fill="none" viewBox="0 0 24 24">
                    <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                    <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
                  </svg>
                  Analiz ediliyor…
                </>
              ) : (
                <>
                  <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 10V3L4 14h7v7l9-11h-7z" />
                  </svg>
                  Analiz Et
                </>
              )}
            </button>
          </div>
        </section>

        {/* ── Proje Listesi ── */}
        <section>
          <div className="mb-5 flex items-center justify-between gap-4">
            <div>
              <h1 className="text-lg font-semibold text-gray-100">Projelerim</h1>
              <p className="text-gray-500 text-xs mt-0.5">API analiz raporlarınızı görüntüleyin ve yönetin.</p>
            </div>

            {fetchState === 'success' && projects.length > 0 && (
              <div className="flex items-center gap-2 shrink-0">
                {selectMode ? (
                  <>
                    <button
                      onClick={toggleSelectMode}
                      className="px-3 py-1.5 rounded-md bg-gray-800 hover:bg-gray-700 border border-gray-700 text-gray-300 text-sm transition-colors"
                    >
                      İptal
                    </button>
                    <button
                      onClick={() => {
                        if (selectedIds.size === projects.length) {
                          setSelectedIds(new Set())
                        } else {
                          setSelectedIds(new Set(projects.map(p => p.id)))
                        }
                      }}
                      className="px-3 py-1.5 rounded-md bg-gray-800 hover:bg-gray-700 border border-gray-700 text-gray-300 text-sm transition-colors"
                    >
                      {selectedIds.size === projects.length ? 'Seçimi Kaldır' : 'Hepsini Seç'}
                    </button>
                    <button
                      onClick={handleDeleteSelected}
                      disabled={selectedIds.size === 0 || deleting}
                      className="flex items-center gap-1.5 px-3 py-1.5 rounded-md bg-red-700/50 hover:bg-red-700/70 disabled:opacity-40 disabled:cursor-not-allowed border border-red-700/50 text-red-200 text-sm font-medium transition-colors"
                    >
                      {deleting ? (
                        <svg className="animate-spin w-3.5 h-3.5" fill="none" viewBox="0 0 24 24">
                          <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                          <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
                        </svg>
                      ) : (
                        <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                        </svg>
                      )}
                      {selectedIds.size > 0 ? `${selectedIds.size} Projeyi Sil` : 'Seçim Yapın'}
                    </button>
                  </>
                ) : (
                  <button
                    onClick={toggleSelectMode}
                    className="flex items-center gap-1.5 px-3 py-1.5 rounded-md bg-gray-800 hover:bg-gray-700 border border-gray-700 hover:border-red-800 text-gray-400 hover:text-red-400 text-sm transition-colors"
                  >
                    <svg className="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                    </svg>
                    Sil
                  </button>
                )}
              </div>
            )}
          </div>

          {/* Yükleniyor */}
          {fetchState === 'loading' && (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
              {Array.from({ length: 6 }).map((_, i) => (
                <div key={i} className="bg-gray-900 border border-gray-800 rounded-lg p-5 animate-pulse">
                  <div className="h-3.5 bg-gray-800 rounded w-2/3 mb-3" />
                  <div className="h-3 bg-gray-800 rounded w-full mb-2" />
                  <div className="h-3 bg-gray-800 rounded w-4/5 mb-5" />
                  <div className="h-5 bg-gray-800 rounded w-24" />
                </div>
              ))}
            </div>
          )}

          {/* Hata */}
          {fetchState === 'error' && (
            <div className="flex flex-col items-center justify-center py-16 text-center">
              <div className="w-10 h-10 rounded-lg bg-red-500/10 border border-red-800 flex items-center justify-center mb-4">
                <svg className="w-5 h-5 text-red-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
              </div>
              <p className="text-gray-300 text-sm font-medium">{fetchError}</p>
              <button
                onClick={fetchProjects}
                className="mt-3 text-blue-400 hover:text-blue-300 text-sm transition-colors"
              >
                Tekrar dene
              </button>
            </div>
          )}

          {/* Boş */}
          {fetchState === 'success' && projects.length === 0 && (
            <div className="flex flex-col items-center justify-center py-16 text-center border border-dashed border-gray-800 rounded-lg">
              <svg className="w-8 h-8 text-gray-700 mb-3" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M3 7v10a2 2 0 002 2h14a2 2 0 002-2V9a2 2 0 00-2-2h-6l-2-2H5a2 2 0 00-2 2z" />
              </svg>
              <p className="text-gray-400 font-medium text-sm">Henüz bir proje yüklemediniz.</p>
              <p className="text-gray-600 text-xs mt-1">Yukarıdan Swagger JSON yapıştırarak ilk analizinizi başlatın.</p>
            </div>
          )}

          {/* Grid */}
          {fetchState === 'success' && projects.length > 0 && (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
              {projects.map((project) => {
                const isSelected = selectedIds.has(project.id)
                return selectMode ? (
                  <div
                    key={project.id}
                    onClick={() => toggleSelect(project.id)}
                    className={`bg-gray-900 border rounded-lg p-5 transition-colors flex flex-col relative cursor-pointer
                      ${isSelected
                        ? 'border-red-800/60 bg-gray-800/60'
                        : 'border-gray-800 hover:border-gray-700 hover:bg-gray-800/50'
                      }`}
                  >
                    <div className={`absolute top-4 right-4 w-4 h-4 rounded border flex items-center justify-center transition-colors
                      ${isSelected ? 'bg-red-700/40 border-red-600/50' : 'bg-transparent border-gray-600'}`}
                    >
                      {isSelected && (
                        <svg className="w-2.5 h-2.5 text-white" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={3} d="M5 13l4 4L19 7" />
                        </svg>
                      )}
                    </div>
                    <div className="flex items-start gap-3 mb-2">
                      <svg className="w-4 h-4 text-gray-500 shrink-0 mt-0.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
                      </svg>
                      <div className="min-w-0 pr-5">
                        <h2 className="text-gray-100 font-medium text-sm leading-snug truncate">{project.name}</h2>
                        {project.createdAt && (
                          <p className="text-gray-600 text-xs mt-0.5">
                            {new Date(project.createdAt.endsWith('Z') ? project.createdAt : project.createdAt + 'Z').toLocaleString('tr-TR', {
                              day: 'numeric', month: 'short', year: 'numeric',
                              hour: '2-digit', minute: '2-digit',
                            })}
                          </p>
                        )}
                      </div>
                    </div>
                    {project.description && (
                      <p className="text-gray-500 text-xs leading-relaxed line-clamp-2 mb-2">{project.description}</p>
                    )}
                    {project.endpointCount !== undefined && (
                      <div className="flex items-center gap-1.5 mt-auto pt-2">
                        <span className="w-1.5 h-1.5 rounded-full bg-green-500" />
                        <span className="text-gray-500 text-xs">{project.endpointCount} endpoint</span>
                      </div>
                    )}
                  </div>
                ) : (
                  <Link
                    key={project.id}
                    href={`/dashboard/report?id=${project.id}`}
                    className="group bg-gray-900 border border-gray-800 rounded-lg p-5 transition-colors flex flex-col hover:border-gray-600 hover:bg-gray-800/40"
                  >
                    <div className="flex items-start gap-3 mb-2">
                      <svg className="w-4 h-4 text-gray-500 shrink-0 mt-0.5 group-hover:text-gray-400 transition-colors" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M10 20l4-16m4 4l4 4-4 4M6 16l-4-4 4-4" />
                      </svg>
                      <div className="min-w-0 flex-1">
                        <h2 className="text-gray-100 font-medium text-sm leading-snug truncate group-hover:text-white transition-colors">{project.name}</h2>
                        {project.createdAt && (
                          <p className="text-gray-600 text-xs mt-0.5">
                            {new Date(project.createdAt.endsWith('Z') ? project.createdAt : project.createdAt + 'Z').toLocaleString('tr-TR', {
                              day: 'numeric', month: 'short', year: 'numeric',
                              hour: '2-digit', minute: '2-digit',
                            })}
                          </p>
                        )}
                      </div>
                      <svg className="w-3.5 h-3.5 text-gray-700 group-hover:text-gray-400 transition-colors shrink-0 mt-0.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 5l7 7-7 7" />
                      </svg>
                    </div>
                    {project.description && (
                      <p className="text-gray-500 text-xs leading-relaxed line-clamp-2 mb-2">{project.description}</p>
                    )}
                    {project.endpointCount !== undefined && (
                      <div className="flex items-center gap-1.5 mt-auto pt-2">
                        <span className="w-1.5 h-1.5 rounded-full bg-green-500" />
                        <span className="text-gray-500 text-xs">{project.endpointCount} endpoint</span>
                      </div>
                    )}
                  </Link>
                )
              })}
            </div>
          )}
        </section>
      </main>
    </div>
  )
}
