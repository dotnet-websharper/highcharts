# WebSharper.HighCharts

[HighCharts][hc] is a JavaScript charting library, which can produce
interactive HTML5 charts from a config object.

The WebSharper bindings for HighCharts provide a strongly typed interface to
to the configuration object and helper functions. These are automatically
generated from the official [API documentation][hcapi], see the full list
of settings there.

The simplest way to define a chart with HighCharts in a WebSharper
`Web.Control` class:


```
Div [] |>! OnAfterRender (fun el ->
    Highcharts.Create(JQuery.Of el.Body,
        HighchartsCfg(
            // config properties
        )
    )
```

Sometimes a config property can accept multiple types, for example an array instead of a config object. In these cases, use WebSharper's `As` helper
function to cast it to the required type. As this cast is erased from the
resulting JavaScript code, the library works as intended.

[hc]: http://www.highcharts.com/
[hcapi]: http://api.highcharts.com/highcharts