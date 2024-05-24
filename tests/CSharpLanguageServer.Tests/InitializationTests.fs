module CSharpLanguageServer.Tests.InitializationTests

open System.IO
open System.Text

open NUnit.Framework

open CSharpLanguageServer.Tests.Util

[<TestCase>]
let testServerInitializes () =
    let testFn (stdin: Stream) (stdout: StreamReader) = task {
        do! stdin.WriteAsync(Encoding.UTF8.GetBytes("{}"))
        let! input = stdout.ReadLineAsync()
        printfn "input=%s" input
    }

    withServer testFn
