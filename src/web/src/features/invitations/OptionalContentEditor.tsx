import type { EditableDraftContent } from './draftModel'

interface Props {
  content: EditableDraftContent
  update: <K extends keyof EditableDraftContent>(key: K, value: EditableDraftContent[K]) => void
}

export function OptionalContentEditor({ content, update }: Props) {
  return <div className="optional-content-editor">
    <fieldset><legend>Duyuru</legend><label htmlFor="draft-announcement">Duyuru metni (isteğe bağlı)</label><textarea id="draft-announcement" value={content.announcement} onChange={event => update('announcement', event.target.value)} /></fieldset>
    <fieldset><legend>İletişim kişileri</legend><p>WhatsApp bağlantısı için ülke koduyla başlayan telefon girin (örnek: +90…).</p>
      {content.contacts.map((contact, index) => <fieldset key={index}><legend>İletişim kişisi {index + 1}</legend>
        {(['name', 'role', 'phone'] as const).map(field => <label key={field}>{field === 'name' ? 'İsim' : field === 'role' ? 'Rol / açıklama (isteğe bağlı)' : 'Telefon'}<input type={field === 'phone' ? 'tel' : 'text'} value={contact[field] ?? ''} onChange={event => update('contacts', content.contacts.map((item, position) => position === index ? { ...item, [field]: event.target.value } : item))} /></label>)}
        <button className="button button--secondary" type="button" onClick={() => update('contacts', content.contacts.filter((_, position) => position !== index))}>İletişim kişisini kaldır<span className="visually-hidden">: {index + 1}</span></button>
      </fieldset>)}
      <button className="button button--secondary" type="button" onClick={() => update('contacts', [...content.contacts, { name: '', phone: '' }])}>İletişim kişisi ekle</button>
    </fieldset>
    <fieldset><legend>Sık sorulan sorular</legend>{content.faqs.map((faq, index) => <fieldset key={index}><legend>Soru {index + 1}</legend>
      <label>Soru<input value={faq.question} onChange={event => update('faqs', content.faqs.map((item, position) => position === index ? { ...item, question: event.target.value } : item))} /></label>
      <label>Yanıt<textarea value={faq.answer} onChange={event => update('faqs', content.faqs.map((item, position) => position === index ? { ...item, answer: event.target.value } : item))} /></label>
      <button className="button button--secondary" type="button" onClick={() => update('faqs', content.faqs.filter((_, position) => position !== index))}>Soruyu kaldır<span className="visually-hidden">: {index + 1}</span></button>
    </fieldset>)}<button className="button button--secondary" type="button" onClick={() => update('faqs', [...content.faqs, { question: '', answer: '' }])}>Soru ekle</button></fieldset>
    <fieldset><legend>Ulaşım ve servis</legend><p>Kalkış saati etkinliğin saat diliminde gösterilir; ayrı bir tarih belirtilmez.</p>{content.transportStops.map((stop, index) => <fieldset key={index}><legend>Kalkış noktası {index + 1}</legend>
      <label>Nokta adı<input value={stop.name} onChange={event => update('transportStops', content.transportStops.map((item, position) => position === index ? { ...item, name: event.target.value } : item))} /></label>
      <label>Adres (isteğe bağlı)<textarea value={stop.address ?? ''} onChange={event => update('transportStops', content.transportStops.map((item, position) => position === index ? { ...item, address: event.target.value } : item))} /></label>
      <label>Kalkış saati (isteğe bağlı)<input type="time" value={stop.departureTime ?? ''} onChange={event => update('transportStops', content.transportStops.map((item, position) => position === index ? { ...item, departureTime: event.target.value || null } : item))} /></label>
      <button className="button button--secondary" type="button" onClick={() => update('transportStops', content.transportStops.filter((_, position) => position !== index))}>Kalkış noktasını kaldır<span className="visually-hidden">: {index + 1}</span></button>
    </fieldset>)}<button className="button button--secondary" type="button" onClick={() => update('transportStops', [...content.transportStops, { name: '' }])}>Kalkış noktası ekle</button></fieldset>
  </div>
}
