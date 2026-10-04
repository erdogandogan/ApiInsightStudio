// AI önerisi inceleme ve denetim izi: sunucu tipleri ve ortak yardımcılar.

export type SuggestionStatus = 'Pending' | 'Approved' | 'Rejected' | 'Superseded'

export interface AiSuggestionDto {
  id: number
  endpointId: number
  method: string
  path: string
  content: string
  /** Onaylanırken düzenlendiyse yayımlanan son metin; düzenlenmediyse null. */
  finalContent: string | null
  model: string
  promptVersion: string
  status: SuggestionStatus
  createdAt: string
  reviewedAt: string | null
  reviewNote: string | null
}

export interface AuditEntryDto {
  sequence: number
  occurredAt: string
  actorUserId: number | null
  action: string
  subject: string | null
  details: string
  hash: string
}

export interface AuditPageDto {
  items: AuditEntryDto[]
  nextBefore: number | null
}

export interface AuditVerificationDto {
  valid: boolean
  entryCount: number
  firstInvalidSequence: number | null
  reason: string | null
  headHash: string | null
}

// Sunucudaki sınırlarla aynı (AiSuggestion.MaxContentLength / MaxNoteLength)
export const MAX_CONTENT_LENGTH = 2000
export const MAX_NOTE_LENGTH = 500

export const STATUS_LABELS: Record<SuggestionStatus, string> = {
  Pending: 'Onay bekliyor',
  Approved: 'Onaylandı',
  Rejected: 'Reddedildi',
  Superseded: 'Yenisiyle değiştirildi',
}

export const ACTION_LABELS: Record<string, string> = {
  'project.uploaded': 'Proje yüklendi',
  'analysis.completed': 'Analiz tamamlandı',
  'ai.suggestion.created': 'AI önerisi üretildi',
  'ai.suggestion.approved': 'AI önerisi onaylandı',
  'ai.suggestion.rejected': 'AI önerisi reddedildi',
  'ai.suggestion.superseded': 'AI önerisi yenisiyle değiştirildi',
  'settings.updated': 'Ayarlar güncellendi',
  'webhook.secret.rotated': 'Webhook sırrı yenilendi',
  'target.token.set': 'Hedef token tanımlandı',
  'target.token.cleared': 'Hedef token silindi',
  'testrun.started': 'Test koşusu başlatıldı',
  'testrun.finished': 'Test koşusu bitti',
}

/** Denetim izi süzgeci: sunucuya "action" öneki olarak gider. */
export const AUDIT_FILTERS: { value: string; label: string }[] = [
  { value: '', label: 'Tümü' },
  { value: 'ai.suggestion', label: 'AI önerileri' },
  { value: 'settings', label: 'Ayarlar' },
  { value: 'webhook', label: 'Webhook' },
  { value: 'target', label: 'Hedef token' },
  { value: 'testrun', label: 'Test koşuları' },
  { value: 'analysis', label: 'Analiz' },
  { value: 'project', label: 'Proje' },
]

/** Sunucunun { message } gövdesini, yoksa yedek metni döndürür. */
export function errorMessage(err: unknown, fallback: string): string {
  const e = err as { response?: { data?: { message?: string } }; message?: string }
  return e?.response?.data?.message ?? fallback
}

export function httpStatus(err: unknown): number | undefined {
  return (err as { response?: { status?: number } })?.response?.status
}

export function formatDate(iso: string): string {
  const d = new Date(iso.endsWith('Z') || /[+-]\d\d:\d\d$/.test(iso) ? iso : iso + 'Z')
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString('tr-TR')
}
