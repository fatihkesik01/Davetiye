import type { ReactNode } from 'react'

import {
  formatInvitationDate,
  formatInvitationTime,
  type NormalizedInvitationRenderModel,
} from './model'

export interface TemplateRendererProps {
  model: NormalizedInvitationRenderModel
}

function DateVenue({ model, compact = false }: TemplateRendererProps & { compact?: boolean }) {
  return <div className={`invite-facts${compact ? ' invite-facts--compact' : ''}`}>
    <p><span>Tarih</span><time dateTime={model.startsAt}>{formatInvitationDate(model.startsAt, model.timeZoneId)}</time></p>
    <p><span>Mekân</span><strong>{model.venue?.name ?? 'Mekân daha sonra duyurulacak'}</strong>{model.venue?.address ? <small>{model.venue.address}</small> : null}</p>
  </div>
}

function Program({ model, title = 'Program' }: TemplateRendererProps & { title?: string }) {
  if (model.programItems.length === 0) return null
  return <section className="invite-program" aria-labelledby={`program-${model.eventType}`}>
    <h2 id={`program-${model.eventType}`}>{title}</h2>
    <ol>{model.programItems.map((item, index) => <li key={`${item.title}-${index}`}>
      {item.startsAt ? <time dateTime={item.startsAt}>{formatInvitationTime(item.startsAt, model.timeZoneId)}</time> : null}
      <span><strong>{item.title}</strong>{item.description ? <small>{item.description}</small> : null}</span>
    </li>)}</ol>
  </section>
}

function Message({ children }: { children?: ReactNode }) {
  return children ? <p className="invite-message">{children}</p> : null
}

export function TimelessWeddingRenderer({ model }: TemplateRendererProps) {
  const monogram = model.hostNames.map(name => name.at(0)).filter(Boolean).slice(0, 2).join(' · ')
  return <article className="invitation-template invitation-template--timeless" aria-label={model.headline}>
    <header><p className="invite-kicker">Birlikte yeni bir başlangıç</p><div className="invite-monogram" aria-hidden="true">{monogram || 'D'}</div><h1>{model.headline}</h1><Message>{model.message}</Message></header>
    <DateVenue model={model} /><Program model={model} title="Günün akışı" />
    <footer>Sevgiyle bekliyoruz</footer>
  </article>
}

export function RomanticEngagementRenderer({ model }: TemplateRendererProps) {
  return <article className="invitation-template invitation-template--romantic" aria-label={model.headline}>
    <div className="romantic-bloom" aria-hidden="true">✦</div>
    <header><p className="invite-kicker">Nişanımıza davetlisiniz</p><h1>{model.headline}</h1></header>
    <div className="romantic-panel"><Message>{model.message}</Message><DateVenue model={model} compact /></div>
    <p className="invite-signature">{model.hostNames.join(' & ')}</p>
  </article>
}

export function HennaNightRenderer({ model }: TemplateRendererProps) {
  return <article className="invitation-template invitation-template--henna" aria-label={model.headline}>
    <div className="henna-stars" aria-hidden="true">✦ · ✧ · ✦</div>
    <header><p className="invite-kicker">Bu gece bizim gecemiz</p><h1>{model.headline}</h1><Message>{model.message}</Message></header>
    <DateVenue model={model} /><Program model={model} title="Gece programı" />
  </article>
}

export function JoyfulCircumcisionRenderer({ model }: TemplateRendererProps) {
  return <article className="invitation-template invitation-template--joyful" aria-label={model.headline}>
    <div className="joyful-flags" aria-hidden="true"><span>◆</span><span>●</span><span>▲</span><span>★</span><span>●</span></div>
    <header><p className="invite-kicker">Neşemize ortak olun</p><h1>{model.headline}</h1><Message>{model.message}</Message></header>
    <DateVenue model={model} compact /><Program model={model} />
  </article>
}

export function ColorfulBirthdayRenderer({ model }: TemplateRendererProps) {
  return <article className="invitation-template invitation-template--birthday" aria-label={model.headline}>
    <div className="birthday-confetti" aria-hidden="true">● ✦ ▲ ● ✦</div>
    <header><p className="invite-kicker">Kutlama zamanı</p><h1>{model.headline}</h1></header>
    <Message>{model.message}</Message><DateVenue model={model} compact />
    <div className="birthday-stamp" aria-hidden="true">Gel · Oyna · Kutla</div>
  </article>
}

export function PastelBabyShowerRenderer({ model }: TemplateRendererProps) {
  return <article className="invitation-template invitation-template--baby" aria-label={model.headline}>
    <div className="baby-cloud baby-cloud--one" aria-hidden="true" /><div className="baby-cloud baby-cloud--two" aria-hidden="true" />
    <header><p className="invite-kicker">Baby shower</p><h1>{model.headline}</h1><Message>{model.message}</Message></header>
    <DateVenue model={model} compact /><p className="invite-signature">{model.hostNames.join(' & ')}</p>
  </article>
}

export function ModernGraduationRenderer({ model }: TemplateRendererProps) {
  return <article className="invitation-template invitation-template--graduation" aria-label={model.headline}>
    <header><p className="invite-kicker">Mezuniyet kutlaması</p><h1>{model.headline}</h1><p className="graduation-name">{model.hostNames.join(' · ')}</p></header>
    <div className="graduation-grid"><Message>{model.message}</Message><DateVenue model={model} compact /></div>
    <Program model={model} title="Akşamın ritmi" />
  </article>
}

export function MinimalOpeningRenderer({ model }: TemplateRendererProps) {
  return <article className="invitation-template invitation-template--minimal" aria-label={model.headline}>
    <header><p className="invite-index" aria-hidden="true">08 / AÇILIŞ</p><h1>{model.headline}</h1></header>
    <div className="minimal-rule" aria-hidden="true" />
    <div className="minimal-grid"><Message>{model.message}</Message><DateVenue model={model} compact /></div>
  </article>
}
