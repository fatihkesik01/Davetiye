import type { NormalizedInvitationRenderModel } from './model'

export function InvitationOptionalSections({ model }: { model: NormalizedInvitationRenderModel }) {
  return <div className="invitation-optional-sections">
    {model.announcement ? <section aria-labelledby="invitation-announcement"><h2 id="invitation-announcement">Duyuru</h2><p>{model.announcement}</p></section> : null}
    {model.contacts?.length ? <section aria-labelledby="invitation-contacts"><h2 id="invitation-contacts">İletişim</h2><ul>{model.contacts.map((contact, index) => {
      const digits = contact.phone.replace(/[^0-9]/g, '')
      const international = contact.phone.startsWith('+') && digits.length >= 7 && digits.length <= 15
      const telephone = /^\+?[0-9\s().-]{7,30}$/.test(contact.phone)
      return <li key={index}><h3>{contact.name}</h3>{contact.role ? <p>{contact.role}</p> : null}<p>{contact.phone}</p><div className="button-row">
        {telephone ? <a className="button button--secondary" href={`tel:${contact.phone.startsWith('+') ? '+' : ''}${digits}`}>Ara<span className="visually-hidden">: {contact.name}</span></a> : null}
        {international ? <a className="button button--secondary" href={`https://wa.me/${digits}`} target="_blank" rel="noopener noreferrer">WhatsApp<span className="visually-hidden">: {contact.name}</span></a> : null}
      </div></li>
    })}</ul></section> : null}
    {model.faqs?.length ? <section aria-labelledby="invitation-faq"><h2 id="invitation-faq">Sık sorulan sorular</h2>{model.faqs.map((faq, index) => <details key={index}><summary>{faq.question}</summary><p>{faq.answer}</p></details>)}</section> : null}
    {model.transportStops?.length ? <section aria-labelledby="invitation-transport"><h2 id="invitation-transport">Ulaşım ve servis</h2><ul>{model.transportStops.map((stop, index) => <li key={index}><h3>{stop.name}</h3>{stop.address ? <p>{stop.address}</p> : null}{stop.departureTime ? <p>Kalkış: {stop.departureTime}{model.timeZoneId ? ` (${model.timeZoneId})` : ''}</p> : null}</li>)}</ul></section> : null}
  </div>
}
