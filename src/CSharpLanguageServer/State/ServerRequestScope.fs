namespace CSharpLanguageServer.State

open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.Text
open Microsoft.CodeAnalysis.FindSymbols
open Ionide.LanguageServerProtocol.Types
open FSharpPlus

open CSharpLanguageServer.State.ServerState
open CSharpLanguageServer.Util
open CSharpLanguageServer.Types
open CSharpLanguageServer.RoslynHelpers
open CSharpLanguageServer.Conversions

type ServerRequestScope (requestId: int, state: ServerState, emitServerEvent, logMessage: AsyncLogFn) =
    let mutable solutionMaybe = state.Solution

    member _.RequestId = requestId
    member _.State = state
    member _.ClientCapabilities = state.ClientCapabilities
    member _.Solution = solutionMaybe.Value
    member _.OpenDocVersions = state.OpenDocVersions
    member _.DecompiledMetadata = state.DecompiledMetadata

    member _.logMessage _ = logMessage

    member this.GetDocumentForUriOfType = getDocumentForUriOfType this.State

    member this.GetUserDocumentForUri (u: string) =
        this.GetDocumentForUriOfType UserDocument u |> Option.map fst

    member this.GetAnyDocumentForUri (u: string) =
        this.GetDocumentForUriOfType AnyDocument u |> Option.map fst

    member this.GetSymbolAtPositionOfType docType uri pos = async {
        match this.GetDocumentForUriOfType docType uri with
        | Some (doc, _docType) ->
            let! ct = Async.CancellationToken
            let! sourceText = doc.GetTextAsync(ct) |> Async.AwaitTask
            let position = sourceText.Lines.GetPosition(LinePosition(pos.Line, pos.Character))
            let! symbolRef = SymbolFinder.FindSymbolAtPositionAsync(doc, position, ct) |> Async.AwaitTask
            return if isNull symbolRef then None else Some (symbolRef, doc, position)

        | None ->
            return None
    }

    member this.GetSymbolAtPositionOnUserDocument uri pos =
        this.GetSymbolAtPositionOfType UserDocument uri pos

    member _.Emit ev =
        match ev with
        | SolutionChange newSolution ->
            solutionMaybe <- Some newSolution
        | _ -> ()

        emitServerEvent ev

    member this.EmitMany es =
        for e in es do this.Emit e

    member private this.ResolveSymbolLocation
            (project: Microsoft.CodeAnalysis.Project option)
            sym
            (l: Microsoft.CodeAnalysis.Location) = async {
        match l.IsInMetadata, l.IsInSource, project with
        | true, _, Some project ->
            let! ct = Async.CancellationToken
            let! compilation = project.GetCompilationAsync(ct) |> Async.AwaitTask

            let fullName = sym |> getContainingTypeOrThis |> getFullReflectionName
            let uri = $"csharp:/metadata/projects/{project.Name}/assemblies/{l.MetadataModule.ContainingAssembly.Name}/symbols/{fullName}.cs"

            let mdDocument, stateChanges =
                match Map.tryFind uri state.DecompiledMetadata with
                | Some value ->
                    (value.Document, [])
                | None ->
                    let (documentFromMd, text) = makeDocumentFromMetadata compilation project l fullName

                    let csharpMetadata = { ProjectName = project.Name
                                           AssemblyName = l.MetadataModule.ContainingAssembly.Name
                                           SymbolName = fullName
                                           Source = text }
                    (documentFromMd, [
                        DecompiledMetadataAdd (uri, { Metadata = csharpMetadata; Document = documentFromMd })])

            this.EmitMany stateChanges

            // figure out location on the document (approx implementation)
            let! syntaxTree = mdDocument.GetSyntaxTreeAsync(ct) |> Async.AwaitTask

            let collector = DocumentSymbolCollectorForMatchingSymbolName(uri, sym)
            collector.Visit(syntaxTree.GetRoot())

            let fallbackLocationInMetadata = {
                Uri = uri
                Range = { Start = { Line = 0; Character = 0 }; End = { Line = 0; Character = 1 } } }

            return
                match collector.GetLocations() with
                | [] -> [fallbackLocationInMetadata]
                | ls -> ls

        | false, true, _ ->
            return [Location.fromRoslynLocation l]

        | _, _, _ ->
            return []
    }

    member this.ResolveSymbolLocations
            (symbol: Microsoft.CodeAnalysis.ISymbol)
            (project: Microsoft.CodeAnalysis.Project option) = async {
        let mutable aggregatedLspLocations = []
        for l in symbol.Locations do
            let! symLspLocations = this.ResolveSymbolLocation project symbol l

            aggregatedLspLocations <- aggregatedLspLocations @ symLspLocations

        return aggregatedLspLocations
    }

    member this.FindSymbol' (uri: DocumentUri) (pos: Position): Async<(ISymbol * Document) option> = async {
        match this.GetAnyDocumentForUri uri with
        | None -> return None
        | Some doc ->
            let! sourceText = doc.GetTextAsync() |> Async.AwaitTask
            let position = Position.toRoslynPosition sourceText.Lines pos
            let! symbol = SymbolFinder.FindSymbolAtPositionAsync(doc, position) |> Async.AwaitTask
            return symbol |> Option.ofObj |> Option.map (fun sym -> sym, doc)
     }

    member this.FindSymbol (uri: DocumentUri) (pos: Position): Async<ISymbol option> =
        this.FindSymbol' uri pos |> map (Option.map fst)

    member private this._FindDerivedClasses (symbol: INamedTypeSymbol) (transitive: bool): Async<INamedTypeSymbol seq> =
        match state.Solution with
        | None -> async.Return []
        | Some currentSolution ->
            SymbolFinder.FindDerivedClassesAsync(symbol, currentSolution, transitive) |> Async.AwaitTask

    member private this._FindDerivedInterfaces (symbol: INamedTypeSymbol) (transitive: bool):  Async<INamedTypeSymbol seq> =
        match state.Solution with
        | None -> async.Return []
        | Some currentSolution ->
            SymbolFinder.FindDerivedInterfacesAsync(symbol, currentSolution, transitive) |> Async.AwaitTask

    member this.FindImplementations (symbol: ISymbol): Async<ISymbol seq> =
        match state.Solution with
        | None -> async.Return []
        | Some currentSolution ->
            SymbolFinder.FindImplementationsAsync(symbol, currentSolution) |> Async.AwaitTask

    member this.FindImplementations' (symbol: INamedTypeSymbol) (transitive: bool): Async<INamedTypeSymbol seq> =
        match state.Solution with
        | None -> async.Return []
        | Some currentSolution ->
            SymbolFinder.FindImplementationsAsync(symbol, currentSolution, transitive) |> Async.AwaitTask

    member this.FindDerivedClasses (symbol: INamedTypeSymbol): Async<INamedTypeSymbol seq> = this._FindDerivedClasses symbol true
    member this.FindDerivedClasses' (symbol: INamedTypeSymbol) (transitive: bool): Async<INamedTypeSymbol seq> = this._FindDerivedClasses symbol transitive

    member this.FindDerivedInterfaces (symbol: INamedTypeSymbol):  Async<INamedTypeSymbol seq> = this._FindDerivedInterfaces symbol true
    member this.FindDerivedInterfaces' (symbol: INamedTypeSymbol) (transitive: bool):  Async<INamedTypeSymbol seq> = this._FindDerivedInterfaces symbol transitive

    member this.FindCallers (symbol: ISymbol): Async<SymbolCallerInfo seq> =
        match state.Solution with
        | None -> async.Return []
        | Some currentSolution -> SymbolFinder.FindCallersAsync(symbol, currentSolution) |> Async.AwaitTask

    member this.ResolveTypeSymbolLocations
            (project: Microsoft.CodeAnalysis.Project)
            (symbols: Microsoft.CodeAnalysis.ITypeSymbol list) = async {
        let mutable aggregatedLspLocations = []

        for sym in symbols do
            for l in sym.Locations do
                let! symLspLocations = this.ResolveSymbolLocation (Some project) sym l

                aggregatedLspLocations <- aggregatedLspLocations @ symLspLocations

        return aggregatedLspLocations
    }

    member this.FindSymbols (pattern: string option): Async<ISymbol seq> = async {
        let findTask =
                match pattern with
                | Some pat ->
                    fun (sln: Solution) -> SymbolFinder.FindSourceDeclarationsWithPatternAsync(sln, pat, SymbolFilter.TypeAndMember)
                | None ->
                    fun (sln: Solution) -> SymbolFinder.FindSourceDeclarationsAsync(sln, konst true, SymbolFilter.TypeAndMember)

        match this.State.Solution with
        | None -> return []
        | Some solution -> return! findTask solution |> Async.AwaitTask
    }

    member this.FindReferences (symbol: ISymbol): Async<ReferencedSymbol seq> = async {
        match this.State.Solution with
        | None -> return []
        | Some solution ->
            return! SymbolFinder.FindReferencesAsync(symbol, solution) |> Async.AwaitTask
    }

    member this.GetDocumentVersion (uri: DocumentUri): int option =
        Uri.unescape uri |> this.OpenDocVersions.TryFind
