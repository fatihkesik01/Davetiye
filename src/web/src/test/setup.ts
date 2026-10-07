import { cleanup, configure } from '@testing-library/react'
import { afterEach } from 'vitest'

import '../i18n'

// Lazy route chunks can take longer than the 1s default to resolve on loaded CI runners.
configure({ asyncUtilTimeout: 5000 })

afterEach(() => {
  cleanup()
})
