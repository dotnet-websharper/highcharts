// Older WebSharper.JQuery releases import the npm `jquery` module, while
// Highcharts (loaded by the binding's resource) attaches `$.fn.highcharts` to
// the global jQuery. This shim makes the import resolve to that same global
// instance, so JQuery.Of(...).highcharts(...) works. Once WebSharper.JQuery
// uses the global directly (see dotnet-websharper/jquery#14) this is unused.
export default globalThis.jQuery;
