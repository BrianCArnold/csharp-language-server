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

    let testFn projectDir (serverStdin: StreamWriter)
               = task {
        let thisProcessId = System.Diagnostics.Process.GetCurrentProcess().Id

        let initRequest =
          sprintf """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"processId": %d, "rootUri": "%s", "capabilities": {}, "trace": "off"}}"""
                  thisProcessId
                  (sprintf "file://%s" projectDir)

        do! serverStdin.WriteAsync(
          String.Format("Content-Length: {0}\r\n\r\n{1}", initRequest.Length, initRequest))

        do! serverStdin.FlushAsync()

(*
        do Console.Error.WriteLine("ReadLineAsync...");
        let! stdout = serverStdout.ReadToEndAsync()
        do Console.Error.WriteLine("ReadLineAsync, done, stdout.Length={0}...", stdout.Length);
        printfn "stdout=%s" stdout

        let! stderr = serverStderr.ReadToEndAsync()
        do Console.Error.WriteLine("ReadLineAsync, done, stderr.Length={0}...", stderr.Length);
        printfn "stderr=%s" stderr

        do Console.Error.WriteLine("ReadLineAsync...");
        let! stdout = serverStdout.ReadToEndAsync()
        do Console.Error.WriteLine("ReadLineAsync, done, stdout.Length={0}...", stdout.Length);
        printfn "stdout=%s" stdout
*)
    }

    withServer projectFiles testFn
