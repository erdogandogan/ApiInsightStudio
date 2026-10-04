import { Suspense } from 'react'
import AuditContent from './AuditContent'

export default function AuditPage() {
  return (
    <Suspense>
      <AuditContent />
    </Suspense>
  )
}
