import type { TemplateCatalogItem } from '../../../api/generated/client'

export function templateCategoryLabel(template: Pick<TemplateCatalogItem, 'key' | 'category'>): string {
  return template.key === 'zarf-davetiye' ? 'Düğün / Tarih Duyurusu' : template.category
}
