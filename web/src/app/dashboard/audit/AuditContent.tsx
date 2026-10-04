'use client'

import { useCallback, useEffect, useState } from 'react'
import { useRouter, useSearchParams } from 'next/navigation'
import Cookies from 'js-cookie'
import apiClient from '@/lib/api'
import {
  ACTION_LABELS,
  AUDIT_FILTERS,
  AuditEntryDto,
  AuditPageDto,
  AuditVerificationDto,
  errorMessage,
  formatDate,
  httpStatus,
} from '@/lib/aiReview'

const PAGE_SIZE = 50

export default function AuditContent() {
  const searchParams = useSearchParams()
  const id = searchParams.get('id') ?? ''
  const router = useRouter()

  const [entries, setEntries] = useState<AuditEntryDto[]>([])
  const [nextBefore, setNextBefore] = useState<number | null>(null)
  const [filter, setFilter] = useState('')
  const [loading, setLoading] = useState(Boolean(id))
  const [loadingMore, setLoadingMore] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [verifying, setVerifying] = useState(false)
  const [verification, setVerification] = useState<AuditVerificationDto | null>(null)
  const [verifyError, setVerifyError] = useState<string | null>(null)

  // Sayfa getirme: durum güncellemeleri yalnızca cevap geldiğinde (then/catch içinde) yapılır
  const fetchPage = useCallback((before: number | null, action: string) => {
    const params = new URLSearchParams({ take: String(PAGE_SIZE) })
    if (before !== null) params.set('before', String(before))
    if (action) params.set('action', action)
    return apiClient.get<AuditPageDto>(`/automation/${id}/audit?${params.toString()}`)
  }, [id])

  function handleFetchError(err: unknown) {
    if (httpStatus(err) === 401) { router.push('/login'); return }
    setError(errorMessage(err, 'Denetim izi yüklenirken bir hata oluştu.'))
  }

  // İlk yükleme ve süzgeç değişimi (ikincisinde loading, onChange işleyicisinde açılır)
  useEffect(() => {
    if (!id) return
    let cancelled = false
    fetchPage(null, filter)
      .then(({ data }) => {
        if (cancelled) return
        setEntries(data.items)
        setNextBefore(data.nextBefore)
        setError(null)
      })
      .catch((err: unknown) => { if (!cancelled) handleFetchError(err) })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
    // handleFetchError yalnızca router ve setState kullanır; her render'da yeniden oluşması yeniden getirmeyi tetiklememeli
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, filter, fetchPage])

  function handleFilterChange(value: string) {
    setLoading(true)
    setEntries([])
    setNextBefore(null)
    setFilter(value)
  }

  function handleLoadMore() {
    if (nextBefore === null) return
    setLoadingMore(true)
    fetchPage(nextBefore, filter)
      .then(({ data }) => {
        setEntries(prev => [...prev, ...data.items])
        setNextBefore(data.nextBefore)
      })
      .catch(handleFetchError)
      .finally(() => setLoadingMore(false))
  }

  function handleVerify() {
    setVerifying(true)
    setVerifyError(null)
    setVerification(null)
    apiClient
      .get<AuditVerificationDto>(`/automation/${id}/audit/verify`)
      .then(({ data }) => setVerification(data))
      .catch((err: unknown) => {
        if (httpStatus(err) === 401) { router.push('/login'); return }
        setVerifyError(errorMessage(err, 'Zincir doğrulanamadı.'))
      })
      .finally(() => setVerifying(false))
  }

  function handleLogout() {
    Cookies.remove('token')
    localStorage.removeItem('token')
    router.push('/login')
  }

  return (
    <div className="min-h-screen bg-gray-950 text-gray-100">
      <header className="border-b border-gray-800 bg-gray-950 sticky top-0 z-10">
        <div className="max-w-6xl mx-auto px-6 h-14 flex items-center gap-3">
          <button
            onClick={() => router.push(id ? `/dashboard/report/?id=${encodeURIComponent(id)}` : '/dashboard')}
            className="flex items-center gap-1.5 text-gray-500 hover:text-gray-200 text-sm transition-colors"
          >
            <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M15 19l-7-7 7-7" />
            </svg>
            Rapora Dön
          </button>
          <span className="text-gray-700">/</span>
          <span className="text-gray-200 font-medium text-sm">Denetim İzi</span>
          <div className="ml-auto">
            <button onClick={handleLogout} className="text-gray-500 hover:text-gray-200 text-sm transition-colors">
              Çıkış Yap
            </button>
          </div>
        </div>
      </header>

      <main className="max-w-6xl mx-auto px-6 py-8 space-y-6">
        <div className="bg-gray-900 border border-gray-800 rounded-lg p-5">
          <div className="flex flex-wrap items-center gap-3">
            <div>
              <h1 className="text-gray-100 font-medium text-sm">Değiştirilemez denetim izi</h1>
              <p className="text-gray-600 text-xs mt-0.5">
                Kayıtlar yalnızca eklenir; her biri bir öncekinin özetini taşır. Hiçbir kayıt token, sır veya tam adres içermez.
              </p>
            </div>
            <div className="ml-auto flex items-center gap-2">
              <label htmlFor="audit-filter" className="text-gray-500 text-xs">Süzgeç</label>
              <select
                id="audit-filter"
                value={filter}
                onChange={e => handleFilterChange(e.target.value)}
                className="rounded-md bg-gray-950 border border-gray-700 text-gray-200 text-sm px-2 py-1"
              >
                {AUDIT_FILTERS.map(f => <option key={f.value} value={f.value}>{f.label}</option>)}
              </select>
              <button
                onClick={handleVerify}
                disabled={verifying || !id}
                className="px-3 py-1.5 rounded-md bg-gray-800 hover:bg-gray-700 border border-gray-700 text-gray-300 hover:text-gray-100 text-sm font-medium disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                {verifying ? 'Doğrulanıyor…' : 'Zinciri doğrula'}
              </button>
            </div>
          </div>

          {verification && (
            verification.valid ? (
              <div role="status" className="mt-4 p-3 rounded-md bg-green-500/10 border border-green-800 text-green-400 text-xs">
                Zincir sağlam: {verification.entryCount} kayıt doğrulandı.
                {verification.headHash && (
                  <> Son özet: <span className="font-mono" title={verification.headHash}>{verification.headHash.slice(0, 16)}…</span>
                  {' '}<span className="text-green-400/60">(sondaki kayıtların silinmesini fark etmek için bu değeri dışarıda saklayabilirsiniz.)</span></>
                )}
              </div>
            ) : (
              <div role="alert" className="mt-4 p-3 rounded-md bg-red-500/10 border border-red-800 text-red-400 text-xs">
                Zincir bozulmuş: {verification.firstInvalidSequence !== null && <>#{verification.firstInvalidSequence} numaralı kayıtta. </>}
                {verification.reason} (Bundan önceki {verification.entryCount} kayıt güvenilir.)
              </div>
            )
          )}
          {verifyError && <p role="alert" className="mt-4 text-red-400 text-xs">{verifyError}</p>}
        </div>

        <div className="bg-gray-900 border border-gray-800 rounded-lg p-5">
          {loading && <p className="text-gray-500 text-sm text-center py-10">Kayıtlar yükleniyor…</p>}

          {!loading && (error || !id) && <p role="alert" className="text-red-400 text-sm text-center py-10">{error ?? 'Geçersiz proje ID.'}</p>}

          {!loading && !error && id && entries.length === 0 && (
            <p className="text-gray-500 text-sm text-center py-10">Bu süzgeçle kayıt bulunamadı.</p>
          )}

          {!loading && !error && entries.length > 0 && (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-gray-800">
                    <th className="text-left text-gray-500 font-medium text-xs pb-2.5 pr-4">#</th>
                    <th className="text-left text-gray-500 font-medium text-xs pb-2.5 pr-4">Zaman</th>
                    <th className="text-left text-gray-500 font-medium text-xs pb-2.5 pr-4">Eylem</th>
                    <th className="text-left text-gray-500 font-medium text-xs pb-2.5 pr-4">Yapan</th>
                    <th className="text-left text-gray-500 font-medium text-xs pb-2.5 pr-4">Ayrıntı</th>
                    <th className="text-left text-gray-500 font-medium text-xs pb-2.5">Özet</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-800">
                  {entries.map(e => (
                    <tr key={e.sequence} className="hover:bg-gray-800/40 transition-colors align-top">
                      <td className="py-2.5 pr-4 text-gray-500 text-xs font-mono">{e.sequence}</td>
                      <td className="py-2.5 pr-4 text-gray-400 text-xs whitespace-nowrap">{formatDate(e.occurredAt)}</td>
                      <td className="py-2.5 pr-4">
                        <div className="text-gray-200 text-xs">{ACTION_LABELS[e.action] ?? e.action}</div>
                        <div className="text-gray-600 text-xs font-mono">{e.action}{e.subject ? ` · ${e.subject}` : ''}</div>
                      </td>
                      <td className="py-2.5 pr-4 text-gray-400 text-xs whitespace-nowrap">
                        {e.actorUserId === null ? 'Sistem' : `Kullanıcı #${e.actorUserId}`}
                      </td>
                      <td className="py-2.5 pr-4 text-gray-400 text-xs break-words max-w-md">{e.details}</td>
                      <td className="py-2.5 text-gray-600 text-xs font-mono" title={e.hash}>{e.hash.slice(0, 8)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {!loading && !error && nextBefore !== null && (
            <div className="flex justify-center mt-4">
              <button
                onClick={handleLoadMore}
                disabled={loadingMore}
                className="px-3 py-1.5 rounded-md bg-gray-800 hover:bg-gray-700 border border-gray-700 text-gray-300 text-sm disabled:opacity-50 transition-colors"
              >
                {loadingMore ? 'Yükleniyor…' : 'Daha eski kayıtlar'}
              </button>
            </div>
          )}
        </div>
      </main>
    </div>
  )
}
