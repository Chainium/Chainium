namespace Chainium.Blockchain.Public.Data

open System
open System.IO
open Newtonsoft.Json
open Chainium.Common
open Chainium.Blockchain.Common
open Chainium.Blockchain.Public.Core.DomainTypes
open Chainium.Blockchain.Public.Core.Dtos

module Raw =

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // General
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    type RawDataType =
        | Tx
        | Block

    let private createFileName (dataType : RawDataType) (key : string) =
        sprintf "%s_%s" (unionCaseName dataType) key

    let private saveData (dataDir : string) (dataType : RawDataType) (key : string) data : Result<unit, AppErrors> =
        try
            if not (Directory.Exists(dataDir)) then
                Directory.CreateDirectory(dataDir) |> ignore

            let dataTypeName = unionCaseName dataType
            let fileName = createFileName dataType key
            let path = Path.Combine(dataDir, fileName)

            if File.Exists(path) then
                Error [AppError (sprintf "%s %s already exists." dataTypeName key)]
            else
                let json = data |> JsonConvert.SerializeObject
                File.WriteAllText(path, json)
                Ok ()
        with
        | ex ->
            Log.error ex.AllMessagesAndStackTraces
            Error [AppError "Save failed"]

    let private loadData<'T> (dataDir : string) (dataType : RawDataType) (key : string) : Result<'T, AppErrors> =
        try
            let dataTypeName = unionCaseName dataType
            let fileName = createFileName dataType key
            let path = Path.Combine(dataDir, fileName)

            if File.Exists(path) then
                File.ReadAllText path
                |> JsonConvert.DeserializeObject<'T>
                |> Ok
            else
                Error [AppError (sprintf "%s %s not found." dataTypeName key)]
        with
        | ex ->
            Log.error ex.AllMessagesAndStackTraces
            Error [AppError "Load failed"]

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Specific
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    let saveTx (dataDir : string) (TxHash txHash) (txEnvelopeDto : TxEnvelopeDto) : Result<unit, AppErrors> =
        saveData dataDir Tx txHash txEnvelopeDto

    let getTx (dataDir : string) (TxHash txHash) : Result<TxEnvelopeDto, AppErrors> =
        loadData<TxEnvelopeDto> dataDir Tx txHash

    let saveBlock (dataDir : string) (blockDto : BlockDto) : Result<unit, AppErrors> =
        saveData dataDir Block (string blockDto.Header.Number) blockDto

    let getBlock (dataDir : string) (BlockNumber blockNumber) : Result<BlockDto, AppErrors> =
        ////////////////////////////////////////////////////////////////////////////////////////////////////
        // TODO: Store genesis block during initalization and remove this dummy
        if blockNumber = 0L then
            let header = {
                Number = 0L
                Hash = "0"
                PreviousHash = "0"
                Timestamp = 0L
                Validator = "0"
                TxSetRoot = "0"
                TxResultSetRoot = "0"
                StateRoot = "0"
            }

            Ok {
                Header = header
                TxSet = []
            }
        else
        ////////////////////////////////////////////////////////////////////////////////////////////////////
            loadData<BlockDto> dataDir Block (string blockNumber)
