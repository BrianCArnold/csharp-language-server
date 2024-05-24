module CSharpLanguageServer.Tests.Util

open System
open System.IO
open System.Diagnostics

open NUnit.Framework
open System.Timers

let withServer contextFn =
    let serverExe = Path.Combine(Environment.CurrentDirectory)
    let tfm = Path.GetFileName(serverExe)
    let buildMode = Path.GetFileName(Path.GetDirectoryName(serverExe))

    let baseDir =
        serverExe
        |> Path.GetDirectoryName
        |> Path.GetDirectoryName
        |> Path.GetDirectoryName
        |> Path.GetDirectoryName
        |> Path.GetDirectoryName

    let serverFileName =
        Path.Combine(baseDir, "src", "CSharpLanguageServer", "bin", buildMode, tfm, "CSharpLanguageServer")

    Assert.IsTrue(File.Exists(serverFileName))

    let processStartInfo = new ProcessStartInfo()
    processStartInfo.FileName <- serverFileName
//    processStartInfo.Arguments <- arguments
    processStartInfo.RedirectStandardInput <- true
    processStartInfo.RedirectStandardOutput <- true
    processStartInfo.RedirectStandardError <- true
    processStartInfo.UseShellExecute <- false
    processStartInfo.CreateNoWindow <- true

    task {
        use p = new Process()
        p.StartInfo <- processStartInfo

        p.Start() |> ignore

        // ensure we progress here
        let testTimeoutSecs: int = 1
        let timer = new Timer(testTimeoutSecs * 1000)
        let mutable killed = false

        timer.Elapsed.Add(fun _ ->
            timer.Stop()
            p.Kill()
            killed <- true
        )

        timer.Start()

        use reader = new StreamReader(p.StandardOutput.BaseStream)

        do! contextFn p.StandardInput.BaseStream reader

        p.Kill()

        p.WaitForExit()

        if killed then
           (sprintf "withServer: Timeout of %d secs was reached, killing the process.." testTimeoutSecs)
           |> Exception
           |> raise
    }
