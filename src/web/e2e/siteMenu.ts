import type { Page } from '@playwright/test'

/**
 * On narrow viewports the site header keeps its links inside a disclosure panel. Call this before reaching for a
 * header link: it opens the panel when (and only when) the menu button is shown and the panel is closed.
 */
export async function openSiteMenuIfCollapsed(page: Page) {
  const button = page.locator('.site-menu-button')
  if (!(await button.isVisible())) return
  if ((await button.getAttribute('aria-expanded')) === 'true') return
  await button.click()
}
