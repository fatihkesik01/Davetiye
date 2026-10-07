import { useState } from 'react'

import type { DraftContentInput } from '../../api/generated/client'
import { InvitationRenderer } from '../templates/rendering/InvitationRenderer'
import { normalizeDraftContent } from '../templates/rendering/model'

type PreviewDevice = 'phone' | 'tablet' | 'desktop'

interface DevicePreviewProps {
  invitationId: string
  templateKey: string | null
  rendererVersion: number | null
  content: DraftContentInput
}

const devices: Array<{ value: PreviewDevice; label: string }> = [
  { value: 'phone', label: 'Telefon' },
  { value: 'tablet', label: 'Tablet' },
  { value: 'desktop', label: 'Masaüstü' },
]

export function DevicePreview({ invitationId, templateKey, rendererVersion, content }: DevicePreviewProps) {
  const [device, setDevice] = useState<PreviewDevice>('phone')

  if (!templateKey || !rendererVersion) {
    return <section className="preview-empty" role="status">
      <h3>Önizleme için şablon seçin</h3>
      <p>Şablon adımından bir tasarım seçtiğinizde kendi bilgileriniz burada görünür.</p>
    </section>
  }

  return <section className="device-preview" aria-labelledby="preview-heading">
    <div className="device-preview__toolbar">
      <div>
        <h3 id="preview-heading">Davetiye önizlemesi</h3>
        <p>Cihaz seçimi yalnızca görünüm genişliğini değiştirir.</p>
      </div>
      <div className="segmented-control" role="group" aria-label="Önizleme cihazı">
        {devices.map(option => <button
          key={option.value}
          type="button"
          aria-pressed={device === option.value}
          onClick={() => setDevice(option.value)}
        >{option.label}</button>)}
      </div>
      <a
        className="button button--secondary"
        href={`/panel/davetiyeler/${encodeURIComponent(invitationId)}/onizleme`}
        target="_blank"
        rel="noopener noreferrer"
      >Yeni sekmede tam önizleme</a>
    </div>
    <div className={`device-preview__stage device-preview__stage--${device}`}>
      <InvitationRenderer
        templateKey={templateKey}
        rendererVersion={rendererVersion}
        model={normalizeDraftContent(content)}
        previewContext="creator"
      />
    </div>
    <p className="device-preview__note">Bu bir taslak önizlemesidir; public bir davetiye adresi oluşturmaz.</p>
  </section>
}
