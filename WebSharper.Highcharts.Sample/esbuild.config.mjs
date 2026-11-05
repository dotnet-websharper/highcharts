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

// Inject the WebSharper head: the jQuery and Highcharts <script> tags linked by
// the binding's resources (jQuery from code.jquery.com, Highcharts from jsDelivr).
const head = readFileSync(`./build/${name}.head.html`, 'utf8')
const html = readFileSync('./index.html', 'utf8').replace('<!-- ws-head -->', head)
writeFileSync('../dist/index.html', html)

console.log('Bundled:', name)
