'use client'

import { useState } from 'react'
import apiClient from '@/lib/api'
import {
  AiSuggestionDto,
  MAX_CONTENT_LENGTH,
  MAX_NOTE_LENGTH,
  STATUS_LABELS,
  errorMessage,
  formatDate,
} from '@/lib/aiReview'

type Mode = 'edit' | 'reject'

const METHOD_COLORS: Record<string, string> = {
  GET: 'text-green-400/70',
  POST: 'text-blue-400/70',
  PUT: 'text-yellow-400/70',
  PATCH: 'text-purple-400/70',
  DELETE: 'text-red-400/70',
}

function Method({ method }: { method: string }) {
  return (
    <span className={`inline-block px-1.5 py-0.5 rounded border border-gray-700 bg-gray-800 text-xs font-bold font-mono ${METHOD_COLORS[method.toUpperCase()] ?? 'text-gray-500'}`}>
      {method.toUpperCase()}
    </span>
  )
}

function StatusPill({ status }: { status: AiSuggestionDto['status'] }) {
  const color =
    status === 'Approved' ? 'text-green-400 border-green-800 bg-green-500/10'
    : status === 'Rejected' ? 'text-red-400 border-red-800 bg-red-500/10'
    : status === 'Pending' ? 'text-yellow-400 border-yellow-800 bg-yellow-500/10'
    : 'text-gray-500 border-gray-700 bg-gray-800'
  return <span className={`px-1.5 py-0.5 rounded border text-xs ${color}`}>{STATUS_LABELS[status]}</span>
}

interface Props {
  projectId: string
  suggestions: AiSuggestionDto[]
  /** Bir inceleme (onay/ret) bittiğinde veya sunucu durumu değiştiğinde listeyi ve raporu yenilemek için. */
  onChanged: () => void
}

export default function AiSuggestionsPanel({ projectId, suggestions, onChanged }: Props) {
  const [busyId, setBusyId] = useState<number | null>(null)
  const [openId, setOpenId] = useState<number | null>(null)
  const [mode, setMode] = useState<Mode | null>(null)
  const [draft, setDraft] = useState('')
  const [note, setNote] = useState('')
  const [error, setError] = useState<{ id: number; message: string } | null>(null)

  const pending = suggestions.filter(s => s.status === 'Pending')
  const history = suggestions.filter(s => s.status !== 'Pending')

  function open(s: AiSuggestionDto, nextMode: Mode) {
    setError(null)
    setOpenId(s.id)
    setMode(nextMode)
    setDraft(s.content)
    setNote('')
  }

  function close() {
    setOpenId(null)
    setMode(null)
    setDraft('')
    setNote('')
  }

  async function review(s: AiSuggestionDto, action: 'approve' | 'reject', body: Record<string, string>) {
    setBusyId(s.id)
    setError(null)
    try {
      await apiClient.post(`/automation/${projectId}/ai-suggestions/${s.id}/${action}`, body)
      close()
    } catch (err: unknown) {
      setError({ id: s.id, message: errorMessage(err, 'İşlem tamamlanamadı.') })
    } finally {
      setBusyId(null)
      // Hata (örn. 409: öneri artık bekleyen değil) olsa da sunucudaki güncel durum gösterilsin
      onChanged()
    }
  }

  const trimmedDraft = draft.trim()
  const edited = trimmedDraft.length > 0 && trimmedDraft

  return (
    <div className="bg-gray-900 border border-gray-800 rounded-lg p-5">
      <div className="flex items-center justify-between mb-1">
        <div className="flex items-center gap-2">
          <svg className="w-4 h-4 text-yellow-400" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
          </svg>
          <h2 className="text-gray-100 font-medium text-sm">AI Önerileri</h2>
        </div>
        <span className="px-2 py-0.5 rounded bg-yellow-500/10 text-yellow-400 text-xs font-medium border border-yellow-800">
          {pending.length} onay bekliyor
        </span>
      </div>
      <p className="text-gray-600 text-xs mb-4">
        AI çıktısı siz onaylayana kadar yayımlanmaz. Onayladığınız metin yalnızca AI açıklaması olarak kaydedilir; kalite skoru değişmez.
      </p>

      {pending.length === 0 ? (
        <p className="text-gray-500 text-sm text-center py-4">Onay bekleyen öneri yok.</p>
      ) : (
        <ul className="space-y-3">
          {pending.map(s => {
            const busy = busyId === s.id
            const isOpen = openId === s.id
            return (
              <li key={s.id} className="bg-gray-950 border border-gray-800 rounded-md p-3">
                <div className="flex flex-wrap items-center gap-2 mb-2">
                  <Method method={s.method} />
                  <span className="text-gray-400 text-xs font-mono">{s.path}</span>
                  <span className="ml-auto text-gray-600 text-xs" title={`Komut sürümü: ${s.promptVersion}`}>
                    {s.model} · {s.promptVersion} · {formatDate(s.createdAt)}
                  </span>
                </div>

                <p className="text-gray-300 text-sm leading-relaxed whitespace-pre-wrap">{s.content}</p>

                {isOpen && mode === 'edit' && (
                  <div className="mt-3">
                    <label className="block text-gray-500 text-xs mb-1" htmlFor={`edit-${s.id}`}>
                      Metni düzenleyin (özgün AI metni de kayıtlı kalır)
                    </label>
                    <textarea
                      id={`edit-${s.id}`}
                      value={draft}
                      onChange={e => setDraft(e.target.value)}
                      maxLength={MAX_CONTENT_LENGTH}
                      rows={3}
                      className="w-full rounded-md bg-gray-900 border border-gray-700 focus:border-gray-500 outline-none text-gray-200 text-sm p-2"
                    />
                    <label className="block text-gray-500 text-xs mt-2 mb-1" htmlFor={`note-${s.id}`}>Not (isteğe bağlı)</label>
                    <input
                      id={`note-${s.id}`}
                      value={note}
                      onChange={e => setNote(e.target.value)}
                      maxLength={MAX_NOTE_LENGTH}
                      className="w-full rounded-md bg-gray-900 border border-gray-700 focus:border-gray-500 outline-none text-gray-200 text-sm p-2"
                    />
                  </div>
                )}

                {isOpen && mode === 'reject' && (
                  <div className="mt-3">
                    <label className="block text-gray-500 text-xs mb-1" htmlFor={`reject-${s.id}`}>Ret nedeni (isteğe bağlı)</label>
                    <input
                      id={`reject-${s.id}`}
                      value={note}
                      onChange={e => setNote(e.target.value)}
                      maxLength={MAX_NOTE_LENGTH}
                      className="w-full rounded-md bg-gray-900 border border-gray-700 focus:border-gray-500 outline-none text-gray-200 text-sm p-2"
                    />
                  </div>
                )}

                {error?.id === s.id && (
                  <p role="alert" className="mt-2 text-red-400 text-xs">{error.message}</p>
                )}

                <div className="flex flex-wrap items-center gap-2 mt-3">
                  {!isOpen && (
                    <>
                      <button
                        disabled={busy}
                        onClick={() => review(s, 'approve', {})}
                        className="px-2.5 py-1 rounded text-xs font-medium bg-green-500/10 hover:bg-green-500/20 border border-green-800 text-green-400 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                      >
                        {busy ? 'İşleniyor…' : 'Onayla'}
                      </button>
                      <button
                        disabled={busy}
                        onClick={() => open(s, 'edit')}
                        className="px-2.5 py-1 rounded text-xs font-medium bg-gray-800 hover:bg-gray-700 border border-gray-700 text-gray-300 disabled:opacity-50 transition-colors"
                      >
                        Düzenleyip onayla
                      </button>
                      <button
                        disabled={busy}
                        onClick={() => open(s, 'reject')}
                        className="px-2.5 py-1 rounded text-xs font-medium bg-gray-800 hover:bg-gray-700 border border-gray-700 text-red-400/80 disabled:opacity-50 transition-colors"
                      >
                        Reddet
                      </button>
                    </>
                  )}

                  {isOpen && mode === 'edit' && (
                    <>
                      <button
                        disabled={busy || !edited}
                        onClick={() => review(s, 'approve', {
                          ...(edited ? { editedContent: edited } : {}),
                          ...(note.trim() ? { note: note.trim() } : {}),
                        })}
                        className="px-2.5 py-1 rounded text-xs font-medium bg-green-500/10 hover:bg-green-500/20 border border-green-800 text-green-400 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                      >
                        {busy ? 'İşleniyor…' : 'Düzenlenmiş metni onayla'}
                      </button>
                      <button disabled={busy} onClick={close} className="px-2.5 py-1 rounded text-xs text-gray-500 hover:text-gray-300 transition-colors">Vazgeç</button>
                    </>
                  )}

                  {isOpen && mode === 'reject' && (
                    <>
                      <button
                        disabled={busy}
                        onClick={() => review(s, 'reject', note.trim() ? { note: note.trim() } : {})}
                        className="px-2.5 py-1 rounded text-xs font-medium bg-red-500/10 hover:bg-red-500/20 border border-red-800 text-red-400 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                      >
                        {busy ? 'İşleniyor…' : 'Reddi onayla'}
                      </button>
                      <button disabled={busy} onClick={close} className="px-2.5 py-1 rounded text-xs text-gray-500 hover:text-gray-300 transition-colors">Vazgeç</button>
                    </>
                  )}
                </div>
              </li>
            )
          })}
        </ul>
      )}

      {history.length > 0 && (
        <details className="mt-4">
          <summary className="cursor-pointer text-gray-500 hover:text-gray-300 text-xs select-none">
            Geçmiş öneriler ({history.length})
          </summary>
          <ul className="space-y-2 mt-3">
            {history.map(s => (
              <li key={s.id} className="bg-gray-950 border border-gray-800 rounded-md p-3">
                <div className="flex flex-wrap items-center gap-2 mb-1.5">
                  <Method method={s.method} />
                  <span className="text-gray-400 text-xs font-mono">{s.path}</span>
                  <StatusPill status={s.status} />
                  <span className="ml-auto text-gray-600 text-xs">
                    {s.model} · {s.promptVersion}{s.reviewedAt ? ` · ${formatDate(s.reviewedAt)}` : ''}
                  </span>
                </div>
                <p className="text-gray-400 text-sm leading-relaxed whitespace-pre-wrap">{s.finalContent ?? s.content}</p>
                {s.finalContent && (
                  <p className="text-gray-600 text-xs mt-1.5">
                    Düzenlendi. Özgün AI metni: <span className="text-gray-500">{s.content}</span>
                  </p>
                )}
                {s.reviewNote && <p className="text-gray-600 text-xs mt-1">Not: {s.reviewNote}</p>}
              </li>
            ))}
          </ul>
        </details>
      )}
    </div>
  )
}
