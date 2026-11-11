import { spawnSync } from 'node:child_process'
import { copyFileSync, mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const root = fileURLToPath(new URL('../', import.meta.url))
mkdirSync(path.join(root, 'src-tauri/resources'), { recursive: true })
for (const name of ['BD2PreviewInteractions', 'BD2CostumeSkins']) {
  const project = path.join(root, `plugins/${name}/${name}.csproj`)
  const args = ['build', project, '-c', 'Release', '-v:q', '-clp:ErrorsOnly']
  if (process.env.BD2_GAME_ROOT) args.push(`-p:GameRoot=${process.env.BD2_GAME_ROOT}`)
  const result = spawnSync('dotnet', args, { stdio: 'inherit', cwd: root })
  if (result.error) throw result.error
  if (result.status !== 0) process.exit(result.status || 1)
  copyFileSync(path.join(root, `plugins/${name}/bin/Release/netstandard2.1/${name}.dll`), path.join(root, `src-tauri/resources/${name}.dll`))
}
