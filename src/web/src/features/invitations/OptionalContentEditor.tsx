import type { EditableDraftContent } from './draftModel'
import { useTranslation } from 'react-i18next'

interface Props {
  content: EditableDraftContent
  update: <K extends keyof EditableDraftContent>(key: K, value: EditableDraftContent[K]) => void
}

export function OptionalContentEditor({ content, update }: Props) {
  const { t } = useTranslation()
  return <div className="optional-content-editor">
    <fieldset><legend>{t('creatorEditorUi.optionalContent.announcement')}</legend><label htmlFor="draft-announcement">{t('creatorEditorUi.optionalContent.announcementOptional')}</label><textarea id="draft-announcement" value={content.announcement} onChange={event => update('announcement', event.target.value)} /></fieldset>
    <fieldset><legend>{t('creatorEditorUi.optionalContent.contacts')}</legend><p>{t('creatorEditorUi.optionalContent.phoneHelp')}</p>
      {content.contacts.map((contact, index) => <fieldset key={index}><legend>{t('creatorEditorUi.optionalContent.contact', { index: index + 1 })}</legend>
        {(['name', 'role', 'phone'] as const).map(field => <label key={field}>{field === 'name' ? t('creatorEditorUi.optionalContent.name') : field === 'role' ? t('creatorEditorUi.optionalContent.roleOptional') : t('creatorEditorUi.optionalContent.phone')}<input type={field === 'phone' ? 'tel' : 'text'} value={contact[field] ?? ''} onChange={event => update('contacts', content.contacts.map((item, position) => position === index ? { ...item, [field]: event.target.value } : item))} /></label>)}
        <button className="button button--secondary" type="button" onClick={() => update('contacts', content.contacts.filter((_, position) => position !== index))}>{t('creatorEditorUi.optionalContent.removeContact')}<span className="visually-hidden">: {index + 1}</span></button>
      </fieldset>)}
      <button className="button button--secondary" type="button" onClick={() => update('contacts', [...content.contacts, { name: '', phone: '' }])}>{t('creatorEditorUi.optionalContent.addContact')}</button>
    </fieldset>
    <fieldset><legend>{t('creatorEditorUi.optionalContent.faq')}</legend>{content.faqs.map((faq, index) => <fieldset key={index}><legend>{t('creatorEditorUi.optionalContent.question')} {index + 1}</legend>
      <label>{t('creatorEditorUi.optionalContent.question')}<input value={faq.question} onChange={event => update('faqs', content.faqs.map((item, position) => position === index ? { ...item, question: event.target.value } : item))} /></label>
      <label>{t('creatorEditorUi.optionalContent.answer')}<textarea value={faq.answer} onChange={event => update('faqs', content.faqs.map((item, position) => position === index ? { ...item, answer: event.target.value } : item))} /></label>
      <button className="button button--secondary" type="button" onClick={() => update('faqs', content.faqs.filter((_, position) => position !== index))}>{t('creatorEditorUi.optionalContent.removeQuestion')}<span className="visually-hidden">: {index + 1}</span></button>
    </fieldset>)}<button className="button button--secondary" type="button" onClick={() => update('faqs', [...content.faqs, { question: '', answer: '' }])}>{t('creatorEditorUi.optionalContent.addQuestion')}</button></fieldset>
    <fieldset><legend>{t('creatorEditorUi.optionalContent.transport')}</legend><p>{t('creatorEditorUi.optionalContent.transportHelp')}</p>{content.transportStops.map((stop, index) => <fieldset key={index}><legend>{t('creatorEditorUi.optionalContent.stop', { index: index + 1 })}</legend>
      <label>{t('creatorEditorUi.optionalContent.stopName')}<input value={stop.name} onChange={event => update('transportStops', content.transportStops.map((item, position) => position === index ? { ...item, name: event.target.value } : item))} /></label>
      <label>{t('creatorEditorUi.optionalContent.addressOptional')}<textarea value={stop.address ?? ''} onChange={event => update('transportStops', content.transportStops.map((item, position) => position === index ? { ...item, address: event.target.value } : item))} /></label>
      <label>{t('creatorEditorUi.optionalContent.departureOptional')}<input type="time" value={stop.departureTime ?? ''} onChange={event => update('transportStops', content.transportStops.map((item, position) => position === index ? { ...item, departureTime: event.target.value || null } : item))} /></label>
      <button className="button button--secondary" type="button" onClick={() => update('transportStops', content.transportStops.filter((_, position) => position !== index))}>{t('creatorEditorUi.optionalContent.removeStop')}<span className="visually-hidden">: {index + 1}</span></button>
    </fieldset>)}<button className="button button--secondary" type="button" onClick={() => update('transportStops', [...content.transportStops, { name: '' }])}>{t('creatorEditorUi.optionalContent.addStop')}</button></fieldset>
  </div>
}
