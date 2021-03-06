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
module Main

open System.IO

open WebSharper.InterfaceGenerator
open HighchartsGeneratorCommon

let ( +/ ) a b = Path.Combine(a, b)

let Assembly =
    try 
        let configs =
            File.ReadAllText(__SOURCE_DIRECTORY__ +/ "../.temp/hsconfigs.json")  
            |> Json.parse |> HcJson.getConfigs
        let objects =  
            File.ReadAllText(__SOURCE_DIRECTORY__ +/ "../.temp/hsobjects.json")  
            |> Json.parse |> HcJson.getObjects
        Definition.getAssembly Definition.Highstock configs objects
    with exc ->
        printfn "%A" exc
        reraise()    

[<Sealed>]
type HighstockExtension() =
    interface IExtension with
        member ext.Assembly = Assembly

[<assembly: Extension(typeof<HighstockExtension>)>]
do ()
