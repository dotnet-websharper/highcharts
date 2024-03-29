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

type HcConfig =
    {
        Name    : string
        RefName : string
        Type    : string
        Members : HcConfig list   
        Desc    : string option
        Values  : string list
        Extends : string option
    }

type HcProperty =
    {
        Name : string
        Type : string
        Desc : string option
    }

type HcParam =
    {
        Name     : string
        Type     : string 
        Optional : bool
    }

type HcMethod =
    {
        Name       : string
        ReturnType : string
        Params     : HcParam list
        Desc       : string option
    }

type HcMember =
    | HcProperty of HcProperty
    | HcMethod of HcMethod

type HcObject =
    {
        Name    : string
        Desc    : string option
        Members : HcMember list
    }

open Json

let getString j    = match j with JString s -> s | JNull -> null | o -> failwithf "not a string: %A" o
let getStringOpt j    = match j with JString "" | JNull -> None | JString s -> Some s | o -> failwithf "not a string: %A" o
let getList j      = match j with JList   l -> l | o -> failwithf "not a list: %A" o
let getObject j    = match j with JObject s -> s | o -> failwithf "not an object: %A" o
    
let hasTrue p j = j |> Map.tryFind p = Some (JBool true)

module Map =
    let find key table =
        try Map.find key table
        with _ ->
            failwithf "Key not found:\nItem: %A\nMap:%A" key table
   
let trimAndSplit (tr: string) (s: string) =
    s.TrimStart(tr.[0]).TrimEnd(tr.[1]).Split(tr.[2])

let getValues o =
    match o |> Map.tryFind "values" with
    | None -> []
    | Some values ->
    match values |> getString with
    | null | "" -> []
    | s ->
        s |> trimAndSplit "[]," |> Seq.map (fun i ->
            i.Trim()
        ) |> List.ofSeq

let getReturnType o =
    o |> Map.tryFind "returnType" |> Option.map getString |> function Some t -> t | _ -> null

let getConfig (j: Json) (members: HcConfig list) =
    let jo = j |> getObject
    try
        let conf =
            {
                Name    = jo |> Map.find "title" |> getString
                RefName = jo |> Map.find "name" |> getString 
                Type    = jo |> getReturnType
                Members = members
                Desc    = jo |> Map.tryFind "description" |> Option.bind getStringOpt
                Values  = jo |> getValues
                Extends = jo |> Map.tryFind "extending" |> Option.bind getStringOpt
            }
        if System.String.IsNullOrEmpty conf.Name then
            printfn "Warning: config definition of '%s' ignored because of invalid name: %O" (jo |> Map.find "fullname" |> getString) j
            None
        else Some conf
    with e -> failwithf "getConfig error on: %A. Error: %A" (jo |> Map.find "fullname" |> getString) e

let getParent c =
    match c |> getObject |> Map.tryFind "parent" with
    | Some p -> getString p
    | _ -> null

let getConfigs (j: Json) =
    let l = j |> getList
    let nestingMap =
        l |> Seq.filter (fun c -> c |> getObject |> hasTrue "deprecated" |> not)
        |> Seq.map (fun c -> c |> getParent, c)
        |> Seq.groupBy fst |> Seq.map (fun (k, vl) -> k, vl |> Seq.map snd |> List.ofSeq)
        |> Map.ofSeq
    let rec tr c = 
        let co = c |> getObject
        match nestingMap |> Map.tryFind (co |> Map.find "name" |> getString) with
        | Some mem -> List.choose tr mem
        | _ -> []
        |> getConfig c
    
    nestingMap |> Map.find null |> List.choose tr

let getParam (s: string) =
    try
        match s with
        | null | "" | "()" -> []
        | _ ->
        s |> trimAndSplit "()," |> Seq.map (fun p ->
            let p = p.Trim()
            let opt, pa =
                if p.StartsWith("[") then
                    true, p |> trimAndSplit "[] "
                else
                    false, p.Split ' '
            {                              
                Name     = pa.[1]
                Type     = pa.[0]
                Optional = opt
            }                              
        )
        |> List.ofSeq
    with _ ->
        failwithf "getParam error on \"%s\"" s

let getParams o =
    match o |> Map.tryFind "params" with
    | Some ps -> ps |> getString |> getParam
    | None -> []

let getMethod (j: Json) =
    let jo = j |> getObject
    {
        Name       = jo |> Map.find "title" |> getString
        ReturnType = jo |> getReturnType
        Params     = jo |> getParams
        Desc       = jo |> Map.find "description" |> getStringOpt
    }

let getProperty (j: Json) =
    let jo = j |> getObject
    {
        Name = jo |> Map.find "title" |> getString
        Type = jo |> getReturnType
        Desc = jo |> Map.find "description" |> getStringOpt
    } : HcProperty

let getMember name (j: Json) =
    let jo = j |> getObject
    let mem =
        match jo |> Map.find "type" |> getString with
        | "method" | "metthod" | "" -> getMethod j |> HcMethod 
        | "property" | "Number" -> getProperty j |> HcProperty
        | "Array<Object>" -> { getProperty j with Type = "Array<Object>" } |> HcProperty
        | "Object" -> { getProperty j with Type = "Object" } |> HcProperty
        | "Boolean" -> { getProperty j with Type = "Boolean" } |> HcProperty
        | t -> failwithf "getMember error: member type not found: %s [should be method, property; if it looks like a value type, add a workaround]" t
    match mem with
    | (HcMethod { Name = n } | HcProperty { Name = n }) when System.String.IsNullOrEmpty n ->
        printfn "Warning: member of '%s' ignored because of invalid name: %O" name j
        None
    | _ -> Some mem

let getObjects (j: Json) =
    let l = j |> getList
    let memberMap =
        l |> Seq.map (fun c -> c |> getParent, c)
        |> Seq.groupBy fst |> Seq.map (fun (k, vl) -> k, vl |> Seq.map snd |> List.ofSeq)
        |> Map.ofSeq
    memberMap |> Map.find null |> List.choose (fun o ->
        let oo = o |> getObject
        let name = oo |> Map.find "title" |> getString
        if System.String.IsNullOrEmpty name then 
            printfn "Warning: object definition ignored because of invalid name: %O" j
            None
        else
        Some {
            Name    = name
            Desc    = oo |> Map.find "description" |> getStringOpt
            Members = memberMap |> Map.find name |> List.choose (getMember name)
        }
    )