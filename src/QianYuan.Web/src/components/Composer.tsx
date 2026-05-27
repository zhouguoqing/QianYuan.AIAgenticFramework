import { useEffect, useRef, useState } from 'react'
import type { ImagePart } from '../types/api'

interface Props {
  busy: boolean
  onSubmit: (text: string, images: ImagePart[]) => void
  onAbort: () => void
}

export function Composer({ busy, onSubmit, onAbort }: Props) {
  const [text, setText] = useState('')
  const [images, setImages] = useState<{ url: string; mime: string }[]>([])
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
    if (!t && images.length === 0) return
    const parts: ImagePart[] = images.map(i => ({ url: i.url, mime: i.mime }))
    onSubmit(t, parts)
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
      <div className="composer-row">
        <textarea
          ref={ref}
          value={text}
          placeholder={busy ? '正在生成…' : '输入消息,Enter 发送 · Shift+Enter 换行'}
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
          : <button className="send" onClick={submit} disabled={!text.trim() && images.length === 0}>发送</button>
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
          + 图片
          <input type="file" accept="image/*" multiple style={{ display: 'none' }}
                 onChange={e => Array.from(e.target.files ?? []).forEach(pickFile)} />
        </label>
      </div>
    </div>
  )
}
