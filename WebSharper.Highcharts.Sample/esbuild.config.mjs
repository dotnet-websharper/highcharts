import { build } from 'esbuild'
import { readFileSync, writeFileSync, mkdirSync, copyFileSync } from 'fs'

const name = 'WebSharper.Highcharts.Sample'

await build({
  entryPoints: [`./build/${name}.js`],
  bundle: true,
  minify: true,
  format: 'iife',
  outfile: `../dist/Scripts/${name}.min.js`,
  globalName: 'wsbundle',
  // Point the `jquery` import used by older WebSharper.JQuery releases at the
  // global instance Highcharts extends (see jquery-shim.js).
  alias: { jquery: './jquery-shim.js' }
})

mkdirSync('../dist/css', { recursive: true })
copyFileSync('./css/index.css', '../dist/css/index.css')

// Inject the WebSharper head: the jQuery <script> tag linked by the
// WebSharper.JQuery resource. Highcharts is bundled from npm, so no
// <script> tag is generated for it.
const head = readFileSync(`./build/${name}.head.html`, 'utf8')
const html = readFileSync('./index.html', 'utf8').replace('<!-- ws-head -->', head)
writeFileSync('../dist/index.html', html)

console.log('Bundled:', name)
