import './ProgramListEditor.css'

import { useTranslation } from 'react-i18next'

import {
  programLimits,
  sortProgramItemsByTime,
  validateProgramItems,
  type EditableProgramItem,
} from './draftModel'

interface ProgramListEditorProps {
  items: EditableProgramItem[]
  onChange: (items: EditableProgramItem[]) => void
}

/**
 * The schedule ("Günün akışı") as a list: add, edit, remove and reorder with plain buttons (no drag
 * only). Lines are shown in the order listed; a hint appears when their times are out of order and a
 * button sorts them by time on request.
 */
export function ProgramListEditor({ items, onChange }: ProgramListEditorProps) {
  const { t } = useTranslation()
  const validation = validateProgramItems(items)
  const atLimit = items.length >= programLimits.maxItems
  const patch = (index: number, change: Partial<EditableProgramItem>) =>
    onChange(items.map((item, position) => position === index ? { ...item, ...change } : item))
  const move = (index: number, delta: -1 | 1) => {
    const target = index + delta
    if (target < 0 || target >= items.length) return
    const next = [...items]
    const moved = next[index]!
    next[index] = next[target]!
    next[target] = moved
    onChange(next)
  }

  return <section className="program-editor" aria-labelledby="draft-program-section">
    <h3 id="draft-program-section" tabIndex={-1}>{t('creatorFormsUi.editor.program.heading')}</h3>
    <p className="form-field__help">{t('creatorFormsUi.editor.optionalIntro')}</p>
    <p className="program-editor__counter" role="status" aria-live="polite">
      {t('creatorFormsUi.editor.program.counter', { count: items.length, max: programLimits.maxItems })}
    </p>
    {items.length === 0 ? <p className="form-field__help">{t('creatorFormsUi.editor.program.empty')}</p> : null}
    {validation.tooMany ? <p className="form-status form-status--error" role="alert">{t('creatorFormsUi.editor.program.tooMany', { max: programLimits.maxItems })}</p> : null}
    {validation.outOfOrder ? <div className="program-editor__hint" role="status">
      <p>{t('creatorFormsUi.editor.program.outOfOrder')}</p>
      <button type="button" className="button button--secondary" onClick={() => onChange(sortProgramItemsByTime(items))}>{t('creatorFormsUi.editor.program.sortByTime')}</button>
    </div> : null}
    <ol className="program-editor__list">
      {items.map((item, index) => {
        const number = index + 1
        const problems = validation.items[index] ?? {}
        const errorId = `draft-program-${index}-errors`
        const hasProblem = Boolean(problems.titleTooLong || problems.descriptionTooLong || problems.controlCharacters)
        return <li key={index} className="program-editor__item">
          <fieldset>
            <legend>{t('creatorFormsUi.editor.program.item', { index: number })}</legend>
            <div className="form-field">
              <label htmlFor={`draft-program-time-${index}`}>{t('creatorFormsUi.editor.programTime')}</label>
              <input id={`draft-program-time-${index}`} type="datetime-local" value={item.startsAtLocal} aria-label={t('creatorFormsUi.editor.program.timeLabel', { index: number })} onChange={event => patch(index, { startsAtLocal: event.target.value })} />
            </div>
            <div className="form-field">
              <label htmlFor={`draft-program-title-${index}`}>{t('creatorFormsUi.editor.program.title')}</label>
              <input id={`draft-program-title-${index}`} type="text" value={item.title} maxLength={programLimits.maxTitle} aria-label={t('creatorFormsUi.editor.program.titleLabel', { index: number })} aria-invalid={problems.titleTooLong || problems.controlCharacters ? true : undefined} aria-describedby={hasProblem ? errorId : undefined} onChange={event => patch(index, { title: event.target.value })} />
            </div>
            <div className="form-field">
              <label htmlFor={`draft-program-description-${index}`}>{t('creatorFormsUi.editor.program.description')}</label>
              <input id={`draft-program-description-${index}`} type="text" value={item.description} maxLength={programLimits.maxDescription} aria-label={t('creatorFormsUi.editor.program.descriptionLabel', { index: number })} aria-invalid={problems.descriptionTooLong || problems.controlCharacters ? true : undefined} aria-describedby={hasProblem ? errorId : undefined} onChange={event => patch(index, { description: event.target.value })} />
            </div>
            {hasProblem ? <p id={errorId} className="form-status form-status--error" role="alert">
              {problems.titleTooLong ? t('creatorFormsUi.editor.program.titleTooLong', { max: programLimits.maxTitle }) : null}{' '}
              {problems.descriptionTooLong ? t('creatorFormsUi.editor.program.descriptionTooLong', { max: programLimits.maxDescription }) : null}{' '}
              {problems.controlCharacters ? t('creatorFormsUi.editor.program.controlCharacters') : null}
            </p> : null}
            <div className="button-row program-editor__actions">
              <button type="button" className="button button--secondary" disabled={index === 0} aria-label={t('creatorFormsUi.editor.program.moveUp', { index: number })} onClick={() => move(index, -1)}>↑</button>
              <button type="button" className="button button--secondary" disabled={index === items.length - 1} aria-label={t('creatorFormsUi.editor.program.moveDown', { index: number })} onClick={() => move(index, 1)}>↓</button>
              <button type="button" className="button button--secondary" aria-label={t('creatorFormsUi.editor.program.remove', { index: number })} onClick={() => onChange(items.filter((_, position) => position !== index))}>{t('creatorFormsUi.editor.program.removeShort')}</button>
            </div>
          </fieldset>
        </li>
      })}
    </ol>
    <button type="button" className="button button--secondary" disabled={atLimit} onClick={() => onChange([...items, { title: '', description: '', startsAtLocal: '' }])}>{t('creatorFormsUi.editor.program.add')}</button>
    {atLimit ? <p className="form-field__help" role="status">{t('creatorFormsUi.editor.program.limitReached', { max: programLimits.maxItems })}</p> : null}
  </section>
}
