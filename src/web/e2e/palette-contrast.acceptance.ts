import AxeBuilder from '@axe-core/playwright'
import { expect, test } from '@playwright/test'
import { installApi, palettes, scenarios, seedPreferences, sessions, type Appearance, type Palette, type Preferences } from './uiFixtures'

// Colour-contrast and WCAG A/AA/2.2 AA sweep over every themed screen. Each screen is loaded once, then the palette
// and appearance are switched through the same data attributes the preference provider sets, so the whole
// matrix stays cheap. Violations are fixed in the design tokens, never by relaxing the requirement.
type Combo = [Palette, Exclude<Appearance, 'system'>]
const full: Combo[] = palettes.flatMap(palette => (['light', 'dark'] as const).map((appearance): Combo => [palette, appearance]))
const reduced: Combo[] = [['kutlio', 'light'], ['rose', 'dark'], ['plum', 'dark'], ['sage', 'light']]

for (const scenario of scenarios) {
  test(`${scenario.name} keeps text, borders and controls legible in every palette and appearance`, async ({ page }, testInfo) => {
    const combos = testInfo.project.name === 'chromium-desktop' ? full : reduced
    const start: Preferences = { locale: 'tr', colorTheme: 'kutlio', appearance: 'light', avatar: 'berry' }
    await seedPreferences(page, start)
    await installApi(page, { session: sessions[scenario.session], preferences: start, subscription: scenario.subscription })
    await page.goto(scenario.path)
    await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible()
    await page.waitForLoadState('networkidle')
    if (scenario.path.includes('/sablonlar')) await expect(page.locator('.invitation-renderer, img[src^="/template-previews/"]').first()).toBeVisible()
    if (scenario.reveal) await scenario.reveal(page)
    await page.waitForLoadState('networkidle')
    if (testInfo.project.name === 'chromium-200pct') await page.evaluate(() => { document.documentElement.style.zoom = '2' })

    const report: string[] = []
    for (const [colorTheme, appearance] of combos) {
      await page.evaluate(([theme, mode]) => { document.documentElement.dataset.colorTheme = theme; document.documentElement.dataset.appearance = mode }, [colorTheme, appearance])
      // Reduced-motion CSS turns every property change into a one-frame transition; let it settle before measuring.
      await page.evaluate(() => new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve()))))
      const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'])
        // Template renderers own their palettes and are covered by the dedicated visual baselines.
        .exclude('.invitation-renderer').analyze()
      for (const violation of result.violations) {
        for (const node of violation.nodes) report.push(`${colorTheme}-${appearance} | ${violation.id} | ${node.target.join(' ')} | ${(node.any[0]?.message ?? node.failureSummary ?? '').split('\n')[0]}`)
      }
    }
    expect(report).toEqual([])
  })
}
