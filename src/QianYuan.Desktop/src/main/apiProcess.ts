import { ChildProcess, spawn } from 'node:child_process'
import fs from 'node:fs'
import http from 'node:http'
import log from 'electron-log/main'
import { apiProjectPath, packagedApiExecutable, repoRoot } from './paths.js'

export interface ApiRuntime {
  url: string
  stop: () => Promise<void>
}

export async function startApi(port: number): Promise<ApiRuntime> {
  const url = `http://127.0.0.1:${port}`
  if (await isHealthy(url)) return { url, stop: async () => undefined }

  const child = spawnApi(port)
  await waitForHealthy(url, 60_000)
  return {
    url,
    stop: async () => stopProcess(child),
  }
}

function spawnApi(port: number): ChildProcess {
  const packagedApi = packagedApiExecutable()
  if (fs.existsSync(packagedApi)) {
    log.info('Starting packaged API', packagedApi)
    return attachLogging(spawn(packagedApi, ['--urls', `http://127.0.0.1:${port}`], buildSpawnOptions()))
  }

  const project = apiProjectPath()
  log.info('Starting API from project', project)
  return attachLogging(spawn('dotnet', ['run', '--project', project, '--urls', `http://127.0.0.1:${port}`], buildSpawnOptions()))
}

function attachLogging(child: ChildProcess): ChildProcess {
  child.stdout?.on('data', chunk => log.info(`[api] ${chunk.toString().trimEnd()}`))
  child.stderr?.on('data', chunk => log.error(`[api] ${chunk.toString().trimEnd()}`))
  return child
}

function buildSpawnOptions() {
  return {
    cwd: repoRoot(),
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: process.env.ASPNETCORE_ENVIRONMENT ?? 'Production',
    },
    stdio: 'pipe' as const,
  }
}

async function stopProcess(child: ChildProcess): Promise<void> {
  if (child.exitCode !== null || child.signalCode !== null) return
  await new Promise<void>(resolve => {
    child.once('exit', () => resolve())
    child.kill('SIGTERM')
    setTimeout(() => {
      if (child.exitCode === null) child.kill('SIGKILL')
      resolve()
    }, 3_000).unref()
  })
}

async function waitForHealthy(url: string, timeoutMs: number): Promise<void> {
  const startedAt = Date.now()
  while (Date.now() - startedAt < timeoutMs) {
    if (await isHealthy(url)) return
    await new Promise(resolve => setTimeout(resolve, 500))
  }
  throw new Error(`QianYuan.Api did not become healthy within ${timeoutMs}ms`)
}

async function isHealthy(url: string): Promise<boolean> {
  return new Promise(resolve => {
    const req = http.get(`${url}/api/plans`, res => {
      res.resume()
      resolve(res.statusCode !== undefined && res.statusCode < 500)
    })
    req.on('error', () => resolve(false))
    req.setTimeout(1_500, () => {
      req.destroy()
      resolve(false)
    })
  })
}