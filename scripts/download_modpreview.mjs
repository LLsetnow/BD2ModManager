import { createWriteStream, mkdirSync } from 'fs'
import { pipeline } from 'stream/promises'
import { get } from 'https'

const URL = 'https://github.com/LLsetnow/BD2ModPreview/releases/latest/download/BD2ModPreview.exe'
const OUT = 'src-tauri/resources/BD2ModPreview.exe'

mkdirSync('src-tauri/resources', { recursive: true })

function download(url) {
  get(url, { headers: { 'User-Agent': 'BD2ModManager' } }, async res => {
    if (res.statusCode === 301 || res.statusCode === 302) {
      download(res.headers.location)
    } else if (res.statusCode === 200) {
      try {
        await pipeline(res, createWriteStream(OUT))
        console.log('Download completed successfully')
        process.exit(0)
      } catch (err) {
        console.error(`Failed to save file: ${err.message}`)
        process.exit(1)
      }
    } else {
      console.error(`Unexpected status: ${res.statusCode}`)
      process.exit(1)
    }
  }).on('error', err => {
    console.error(`Download failed: ${err.message}`)
    process.exit(1)
  })
}

download(URL)
