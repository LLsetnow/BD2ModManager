import { spawnSync } from 'node:child_process'
import { copyFileSync, existsSync, mkdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import path from 'node:path'

const root = fileURLToPath(new URL('../', import.meta.url))
const resourcesDir = path.join(root, 'src-tauri/resources')
const configuredGameRoot = process.env.BD2_GAME_ROOT ?? 'F:\\Neowiz\\Browndust2\\Browndust2_10000001'
const gameReferencesPresent = [
  path.join(configuredGameRoot, 'BrownDust II_Data/Managed/UnityEngine.CoreModule.dll'),
  path.join(configuredGameRoot, 'BepInEx/core/0Harmony.dll'),
].every(existsSync)

mkdirSync(resourcesDir, { recursive: true })

if (!gameReferencesPresent) {
  const bundledPluginsPresent = ['BD2PreviewInteractions.dll', 'BD2CostumeSkins.dll']
    .every(name => existsSync(path.join(resourcesDir, name)))

  if (!bundledPluginsPresent) {
    throw new Error('Game assemblies are unavailable and prebuilt plugin DLLs are missing from src-tauri/resources.')
  }

  console.log('Game assemblies are unavailable; using the checked-in plugin DLLs.')
  process.exit(0)
}

for (const name of ['BD2PreviewInteractions', 'BD2CostumeSkins']) {
  const project = path.join(root, `plugins/${name}/${name}.csproj`)
  const args = ['build', project, '-c', 'Release', '-v:q', '-clp:ErrorsOnly']
  args.push(`-p:GameRoot=${configuredGameRoot}`)
  const result = spawnSync('dotnet', args, { stdio: 'inherit', cwd: root })
  if (result.error) throw result.error
  if (result.status !== 0) process.exit(result.status || 1)
  copyFileSync(path.join(root, `plugins/${name}/bin/Release/netstandard2.1/${name}.dll`), path.join(resourcesDir, `${name}.dll`))
}
