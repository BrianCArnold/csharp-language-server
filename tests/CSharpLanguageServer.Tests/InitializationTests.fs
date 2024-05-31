module CSharpLanguageServer.Tests.InitializationTests

open System
open System.IO
open System.Text

open NUnit.Framework

open CSharpLanguageServer.Tests.Util

[<TestCase>]
let testServerInitializes () =
    let projectFiles =
        Map.ofList [
          ("Project/Project.csproj",
           """<Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                  <OutputType>Exe</OutputType>
                  <TargetFramework>net8.0</TargetFramework>
                </PropertyGroup>
              </Project>
           """);
          ("Project/Class.cs",
           """using System;
              class Class
              {
              }
           """
          )
        ]

    let sendMessageRaw (serverStdin: StreamWriter) (m: string) = task {
        do! serverStdin.WriteAsync(
          String.Format("Content-Length: {0}\r\n\r\n{1}", m.Length, m))

        do! serverStdin.FlushAsync()
      }

    let testFn projectDir (serverStdin: StreamWriter)
               = task {
        let thisProcessId = System.Diagnostics.Process.GetCurrentProcess().Id

        let initRequest =
          sprintf """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId": %d, "rootUri": "%s", "capabilities": {}, "trace": "off"}}"""
                  thisProcessId
                  (sprintf "file://%s" projectDir)

        do! sendMessageRaw serverStdin initRequest

        do System.Threading.Thread.Sleep(1000)

        let initializedRequest =
          sprintf """{"jsonrpc":"2.0","id":2,"method":"initialized","params":{}}"""

        do! sendMessageRaw serverStdin initializedRequest

        ()
    }

    withServer projectFiles testFn
