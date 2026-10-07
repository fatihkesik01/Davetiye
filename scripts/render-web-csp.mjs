import { readFile, writeFile } from 'node:fs/promises'

const [templatePath, outputPath] = process.argv.slice(2)
if (!templatePath || !outputPath) {
  throw new Error('Usage: node render-web-csp.mjs <template-path> <output-path>')
}

function httpsOrigin(value, name) {
  if (!value) return ''
  let parsed
  try {
    parsed = new URL(value)
  } catch {
    throw new Error(`${name} must be an absolute HTTPS origin.`)
  }
  if (parsed.protocol !== 'https:' || parsed.origin !== value || parsed.username || parsed.password ||
    parsed.pathname !== '/' || parsed.search || parsed.hash) {
    throw new Error(`${name} must be an HTTPS origin without credentials, path, query or fragment.`)
  }
  return parsed
}

const worker = httpsOrigin(process.env.CSP_MEDIA_WORKER_ORIGIN ?? '', 'CSP_MEDIA_WORKER_ORIGIN')
const stream = httpsOrigin(process.env.CSP_STREAM_CUSTOMER_ORIGIN ?? '', 'CSP_STREAM_CUSTOMER_ORIGIN')
if (Boolean(worker) !== Boolean(stream)) {
  throw new Error('Both CSP media origins must be configured together.')
}
if (stream && !/^[a-z0-9-]+\.cloudflarestream\.com$/iu.test(stream.hostname)) {
  throw new Error('CSP_STREAM_CUSTOMER_ORIGIN must use a *.cloudflarestream.com hostname.')
}

const template = await readFile(templatePath, 'utf8')
const rendered = template
  .replaceAll('@@CSP_MEDIA_WORKER_ORIGIN@@', worker?.origin ?? '')
  .replaceAll('@@CSP_STREAM_UPLOAD_ORIGIN@@', worker ? 'https://upload.videodelivery.net' : '')
  .replaceAll('@@CSP_STREAM_CUSTOMER_ORIGIN@@', stream?.origin ?? '')

if (rendered.includes('@@CSP_')) throw new Error('The web CSP template contains an unknown placeholder.')
await writeFile(outputPath, rendered)
