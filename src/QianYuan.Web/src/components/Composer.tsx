import { useEffect, useRef, useState } from 'react'
import type { ComposerMode, ImagePart } from '../types/api'

interface Props {
  busy: boolean
  onSubmit: (text: string, images: ImagePart[], mode: ComposerMode) => void
  onAbort: () => void
}

export function Composer({ busy, onSubmit, onAbort }: Props) {
  const [text, setText] = useState('')
  const [images, setImages] = useState<{ url: string; mime: string }[]>([])
  const [mode, setMode] = useState<ComposerMode>('chat')
  const ref = useRef<HTMLTextAreaElement>(null)

  useEffect(() => { ref.current?.focus() }, [])

  function autoSize(el: HTMLTextAreaElement) {
    el.style.height = 'auto'
    el.style.height = Math.min(220, el.scrollHeight) + 'px'
  }

  function pickFile(file: File) {
    const reader = new FileReader()
    reader.onload = () => {
      const url = reader.result as string
      setImages(prev => [...prev, { url, mime: file.type || 'image/png' }])
    }
    reader.readAsDataURL(file)
  }

  function submit() {
    const t = text.trim()
    if (!canSubmit(text, images.length, mode)) return
    const parts: ImagePart[] = images.map(i => ({ url: i.url, mime: i.mime }))
    onSubmit(t, parts, mode)
    setText('')
    setImages([])
    if (ref.current) ref.current.style.height = '60px'
  }

  function onKey(e: React.KeyboardEvent) {
    // Enter sends. Shift+Enter (or IME composition) inserts a newline.
    if (e.key === 'Enter' && !e.shiftKey && !(e.nativeEvent as any).isComposing) {
      e.preventDefault(); submit()
    }
  }

  return (
    <div className="composer">
      <div className="mode-tabs" role="tablist" aria-label="输入模式">
        <button className={mode === 'chat' ? 'active' : ''} onClick={() => setMode('chat')} type="button">聊天</button>
        <button className={mode === 'text-to-image' ? 'active' : ''} onClick={() => setMode('text-to-image')} type="button">文生图</button>
        <button className={mode === 'image-to-image' ? 'active' : ''} onClick={() => setMode('image-to-image')} type="button">图生图</button>
      </div>
      <div className="composer-row">
        <textarea
          ref={ref}
          value={text}
          placeholder={busy ? '正在生成…' : placeholderFor(mode)}
          onChange={e => { setText(e.target.value); autoSize(e.target) }}
          onKeyDown={onKey}
          onPaste={e => {
            for (const item of Array.from(e.clipboardData.items)) {
              if (item.type.startsWith('image/')) {
                const f = item.getAsFile(); if (f) pickFile(f)
              }
            }
          }}
        />
        {busy
          ? <button className="send" onClick={onAbort}>中止</button>
          : <button className="send" onClick={submit} disabled={!canSubmit(text, images.length, mode)}>{mode === 'chat' ? '发送' : '生成'}</button>
        }
      </div>
      <div className="images">
        {images.map((i, idx) => (
          <div key={idx} style={{ position: 'relative' }}>
            <img src={i.url} alt="" />
            <span onClick={() => setImages(p => p.filter((_, j) => j !== idx))}
                  style={{ position: 'absolute', top: 0, right: 4, cursor: 'pointer', color: '#fff', background: '#000a', borderRadius: 8, padding: '0 4px' }}>×</span>
          </div>
        ))}
        <label style={{ cursor: 'pointer', padding: '6px 10px', border: '1px dashed #888', borderRadius: 6, color: '#9aa0a8' }}>
          + 参考图
          <input type="file" accept="image/*" multiple style={{ display: 'none' }}
                 onChange={e => Array.from(e.target.files ?? []).forEach(pickFile)} />
        </label>
      </div>
    </div>
  )
}

function placeholderFor(mode: ComposerMode): string {
  if (mode === 'text-to-image') return '描述要生成的图片,Enter 生成 · Shift+Enter 换行'
  if (mode === 'image-to-image') return '上传参考图并描述改造方向,Enter 生成 · Shift+Enter 换行'
  return '输入消息,Enter 发送 · Shift+Enter 换行'
}

function canSubmit(text: string, imageCount: number, mode: ComposerMode): boolean {
  const hasText = text.trim().length > 0
  if (mode === 'chat') return hasText || imageCount > 0
  if (mode === 'text-to-image') return hasText
  return hasText && imageCount > 0
}
