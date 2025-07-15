import { cpSync, readdirSync, existsSync } from 'fs'
import { build } from 'esbuild'

cpSync('./build/', './Content/', { recursive: true });

const prebundles = readdirSync('./build/');

prebundles.forEach(file => {
  if (file.endsWith('.js')) {
    var options =
    {
      entryPoints: ['./build/' + file],
      bundle: true,
      minify: true,
      format: 'iife',
      outfile: 'Content/' + file,
      globalName: 'wsbundle',
      allowOverwrite: true
    };

    console.log("Bundling:", file);
    build(options);
  }
});
