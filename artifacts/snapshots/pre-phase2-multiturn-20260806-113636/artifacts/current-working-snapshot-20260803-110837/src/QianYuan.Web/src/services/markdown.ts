import { marked } from 'marked'
import DOMPurify from 'dompurify'
import mermaid from 'mermaid'

marked.setOptions({ gfm: true, breaks: true })
mermaid.initialize({ startOnLoad: false, theme: 'dark', securityLevel: 'strict' })

const renderer = new marked.Renderer()
renderer.code = ({ text, lang }) => {
  const language = (lang ?? '').trim().toLowerCase()
  if (language === 'mermaid' || language === 'uml' || language === 'plantuml') {
    const escaped = escapeHtml(text)
    if (language === 'plantuml') return `<pre class="uml-fallback"><code>${escaped}</code></pre>`
    return `<pre class="mermaid uml-diagram">${escaped}</pre>`
  }
  return `<pre><code class="language-${escapeHtml(language)}">${escapeHtml(text)}</code></pre>`
}

export function renderMarkdown(md: string): string {
  const html = marked.parse(md, { async: false, renderer }) as string
  return DOMPurify.sanitize(html)
}

export function renderUmlDiagrams() {
  requestAnimationFrame(() => {
    mermaid.run({ querySelector: '.mermaid:not([data-processed="true"])' }).catch(() => {})
  })
}

function escapeHtml(value: string): string {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;')
}
