# Summarize CLI Usage Reference

This reference is intentionally usage-first. It covers how to run `summarize` for common tasks and debug failures quickly.

## Quick Start

Install:

```bash
npm i -g @steipete/summarize
```

Run once without installing globally:

```bash
npx -y @steipete/summarize "https://example.com"
```

## Common Commands

```bash
# URL
summarize "https://example.com"

# Local file
summarize "/path/to/file.pdf"

# Stdin
cat notes.md | summarize -

# YouTube/video
summarize "https://youtu.be/<id>"
```

## Pick a Model

Use `<provider>/<model>` when you need deterministic behavior:

```bash
summarize "https://example.com" --model openai/gpt-5-mini
summarize "https://example.com" --model google/gemini-3-flash-preview
```

Set the matching API key for the provider you use, for example:

```bash
export OPENAI_API_KEY="..."
```

## Most Useful Flags

```bash
--length short|medium|long|<chars>
--language <lang>
--extract
--format md|text
--json
--verbose
```

Examples:

```bash
summarize "https://example.com" --length long --language en
summarize "https://example.com" --extract --format md
summarize "https://example.com" --json --verbose
```

## Defaults via Config

Config path: `~/.summarize/config.json`

```json
{
  "model": "openai/gpt-5-mini",
  "env": { "OPENAI_API_KEY": "sk-..." }
}
```

Model precedence:

1. `--model`
2. `SUMMARIZE_MODEL`
3. `~/.summarize/config.json`
4. default `auto`

## Troubleshooting

`summarize: command not found`
- Reinstall `@steipete/summarize`, then retry your original command.

`Auth/model errors`
- Check the API key variable for the selected provider.
- Retry with a known-good explicit model.

`Output quality is poor or incomplete`
- Retry with `--extract --format md`.
- Increase detail with `--length long`.

`Need inspectable output`
- Add `--json` for structured output.
- Add `--verbose` for detailed logs.

`YouTube/media failures`
- Ensure `yt-dlp` and `ffmpeg` are installed.
- For slide OCR flows, install `tesseract` and use `--slides-ocr`.
