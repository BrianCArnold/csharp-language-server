module CSharpLanguageServer.Tests.Util

open System
open System.IO
open System.Diagnostics
open System.Text
open System.Timers

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

let readStream (input: Stream) =
    let buffer = new MemoryStream()
    async {
        do! input.CopyToAsync(buffer) |> Async.AwaitTask
        buffer.Position <- 0L
        return buffer
    }

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

    let serverFileName = "/Users/bob/echo.py"

    Assert.IsTrue(File.Exists(serverFileName))

    let processStartInfo = new ProcessStartInfo()
    processStartInfo.FileName <- serverFileName
//    processStartInfo.Arguments <- arguments
    processStartInfo.RedirectStandardInput <- true
    processStartInfo.RedirectStandardOutput <- true
    processStartInfo.RedirectStandardError <- true
    processStartInfo.UseShellExecute <- false
    processStartInfo.CreateNoWindow <- true
    processStartInfo.WorkingDirectory <- projectTempDir

    task {
        use p = new Process()
        p.StartInfo <- processStartInfo

        p.Start() |> ignore

        // ensure we progress here
        let testTimeoutSecs: int = 3
        let timer = new Timer(testTimeoutSecs * 1000)
        let mutable killed = false

        timer.Elapsed.Add(fun _ ->
            timer.Stop()
            p.Kill()
            killed <- true
        )

        let stdoutBuffer = readStream p.StandardOutput.BaseStream
        let stderrBuffer = readStream p.StandardError.BaseStream

        timer.Start()

        try
            try
                do! contextFn p.StandardInput.BaseStream
            finally
                p.Kill()

                p.WaitForExit()

                if killed then
                    (sprintf "withServer: Timeout of %d secs was reached, the process was killed!" testTimeoutSecs)
                    |> Exception
                    |> raise
        finally
            deleteDirectory projectTempDir

        let! stdoutBuffer0 = stdoutBuffer
        let stdout = Encoding.UTF8.GetString(stdoutBuffer0.ToArray())
        Console.Write("stderr={0}", stdout)

        let! stderrBuffer0 = stderrBuffer
        let stderr = Encoding.UTF8.GetString(stderrBuffer0.ToArray())
        Console.Write("stderr={0}", stderr)

        Assert.IsFalse(true)
    }
