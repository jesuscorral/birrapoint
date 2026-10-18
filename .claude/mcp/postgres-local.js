#!/usr/bin/env node
// Launches Postgres MCP Pro (crystaldba/postgres-mcp) against the local Aspire Postgres in
// read-only "restricted" mode. Aspire generates the password and host port, so they are discovered
// from the running container instead of being configured. The MCP container joins Aspire's Docker
// network and reaches Postgres by container name (postgres-mcp rewrites `localhost` to
// host.docker.internal, so localhost can't be used). The connection string is passed through the
// environment, never on the command line. If the Aspire stack isn't running, the server fails to
// start and agents fall back to migrations and data-model.md.
const { execFileSync, spawn } = require('child_process');

const fail = msg => { console.error(`postgres-local: ${msg}`); process.exit(1); };
const docker = args => execFileSync('docker', args, { stdio: ['ignore', 'pipe', 'ignore'], timeout: 10000 }).toString().trim();

let container;
try {
  container = docker(['ps', '--filter', 'name=^postgres-', '--filter', 'status=running', '--format', '{{.Names}}'])
    .split('\n').filter(Boolean)[0];
} catch { fail('Docker is not running.'); }
if (!container) fail('Aspire Postgres container (postgres-*) is not running. Start the stack: dotnet run --project backend/src/BirraPoint.AppHost');

const env = Object.fromEntries(
  docker(['inspect', '-f', '{{range .Config.Env}}{{println .}}{{end}}', container])
    .split('\n').filter(l => l.includes('=')).map(l => [l.slice(0, l.indexOf('=')), l.slice(l.indexOf('=') + 1)]));
const user = env.POSTGRES_USER || 'postgres';
if (!env.POSTGRES_PASSWORD) fail(`POSTGRES_PASSWORD not found on ${container}.`);
const database = process.env.POSTGRES_LOCAL_DB || 'birrapoint';
const network = docker(['inspect', '-f', '{{range $k, $v := .NetworkSettings.Networks}}{{println $k}}{{end}}', container])
  .split('\n').filter(Boolean)[0];
if (!network) fail(`${container} has no Docker network.`);

const child = spawn('docker', [
  'run', '-i', '--rm', '--network', network, '-e', 'DATABASE_URI',
  'crystaldba/postgres-mcp:latest', '--access-mode=restricted', '--transport=stdio'
], {
  stdio: 'inherit',
  env: {
    ...process.env,
    DATABASE_URI: `postgresql://${encodeURIComponent(user)}:${encodeURIComponent(env.POSTGRES_PASSWORD)}@${container}:5432/${database}`
  }
});
child.on('exit', code => process.exit(code ?? 1));
for (const sig of ['SIGINT', 'SIGTERM']) process.on(sig, () => child.kill(sig));
