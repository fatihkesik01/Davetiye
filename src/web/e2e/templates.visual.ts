import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'

const templateKeys = [
  'zamansiz-dugun',
  'romantik-nisan',
  'gece-kina',
  'neseli-sunnet',
  'renkli-dogum-gunu',
  'pastel-baby-shower',
  'modern-mezuniyet',
  'minimal-acilis',
] as const

for (const templateKey of templateKeys) {
  test(`${templateKey} renderer is responsive, accessible and visually stable`, async ({ page }, testInfo) => {
    await page.goto(`/__visual/templates/${templateKey}`)
    if (testInfo.project.name === 'chromium-200pct') {
      await page.evaluate(() => { document.documentElement.style.zoom = '2' })
    }
    const renderer = page.locator('.invitation-renderer')
    await expect(renderer).toHaveAttribute('data-renderer', `${templateKey}@1`)

    const viewportMetrics = await page.evaluate(() => ({
      clientWidth: document.documentElement.clientWidth,
      scrollWidth: document.documentElement.scrollWidth,
    }))
    expect(viewportMetrics.scrollWidth).toBeLessThanOrEqual(viewportMetrics.clientWidth)

    const accessibility = await new AxeBuilder({ page })
      .include('.invitation-renderer')
      .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
      .analyze()
    expect(accessibility.violations).toEqual([])

    await expect(page).toHaveScreenshot(`${templateKey}.png`, { fullPage: true })
  })
}
