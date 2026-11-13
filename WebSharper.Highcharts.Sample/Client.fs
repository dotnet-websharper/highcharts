namespace WebSharper.Highcharts.Sample

open WebSharper
open WebSharper.JavaScript
open WebSharper.JQuery
open WebSharper.Highcharts
open WebSharper.UI
open WebSharper.UI.Client
open WebSharper.UI.Templating

[<JavaScript>]
module Client =

    type IndexTemplate = Template<"index.html", ClientLoad.FromDocument>

    // Theme: light is the default; the choice is persisted in localStorage.
    // The storage key is shared with the bootstrap script in index.html.
    let themeStorageKey = "websharper-highcharts-theme"
    let theme = Var.Create "light"
    let themeLabel = Var.Create "🌙 Dark mode"

    let applyTheme () =
        JS.Document.DocumentElement.SetAttribute("data-theme", theme.Value)
        themeLabel.Value <- if theme.Value = "dark" then "☀️ Light mode" else "🌙 Dark mode"

    let toggleTheme () =
        theme.Value <- (if theme.Value = "dark" then "light" else "dark")
        try JS.Window.LocalStorage.SetItem(themeStorageKey, theme.Value) with _ -> ()
        applyTheme ()

    let private config () =
        HighchartsCfg(
            Title = TitleCfg(Text = "Monthly Average Temperature"),
            Subtitle = SubtitleCfg(Text = "Source: WorldClimate.com"),
            XAxis = [|
                XAxisCfg(
                    Categories = [| "Jan"; "Feb"; "Mar"; "Apr"; "May"; "Jun"; "Jul"; "Aug"; "Sep"; "Oct"; "Nov"; "Dec" |]
                )
            |],
            YAxis = [|
                YAxisCfg(
                    Title = YAxisTitleCfg(Text = "Temperature (°C)"),
                    PlotLines = [|
                        YAxisPlotLinesCfg(
                            Value = 0.,
                            Width = 1.,
                            Color = "#808080"
                        )
                    |]
                )
            |],
            Tooltip = TooltipCfg(ValueSuffix = "°C"),
            Legend = LegendCfg(
                Layout = "vertical",
                Align = "right",
                VerticalAlign = "middle",
                BorderWidth = 0.
            ),
            Series = [|
                SeriesCfg(
                    Name = "Tokyo",
                    Data = As [| 7.0; 6.9; 9.5; 14.5; 18.2; 21.5; 25.2; 26.5; 23.3; 18.3; 13.9; 9.6 |]
                )
                SeriesCfg(
                    Name = "New York",
                    Data = As [| -0.2; 0.8; 5.7; 11.3; 17.0; 22.0; 24.8; 24.1; 20.1; 14.1; 8.6; 2.5 |]
                )
                SeriesCfg(
                    Name = "Berlin",
                    Data = As [| -0.9; 0.6; 3.5; 8.4; 13.5; 17.0; 18.6; 17.9; 14.3; 9.0; 3.9; 1.0 |]
                )
                SeriesCfg(
                    Name = "London",
                    Data = As [| 3.9; 4.2; 5.7; 8.5; 11.9; 15.2; 17.0; 16.6; 14.2; 10.3; 6.6; 4.8 |]
                )
            |]
        )

    // The binding is npm-based: Highcharts is imported from the `highcharts`
    // package and bundled by esbuild. The only script resource left is the
    // global jQuery that the `create` (jQuery plugin) helper uses.
    [<Require(typeof<WebSharper.JQuery.Resources.JQuery>)>]
    [<SPAEntryPoint>]
    let Main () =
        // Restore a previously stored theme before the first paint.
        try
            let stored = JS.Window.LocalStorage.GetItem(themeStorageKey)
            if not (isNull stored) && (stored = "light" || stored = "dark") then
                theme.Value <- stored
        with _ -> ()
        applyTheme ()

        let summary = Var.Create "Rendering…"

        IndexTemplate()
            .Title("Monthly Average Temperature")
            .themeLabel(themeLabel.View)
            .ToggleTheme(fun _ -> toggleTheme ())
            .ChartSummary(summary.View)
            .Bind()

        // Highcharts modules are opt-in: this imports
        // "highcharts/modules/exporting", which adds the export menu.
        Highcharts.Modules.Exporting()

        let el = JS.Document.GetElementById("chart")
        let chart = Highcharts.Create(JQuery.Of el, config ())

        // The runtime `Chart` object is bound as well, so the plot area can be
        // read back; `Highcharts` exposes the namespace functions too.
        summary.Value <-
            sprintf "Plot area %.0f × %.0f px · CorrectFloat(0.1 + 0.2) = %s"
                chart.PlotWidth chart.PlotHeight
                (string (Highcharts.CorrectFloat(0.1 + 0.2, 2.)))
