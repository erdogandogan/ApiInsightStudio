import { Suspense } from 'react'
import ReportContent from './ReportContent'

export default function ReportPage() {
  return (
    <Suspense>
      <ReportContent />
    </Suspense>
  )
}
