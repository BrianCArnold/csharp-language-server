module CSharpLanguageServer.Tests.InitializationTests

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

    let testFn (stdin: Stream) (stdout: StreamReader) = task {
        do! stdin.WriteAsync(Encoding.UTF8.GetBytes("{}"))
        let! input = stdout.ReadLineAsync()
        printfn "input=%s" input
    }

    withServer projectFiles testFn
