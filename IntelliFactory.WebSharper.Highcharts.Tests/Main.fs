namespace IntelliFactory.WebSharper.Highcharts.Tests

open IntelliFactory.Html
open IntelliFactory.WebSharper
open IntelliFactory.WebSharper.Sitelets

type Action = | App

module Skin =
    open System.Web

    type Page =
        {
            Body : list<Content.HtmlElement>
        }

    let MainTemplate =
        Content.Template<Page>("~/Main.html")
            .With("body", fun x -> x.Body)

    let WithTemplate body : Content<Action> =
        Content.WithTemplate MainTemplate <| fun context ->
            {
                Body = body context
            }

module Site =
    let AppPage =
        Skin.WithTemplate <| fun ctx ->
            [
                Div [ new Control.AppControl() ]
            ]

[<Sealed>]
type EmptyWebsite() =
    interface IWebsite<Action> with
        member this.Actions = [ Action.App ]
        member this.Sitelet = Sitelet.Content "/" Action.App Site.AppPage

[<assembly: Website(typeof<EmptyWebsite>)>]
do ()
