# Maton API Gateway

API gateway for calling third-party APIs with managed auth.

Call native API endpoints directly with a single API key.

## Quick Start

```bash
# Send a Slack message
curl -s -X POST 'https://gateway.maton.ai/slack/api/chat.postMessage' \
  -H 'Content-Type: application/json' \
  -H 'Authorization: Bearer YOUR_API_KEY' \
  -d '{"channel": "C0123456", "text": "Hello from gateway!"}'
```

## Getting Your API Key

1. Sign in or create an account at [maton.ai](https://maton.ai)
2. Go to [maton.ai/settings](https://maton.ai/settings)
3. Click the copy button to copy your API key

```bash
export MATON_API_KEY="YOUR_API_KEY"
```
