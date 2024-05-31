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

        do System.Threading.Thread.Sleep(250)

        let initializedRequest =
          sprintf """{"jsonrpc":"2.0","id":2,"method":"initialized","params":{}}"""

        do! sendMessageRaw serverStdin initializedRequest

        do System.Threading.Thread.Sleep(250)

        let clientRegisterCapResponse =
          sprintf """{"jsonrpc":"2.0","id":2,"result":null}"""

        do! sendMessageRaw serverStdin clientRegisterCapResponse

        do System.Threading.Thread.Sleep(250)

        let workspaceConfigResponse =
          sprintf """{"jsonrpc":"2.0","id":3,"result":[]}"""

        do! sendMessageRaw serverStdin workspaceConfigResponse

        do System.Threading.Thread.Sleep(250)

        let workDoneProgressResponse =
          sprintf """{"jsonrpc":"2.0","id":4,"result":null}"""

        do! sendMessageRaw serverStdin workDoneProgressResponse

        printf "sleeping for 5 secs; %s ..\n" (string DateTime.Now)
        do System.Threading.Thread.Sleep(5 * 1000)
        printf "sleeping for 5 secs -- done; %s ..\n" (string DateTime.Now)

        ()
    }

    withServer projectFiles testFn
