module CSharpLanguageServer.Tests.Util

open System
open System.IO
open System.Diagnostics
open System.Text
open System.Timers
open System.Threading

open NUnit.Framework

let writeProjectDir (fileMap: Map<string, string>) : string =
    let tempDir = Path.Combine(
        Path.GetTempPath(),
        "CSharpLanguageServer.Tests." + System.DateTime.Now.Ticks.ToString())

    Directory.CreateDirectory(tempDir) |> ignore

    for kv in fileMap do
        let filename = Path.Combine(tempDir, kv.Key)

        if filename.Contains("/") then
            let parts = kv.Key.Split("/")
            if parts.Length > 2 then
               failwith "more than 1 subdir is not supported"

            let fileDir = Path.Combine(tempDir, parts[0])

            if not (Directory.Exists(fileDir)) then
                Directory.CreateDirectory(fileDir) |> ignore

        use fileStream = File.Create(filename)
        fileStream.Write(Encoding.UTF8.GetBytes(kv.Value))

    tempDir


let rec deleteDirectory (path: string) =
    if Directory.Exists(path) then
        Directory.GetFileSystemEntries(path)
        |> Array.iter (fun item ->
            if File.Exists(item) then
                File.Delete(item)
            else
                deleteDirectory item)
        Directory.Delete(path)


let withServer (fileMap: Map<string, string>) contextFn =
    let projectTempDir = writeProjectDir fileMap

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

    //let serverFileName = "/Users/bob/echo.py"

    Assert.IsTrue(File.Exists(serverFileName))

    let processStartInfo = new ProcessStartInfo()
    processStartInfo.FileName <- serverFileName
    processStartInfo.RedirectStandardInput <- true
    processStartInfo.RedirectStandardOutput <- true
    processStartInfo.RedirectStandardError <- true
    processStartInfo.UseShellExecute <- false
    processStartInfo.CreateNoWindow <- true
    processStartInfo.WorkingDirectory <- projectTempDir

    printfn "serverFileName=%s" serverFileName

    task {
        use p = new Process()
        p.StartInfo <- processStartInfo

        let startResult = p.Start()
        Assert.IsTrue(startResult)

        // ensure we progress here
        let testTimeoutSecs: int = 3
        let timer = new System.Timers.Timer(testTimeoutSecs * 1000)
        let mutable killed = false

        timer.Elapsed.Add(fun _ ->
            timer.Stop()
            Console.Error.WriteLine("timer.Ellapsed: p.Kill()")
            p.Kill()
            killed <- true
        )

        let stdoutReadTask = p.StandardOutput.ReadToEndAsync()
        let stderrReadTask = p.StandardError.ReadToEndAsync()

        timer.Start()

        try
            try
                do! contextFn projectTempDir p.StandardInput
            finally
                p.WaitForExit()

                if killed then
                    (sprintf "withServer: Timeout of %d secs was reached, the process was killed!" testTimeoutSecs)
                    |> Exception
                    |> raise
        finally
            deleteDirectory projectTempDir

            let stdout = stdoutReadTask.Result
            Console.WriteLine("stdout={0}", stdout)

            let stderr = stderrReadTask.Result
            Console.WriteLine("stderr={0}", stderr)

            Console.WriteLine("exit code={0}", p.ExitCode)

        Assert.IsFalse(true)
    }
