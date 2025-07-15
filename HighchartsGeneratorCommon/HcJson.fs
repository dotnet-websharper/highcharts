// $begin{copyright}
//
// This file is part of WebSharper
//
// Copyright (c) 2008-2018 IntelliFactory
//
// Licensed under the Apache License, Version 2.0 (the "License"); you
// may not use this file except in compliance with the License.  You may
// obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
// implied.  See the License for the specific language governing
// permissions and limitations under the License.
//
// $end{copyright}
module HighchartsGeneratorCommon.HcJson

open WebSharper.Core
open WebSharper.InterfaceGenerator
open System.IO
open System.Collections.Generic

let jsonFile = Path.Combine(__SOURCE_DIRECTORY__, "../.temp/tree.json")

let jsonTxt = File.ReadAllText(jsonFile)

let json = Json.Parse jsonTxt

type CfgInfo =
    {
        Name: string
        Description: string option
        Extends: string option
        Products: string list
        Types: string list
        Children: CfgInfo list
    }

let (-?) obj name =
    match obj with
    | Json.Object mems ->
        mems |> List.tryPick (fun (n, o) -> if n = name then Some o else None)
    | _ -> failwithf "Expecting an object: %A" obj 

let (-.) obj name =
    match obj -? name with
    | Some res -> res
    | _ -> failwithf "Expecting item: %s in %A" name obj 

let getString obj =
    match obj with
    | Json.String s -> s
    | _ -> failwithf "Expecting a string: %A" obj 

let getList obj =
    match obj with
    | Json.Array a -> a
    | _ -> failwithf "Expecting an array: %A" obj 

let rec transform (json: Json.Value) =
    match json with
    | Json.Object children ->
        children
        |> Seq.filter (fun (n, _) -> n <> "_meta")
        |> Seq.collect (fun (n, o) ->
            let doclet = o -. "doclet"
            if doclet -? "internal" |> Option.isNone then
                let children = o -. "children"
                {
                    Name = n
                    Description = doclet -? "description" |> Option.map getString
                    Extends = doclet -? "extends" |> Option.map getString
                    Products = doclet -? "products" |> Option.map (getList >> List.map getString) |> Option.defaultValue []
                    Types = doclet -? "type" |> Option.map (fun t -> t -. "names" |> getList |> List.map getString) |> Option.defaultValue []
                    Children = transform children
                }
                |> Seq.singleton
            else
                []
        )
        |> List.ofSeq
    | _ -> []

let tree = transform json
