import { useEffect, useMemo, useState } from 'react'

import {
  DavetiyeApiClient,
  type TemplateCatalogItem,
} from '../../../api/generated/client'
import { LoadingState } from '../../../components/feedback/LoadingState'
import { InternalLink } from '../../../components/ui/InternalLink'
import { TemplateDemoRenderer } from '../rendering/InvitationRenderer'

interface TemplateCatalogPageProps {
  demoTemplateKey?: string
}

export function TemplateCatalogPage({ demoTemplateKey }: TemplateCatalogPageProps) {
  const api = useMemo(() => new DavetiyeApiClient(), [])
  const [templates, setTemplates] = useState<TemplateCatalogItem[] | null>(null)
  const [failed, setFailed] = useState(false)
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    void api.listTemplates(controller.signal)
      .then(setTemplates)
      .catch(error => {
        if (error instanceof DOMException && error.name === 'AbortError') return
        setFailed(true)
      })
    return () => controller.abort()
  }, [api, attempt])

  if (failed) {
    return <section className="feedback-state feedback-state--stacked" role="alert">
      <h2>Şablonlar yüklenemedi</h2>
      <p>Katalog şu anda alınamadı. Bağlantınızı kontrol edip yeniden deneyin.</p>
      <button className="button button--secondary" type="button" onClick={() => {
        setFailed(false)
        setTemplates(null)
        setAttempt(value => value + 1)
      }}>
        Tekrar dene
      </button>
    </section>
  }

  if (!templates) return <LoadingState label="Şablonlar yükleniyor" />

  if (demoTemplateKey) {
    const selected = templates.find(template => template.key === demoTemplateKey)
    if (!selected) {
      return <section className="catalog-empty" role="status">
        <h2>Şablon bulunamadı</h2>
        <p>Bu şablon katalogda yer almıyor veya artık aktif değil.</p>
        <InternalLink className="button button--secondary" to="/sablonlar">Tüm şablonlara dön</InternalLink>
      </section>
    }

    return <TemplateDemoPage template={selected} />
  }

  return <section className="template-catalog" aria-labelledby="catalog-heading">
    <div className="catalog-intro">
      <p className="eyebrow">Sekiz farklı başlangıç tasarımı</p>
      <h2 id="catalog-heading">Etkinliğinize yakışan görünümü seçin</h2>
      <p>Her şablonu örnek içerikle inceleyin. Hesap açtığınızda kendi bilgilerinizle aynı tasarımı önizleyebilirsiniz.</p>
    </div>
    {templates.length === 0 ? <p className="catalog-empty" role="status">Şu anda gösterilebilecek aktif şablon bulunmuyor.</p> : null}
    <ul className="template-catalog__grid">
      {templates.map(template => <li key={template.key}><TemplateCatalogCard template={template} /></li>)}
    </ul>
  </section>
}

export function TemplateCatalogCard({ template }: { template: TemplateCatalogItem }) {
  return <article className="template-card">
    {template.previewImageUrl
      ? <img src={template.previewImageUrl} alt="" width="960" height="640" loading="lazy" />
      : <div className="template-card__image-placeholder" aria-hidden="true" />}
    <div className="template-card__body">
      <div className="template-card__meta">
        <span>{template.category}</span>
        <span className={`template-badge ${template.isPremium ? 'template-badge--premium' : ''}`}>
          {template.isPremium ? 'Premium' : 'Ücretsiz'}
        </span>
      </div>
      <h3>{template.name}</h3>
      {template.description ? <p>{template.description}</p> : null}
      <p>{template.supportedModules.length > 0
        ? `${template.supportedModules.length} içerik bölümüyle uyumlu`
        : 'Sade davetiye düzeni'}</p>
      <InternalLink className="button button--secondary" to={`/sablonlar/${encodeURIComponent(template.key)}`}>
        Demoyu incele
      </InternalLink>
    </div>
  </article>
}

function TemplateDemoPage({ template }: { template: TemplateCatalogItem }) {
  return <section className="template-demo" aria-labelledby="template-demo-heading">
    <nav className="template-demo__actions" aria-label="Şablon demosu eylemleri">
      <InternalLink to="/sablonlar">← Tüm şablonlar</InternalLink>
      <InternalLink
        className="button button--primary"
        to={`/panel/davetiyeler/yeni?template=${encodeURIComponent(template.key)}`}
      >Bu şablonla başla</InternalLink>
    </nav>
    <header className="template-demo__header">
      <p className="eyebrow">{template.category} · {template.isPremium ? 'Premium' : 'Ücretsiz'}</p>
      <h2 id="template-demo-heading">{template.name}</h2>
      <p>Bu görünüm örnek veriler kullanır. Taslak oluşturduğunuzda bilgileriniz yalnızca korumalı panelinizde işlenir.</p>
    </header>
    <div className="template-demo__canvas">
      <TemplateDemoRenderer templateKey={template.key} rendererVersion={template.rendererVersion} />
    </div>
  </section>
}
