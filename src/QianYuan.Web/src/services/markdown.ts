import { marked } from 'marked'
import DOMPurify from 'dompurify'
import mermaid from 'mermaid'
import hljs from 'highlight.js/lib/core'

// ---- Register popular languages eagerly ----
import javascript from 'highlight.js/lib/languages/javascript'
import typescript from 'highlight.js/lib/languages/typescript'
import python from 'highlight.js/lib/languages/python'
import bash from 'highlight.js/lib/languages/bash'
import json from 'highlight.js/lib/languages/json'
import xml from 'highlight.js/lib/languages/xml'
import css from 'highlight.js/lib/languages/css'
import sql from 'highlight.js/lib/languages/sql'
import yaml from 'highlight.js/lib/languages/yaml'
import markdown from 'highlight.js/lib/languages/markdown'
import c from 'highlight.js/lib/languages/c'
import cpp from 'highlight.js/lib/languages/cpp'
import csharp from 'highlight.js/lib/languages/csharp'
import go from 'highlight.js/lib/languages/go'
import java from 'highlight.js/lib/languages/java'
import rust from 'highlight.js/lib/languages/rust'
import php from 'highlight.js/lib/languages/php'
import ruby from 'highlight.js/lib/languages/ruby'
import swift from 'highlight.js/lib/languages/swift'
import kotlin from 'highlight.js/lib/languages/kotlin'
import dart from 'highlight.js/lib/languages/dart'
import shell from 'highlight.js/lib/languages/shell'
import dockerfile from 'highlight.js/lib/languages/dockerfile'
import ini from 'highlight.js/lib/languages/ini'
import diff from 'highlight.js/lib/languages/diff'
import plaintext from 'highlight.js/lib/languages/plaintext'

hljs.registerLanguage('javascript',javascript); hljs.registerLanguage('js',javascript)
hljs.registerLanguage('typescript',typescript); hljs.registerLanguage('ts',typescript)
hljs.registerLanguage('python',python); hljs.registerLanguage('py',python)
hljs.registerLanguage('bash',bash); hljs.registerLanguage('sh',bash); hljs.registerLanguage('shell',shell)
hljs.registerLanguage('json',json); hljs.registerLanguage('xml',xml); hljs.registerLanguage('html',xml)
hljs.registerLanguage('css',css); hljs.registerLanguage('sql',sql); hljs.registerLanguage('yaml',yaml)
hljs.registerLanguage('markdown',markdown); hljs.registerLanguage('md',markdown)
hljs.registerLanguage('c',c); hljs.registerLanguage('cpp',cpp); hljs.registerLanguage('csharp',csharp)
hljs.registerLanguage('go',go); hljs.registerLanguage('java',java); hljs.registerLanguage('rust',rust)
hljs.registerLanguage('php',php); hljs.registerLanguage('ruby',ruby); hljs.registerLanguage('swift',swift)
hljs.registerLanguage('kotlin',kotlin); hljs.registerLanguage('dart',dart)
hljs.registerLanguage('dockerfile',dockerfile); hljs.registerLanguage('docker',dockerfile)
hljs.registerLanguage('ini',ini); hljs.registerLanguage('diff',diff)
hljs.registerLanguage('plaintext',plaintext); hljs.registerLanguage('text',plaintext)

marked.setOptions({ gfm: true, breaks: true })
mermaid.initialize({ startOnLoad: false, theme: 'default', securityLevel: 'strict' })

// ---- KaTeX lazy load ----
let katex: any = null
async function getKatex() {
  if (katex) return katex
  try { const mod = await import('katex'); katex = mod.default || mod; return katex } catch { return null }
}

// ---- Highlight helper ----
function highlightCode(code: string, lang: string | undefined): string {
  const language = (lang ?? '').trim().toLowerCase()
  // Mermaid / UML
  if (language === 'mermaid' || language === 'uml' || language === 'plantuml') {
    const escaped = escapeHtml(code)
    if (language === 'plantuml') return `<pre class="uml-fallback"><code>${escaped}</code></pre>`
    return `<pre class="mermaid uml-diagram">${escaped}</pre>`
  }
  let highlighted: string
  try {
    const hlLang = language && hljs.getLanguage(language)
    highlighted = hlLang
      ? hljs.highlight(code, { language, ignoreIllegals: true }).value
      : hljs.highlightAuto(code).value
  } catch { highlighted = escapeHtml(code) }
  const displayLang = language || 'text'
  return `<div class="code-block-wrapper"><div class="code-block-header"><span class="code-lang">${escapeHtml(displayLang)}</span><button class="code-copy-btn" onclick="var p=this.closest('.code-block-wrapper').querySelector('code');navigator.clipboard.writeText(p.textContent||'').then(()=>{this.textContent='✓ 已复制';this.classList.add('copied');setTimeout(()=>{this.textContent='复制';this.classList.remove('copied')},1500)}).catch(()=>{})">复制</button></div><pre><code class="hljs">${highlighted}</code></pre></div>`
}

// ---- Custom Renderer ----
const renderer = new marked.Renderer()
renderer.code = ({ text, lang }) => highlightCode(text, lang)
renderer.codespan = ({ text }) => `<code>${escapeHtml(text)}</code>`

export function renderMarkdown(md: string): string {
  const html = marked.parse(md, { async: false, renderer }) as string
  return DOMPurify.sanitize(html, { ADD_ATTR: ['target', 'class', 'onclick'] })
}

// ---- Post-render: process KaTeX in the DOM ----
export async function postProcessKatex(container: Element) {
  const ktx = await getKatex()
  if (!ktx) return
  // Find text nodes with $$...$$ or $...$
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT)
  const replacements: { node: Text; html: string }[] = []
  let node: Text | null
  while ((node = walker.nextNode() as Text | null)) {
    const text = node.textContent || ''
    if (!text.includes('$')) continue
    const parent = node.parentElement
    if (!parent || parent.tagName === 'CODE' || parent.tagName === 'PRE' || parent.closest('.katex, .katex-block-wrapper')) continue
    let result = text
    let changed = false
    // Block first $$...$$
    result = result.replace(/\$\$([^$]+)\$\$/g, (_, f) => {
      changed = true
      try { return ktx.renderToString(f.trim(), { throwOnError: false, displayMode: true, trust: false }) }
      catch { return `$$${f}$$` }
    })
    // Inline $...$
    result = result.replace(/\$([^$]+)\$/g, (_, f) => {
      changed = true
      try { return ktx.renderToString(f.trim(), { throwOnError: false, displayMode: false, trust: false }) }
      catch { return `$${f}$` }
    })
    if (changed) replacements.push({ node, html: result })
  }
  // Apply replacements (create temp div, set innerHTML, replace)
  for (const { node: n, html } of replacements) {
    const span = document.createElement('span')
    span.innerHTML = html
    n.parentNode?.replaceChild(span, n)
  }
}

export function renderUmlDiagrams() {
  requestAnimationFrame(() => {
    mermaid.run({ querySelector: '.mermaid:not([data-processed="true"])' }).catch(() => {})
  })
}

function escapeHtml(value: string): string {
  return value.replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;').replaceAll('"','&quot;').replaceAll("'",'&#39;')
}
