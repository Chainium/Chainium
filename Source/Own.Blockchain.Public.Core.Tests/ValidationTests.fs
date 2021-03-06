﻿namespace Own.Blockchain.Public.Core.Tests

open System
open Xunit
open Swensen.Unquote
open Own.Blockchain.Common
open Own.Blockchain.Public.Core
open Own.Blockchain.Public.Core.Dtos
open Own.Blockchain.Public.Core.DomainTypes
open Own.Blockchain.Public.Crypto

module ValidationTests =

    let chAddress = BlockchainAddress "CHaLUjmvaJs2Yn6dyrLpsjVzcpWg6GENkEw"
    let txHash = TxHash "SampleHash"
    let transferChxActionType = "TransferChx"
    let transferAssetActionType = "TransferAsset"
    let createAssetEmissionActionType = "CreateAssetEmission"
    let createAccountActionType = "CreateAccount"
    let createAssetActionType = "CreateAsset"
    let setAccountControllerActionType = "SetAccountController"
    let setAssetControllerActionType = "SetAssetController"
    let setAssetCodeActionType = "SetAssetCode"
    let configureValidatorActionType = "ConfigureValidator"
    let delegateStakeActionType = "DelegateStake"
    let configureTradingPairActionType = "ConfigureTradingPair"
    let placeTradeOrderActionType = "PlaceTradeOrder"
    let cancelTradeOrderActionType = "CancelTradeOrder"

    let accountHash1, accountHash2 =
        AccountHash "3dYWB8TyU17SFf3ZLZ7fpQxoQAneoxdn92XRf88ZdxYC",
        AccountHash "4NZXDMd2uKLTmkKVciu84pkSnzUtic6TKxD61grbGcm9"

    let assetHash = AssetHash "BPRi75qm2RYWa2QAtyGwyjDXp7BkS9jR1EWAmUqsdEsC"
    let assetHash2 = AssetHash "EJibjhAgic9ynhpQbr2Sx3bbYDvREc4RwdQGdVSAWgzH"

    [<Fact>]
    let ``Validation.validateTx BasicValidation single validation error`` () =
        let recipientWallet = Signing.generateWallet ()
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = -10L
            ExpirationTime = 0L
            ActionFee = 20m
            Actions =
                [
                    {
                        ActionType = transferChxActionType
                        ActionData =
                            {
                                TransferChxTxActionDto.RecipientAddress = recipientWallet.Address.Value
                                Amount = 20m
                            }
                    }
                    {
                        ActionType = transferAssetActionType
                        ActionData =
                            {
                                TransferAssetTxActionDto.FromAccountHash = accountHash1.Value
                                ToAccountHash = accountHash2.Value
                                AssetHash = assetHash.Value
                                Amount = 12m
                            }
                    }
                ]
        }

        let expMessage = AppError "Nonce must be greater than zero"
        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok t -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>
            test <@ errors.[0] = expMessage @>

    [<Fact>]
    let ``Validation.validateTx BasicValidation multiple validation errors`` () =
        let recipientWallet = Signing.generateWallet ()
        let testTx = {
            SenderAddress = ""
            Nonce = -10L
            ExpirationTime = -1L
            ActionFee = 0m
            Actions =
                [
                    {
                        ActionType = transferChxActionType
                        ActionData =
                            {
                                TransferChxTxActionDto.RecipientAddress = recipientWallet.Address.Value
                                Amount = 20m
                            }
                    }
                    {
                        ActionType = transferAssetActionType
                        ActionData =
                            {
                                TransferAssetTxActionDto.FromAccountHash = accountHash1.Value
                                ToAccountHash = accountHash2.Value
                                AssetHash = assetHash.Value
                                Amount = 12m
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok t -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 4 @>

    [<Fact>]
    let ``Validation.validateTx BasicValidation unknown action type`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = "Unknown"
                        ActionData = "Unknown"
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok t -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>

    [<Fact>]
    let ``Validation.validateTx TransferChx invalid Amount`` () =
        let recipientWallet = Signing.generateWallet ()
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = transferChxActionType
                        ActionData =
                            {
                                TransferChxTxActionDto.RecipientAddress = recipientWallet.Address.Value
                                Amount = 0m
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok t -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>

    [<Fact>]
    let ``Validation.validateTx TransferChx invalid Amount, too many decimals`` () =
        let recipientWallet = Signing.generateWallet ()
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = transferChxActionType
                        ActionData =
                            {
                                TransferChxTxActionDto.RecipientAddress = recipientWallet.Address.Value
                                Amount = 12.12345678m
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok t -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>

    [<Fact>]
    let ``Validation.validateTx TransferChx invalid RecipientAddress`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = transferChxActionType
                        ActionData =
                            {
                                TransferChxTxActionDto.RecipientAddress = ""
                                Amount = 10m
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok t -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length > 0 @>

    [<Fact>]
    let ``Validation.validateTx TransferAsset invalid FromAccountHash`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = transferAssetActionType
                        ActionData =
                            {
                                TransferAssetTxActionDto.FromAccountHash = ""
                                ToAccountHash = accountHash2.Value
                                AssetHash = assetHash.Value
                                Amount = 12m
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok t -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>

    [<Fact>]
    let ``Validation.validateTx TransferAsset invalid ToAccountHash`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = transferAssetActionType
                        ActionData =
                            {
                                TransferAssetTxActionDto.FromAccountHash = accountHash1.Value
                                ToAccountHash = ""
                                AssetHash = assetHash.Value
                                Amount = 12m
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok t -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>

    [<Fact>]
    let ``Validation.validateTx TransferAsset same FromAccountHash and ToAccountHash`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = transferAssetActionType
                        ActionData =
                            {
                                TransferAssetTxActionDto.FromAccountHash = accountHash1.Value
                                ToAccountHash = accountHash1.Value
                                AssetHash = assetHash.Value
                                Amount = 12m
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok t -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>

    [<Fact>]
    let ``Validation.validateTx TransferAsset invalid Asset`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = transferAssetActionType
                        ActionData =
                            {
                                TransferAssetTxActionDto.FromAccountHash = accountHash1.Value
                                ToAccountHash = accountHash2.Value
                                AssetHash = ""
                                Amount = 12m
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok t -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>

    [<Fact>]
    let ``Validation.validateTx TransferAsset invalid Amount`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = transferAssetActionType
                        ActionData =
                            {
                                TransferAssetTxActionDto.FromAccountHash = accountHash1.Value
                                ToAccountHash = accountHash2.Value
                                AssetHash = assetHash.Value
                                Amount = 0m
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok t -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>

    [<Fact>]
    let ``Validation.validateTx TransferAsset invalid Amount, too many decimals`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = transferAssetActionType
                        ActionData =
                            {
                                TransferAssetTxActionDto.FromAccountHash = accountHash1.Value
                                ToAccountHash = accountHash2.Value
                                AssetHash = assetHash.Value
                                Amount = 10.12345678m
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok t -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>

    [<Fact>]
    let ``Validation.validateTx validate action`` () =
        let recipientWallet = Signing.generateWallet ()
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = transferChxActionType
                        ActionData =
                            {
                                TransferChxTxActionDto.RecipientAddress = recipientWallet.Address.Value
                                Amount = 10m
                            }
                    }
                    {
                        ActionType = transferAssetActionType
                        ActionData =
                            {
                                TransferAssetTxActionDto.FromAccountHash = accountHash1.Value
                                ToAccountHash = accountHash2.Value
                                AssetHash = assetHash.Value
                                Amount = 1m
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok t ->
            let expectedChx = (testTx.Actions.[0].ActionData :?> TransferChxTxActionDto)
            let actualChx = t.Actions.[0] |> Helpers.extractActionData<TransferChxTxAction>

            let expAsset = (testTx.Actions.[1].ActionData :?> TransferAssetTxActionDto)
            let actualAsset = t.Actions.[1] |> Helpers.extractActionData<TransferAssetTxAction>

            test <@ t.ActionFee = ChxAmount testTx.ActionFee @>
            test <@ t.Nonce = Nonce testTx.Nonce @>
            test <@ t.TxHash = txHash @>
            test <@ t.Sender = chAddress @>
            test <@ actualChx.Amount = ChxAmount expectedChx.Amount @>
            test <@ actualChx.RecipientAddress = BlockchainAddress expectedChx.RecipientAddress @>
            test <@ actualAsset.FromAccountHash = AccountHash expAsset.FromAccountHash @>
            test <@ actualAsset.ToAccountHash = AccountHash expAsset.ToAccountHash @>
            test <@ actualAsset.AssetHash = AssetHash expAsset.AssetHash @>
            test <@ actualAsset.Amount = AssetAmount expAsset.Amount @>
        | Error errors ->
            failwithf "%A" errors

    let private isValidAddressMock (address : BlockchainAddress) =
        let item = address.Value
        String.IsNullOrWhiteSpace(item) |> not

    let private isValidHashMock (hash : string) =
        true

    [<Fact>]
    let ``Validation.validateTx CreateAssetEmission valid action`` () =
        let expected =
            {
                CreateAssetEmissionTxActionDto.EmissionAccountHash = "AAA"
                AssetHash = "BBB"
                Amount = 100m
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = createAssetEmissionActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t ->
            let actual = Helpers.extractActionData<CreateAssetEmissionTxAction> t.Actions.Head
            test <@ AccountHash expected.EmissionAccountHash = actual.EmissionAccountHash @>
            test <@ AssetHash expected.AssetHash = actual.AssetHash @>
            test <@ AssetAmount expected.Amount = actual.Amount @>
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``Validation.validateTx CreateAssetEmission invalid action`` () =
        let expected =
            {
                CreateAssetEmissionTxActionDto.EmissionAccountHash = ""
                AssetHash = ""
                Amount = 0m
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = setAccountControllerActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t -> failwith "This test should fail"
        | Error e ->
            test <@ e.Length = 3 @>

    [<Fact>]
    let ``Validation.validateTx CreateAssetEmission invalid action, amount too big`` () =
        let expected =
            {
                CreateAssetEmissionTxActionDto.EmissionAccountHash = "AAA"
                AssetHash = "BBB"
                Amount = 999999999999m
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = createAssetEmissionActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t -> failwith "This test should fail"
        | Error e ->
            test <@ e.Length = 1 @>

    [<Fact>]
    let ``Validation.validateTx CreateAccount valid action`` () =
        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = createAccountActionType
                        ActionData = CreateAccountTxActionDto()
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t ->
            test <@ t.Actions.Head = CreateAccount @>
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``Validation.validateTx CreateAsset valid action`` () =
        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = createAssetActionType
                        ActionData = CreateAssetTxActionDto()
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t ->
            test <@ t.Actions.Head = CreateAsset @>
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``Validation.validateTx SetAccountController valid action`` () =
        let expected =
            {
                SetAccountControllerTxActionDto.AccountHash = "A"
                ControllerAddress = chAddress.Value
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = setAccountControllerActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t ->
            let actual = Helpers.extractActionData<SetAccountControllerTxAction> t.Actions.Head
            test <@ AccountHash expected.AccountHash = actual.AccountHash @>
            test <@ BlockchainAddress expected.ControllerAddress = actual.ControllerAddress @>
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``Validation.validateTx SetAccountController invalid action`` () =
        let expected =
            {
                SetAccountControllerTxActionDto.AccountHash = ""
                ControllerAddress = ""
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = setAccountControllerActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t -> failwith "This test should fail"
        | Error e ->
            test <@ e.Length = 2 @>

    [<Fact>]
    let ``Validation.validateTx SetAssetController valid action`` () =
        let expected =
            {
                SetAssetControllerTxActionDto.AssetHash = "A"
                ControllerAddress = chAddress.Value
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = setAssetControllerActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t ->
            let actual = Helpers.extractActionData<SetAssetControllerTxAction> t.Actions.Head
            test <@ AssetHash expected.AssetHash = actual.AssetHash @>
            test <@ BlockchainAddress expected.ControllerAddress = actual.ControllerAddress @>
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``Validation.validateTx SetAssetController invalid action`` () =
        let expected =
            {
                SetAssetControllerTxActionDto.AssetHash = ""
                ControllerAddress = ""
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = setAssetControllerActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t -> failwith "This test should fail"
        | Error e ->
            test <@ e.Length = 2 @>

    [<Fact>]
    let ``Validation.validateTx SetAssetCode valid action`` () =
        let expected =
            {
                SetAssetCodeTxActionDto.AssetHash = "A"
                AssetCode = "B"
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = setAssetCodeActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t ->
            let actual = Helpers.extractActionData<SetAssetCodeTxAction> t.Actions.Head
            test <@ AssetHash expected.AssetHash = actual.AssetHash @>
            test <@ AssetCode expected.AssetCode = actual.AssetCode @>
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``Validation.validateTx SetAssetCode invalid action`` () =
        let expected =
            {
                SetAssetCodeTxActionDto.AssetHash = ""
                AssetCode = "A"
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = setAssetCodeActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t -> failwith "This test should fail"
        | Error e ->
            test <@ e.Length = 1 @>

    [<Fact>]
    let ``Validation.validateTx SetAssetCode code is too long`` () =
        let expected =
            {
                SetAssetCodeTxActionDto.AssetHash = "A"
                AssetCode = "ABCDEFGHIJK0123456789"
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = setAssetCodeActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t -> failwith "This test should fail"
        | Error e ->
            test <@ e.Length = 1 @>

    [<Fact>]
    let ``Validation.validateTx SetAssetCode code has invalid chars`` () =
        let expected =
            {
                SetAssetCodeTxActionDto.AssetHash = "A"
                AssetCode = "AaabcZ"
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = setAssetCodeActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t -> failwith "This test should fail"
        | Error e ->
            test <@ e.Length = 1 @>

    [<Fact>]
    let ``Validation.validateTx ConfigureValidator valid action`` () =
        let expected =
            {
                ConfigureValidatorTxActionDto.NetworkAddress = "A"
                SharedRewardPercent = 42m
                IsEnabled = true
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = configureValidatorActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t ->
            let actual = Helpers.extractActionData<ConfigureValidatorTxAction> t.Actions.Head
            test <@ expected.NetworkAddress = actual.NetworkAddress.Value @>
            test <@ expected.SharedRewardPercent = actual.SharedRewardPercent @>
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``Validation.validateTx ConfigureValidator invalid action`` () =
        let expected =
            {
                ConfigureValidatorTxActionDto.NetworkAddress = ""
                SharedRewardPercent = 0m
                IsEnabled = true
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = configureValidatorActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t -> failwith "This test should fail"
        | Error e ->
            test <@ e.Length = 1 @>

    [<Fact>]
    let ``Validation.validateTx ConfigureValidator invalid action too may decimals`` () =
        let expected =
            {
                ConfigureValidatorTxActionDto.NetworkAddress = "A"
                SharedRewardPercent = 42.123m
                IsEnabled = true
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = configureValidatorActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t -> failwith "This test should fail"
        | Error e ->
            test <@ e.Length = 1 @>

    [<Fact>]
    let ``Validation.validateTx DelegateStake valid action`` () =
        let expected =
            {
                DelegateStakeTxActionDto.ValidatorAddress = "A"
                Amount = 1000m
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = delegateStakeActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t ->
            let actual = Helpers.extractActionData<DelegateStakeTxAction> t.Actions.Head
            test <@ BlockchainAddress expected.ValidatorAddress = actual.ValidatorAddress @>
            test <@ ChxAmount expected.Amount = actual.Amount @>
        | Error e -> failwithf "%A" e

    [<Fact>]
    let ``Validation.validateTx DelegateStake invalid action`` () =
        let expected =
            {
                DelegateStakeTxActionDto.ValidatorAddress = ""
                Amount = 0m
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = delegateStakeActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t -> failwith "This test should fail"
        | Error e ->
            test <@ e.Length = 2 @>

    [<Fact>]
    let ``Validation.validateTx DelegateStake invalid action - amount less than allowed`` () =
        let expected =
            {
                DelegateStakeTxActionDto.ValidatorAddress = "A"
                Amount = -(Utils.maxBlockchainNumeric + 1m)
            }

        let tx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = delegateStakeActionType
                        ActionData = expected
                    }
                ]
        }

        match
            Validation.validateTx isValidHashMock isValidAddressMock Helpers.maxActionCountPerTx chAddress txHash tx
            with
        | Ok t -> failwith "This test should fail"
        | Error e ->
            test <@ e.Length = 1 @>
            test <@ e.Head.Message.Contains "cannot be less than" @>

    [<Fact>]
    let ``Validation.validateEquivocationProof rejects proof with wrong order of hashes`` () =
        // ARRANGE
        let equivocationProofDto : EquivocationProofDto =
            {
                BlockNumber = 1L
                ConsensusRound = 0
                ConsensusStep = 1uy
                EquivocationValue1 = "B"
                EquivocationValue2 = "A"
                Signature1 = "S1"
                Signature2 = "S2"
            }

        // ACT
        let result =
            Validation.validateEquivocationProof
                (fun _ _ -> None)
                (fun _ _ _ _ _ -> "")
                (fun _ -> Array.empty)
                (fun _ -> "")
                equivocationProofDto

        // ASSERT
        match result with
        | Ok _ -> failwith "This test should fail"
        | Error e ->
            test <@ e.Length = 1 @>
            test <@ e.[0].Message.Contains("Values in equivocation proof must be ordered") @>

    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // Trade Orders
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    [<Fact>]
    let ``Validation.validateTx PlaceTradeOrder invalid action data`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = placeTradeOrderActionType
                        ActionData =
                            {
                                PlaceTradeOrderTxActionDto.AccountHash = ""
                                BaseAssetHash = ""
                                QuoteAssetHash = ""
                                Side = ""
                                Amount = 0m
                                OrderType = ""
                                LimitPrice = 0m
                                StopPrice = 0m
                                TrailingOffset = 0m
                                TrailingOffsetIsPercentage = false
                                TimeInForce = ""
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok _ -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 8 @>
            test <@ errors.[0].Message = "AccountHash is not provided" @>
            test <@ errors.[1].Message = "BaseAssetHash is not provided" @>
            test <@ errors.[2].Message = "QuoteAssetHash is not provided" @>
            test <@ errors.[3].Message = "QuoteAssetHash cannot be the same as BaseAssetHash" @>
            test <@ errors.[4].Message = "Side must have a valid value" @>
            test <@ errors.[5].Message = "Amount must be greater than zero" @>
            test <@ errors.[6].Message = "OrderType must have a valid value" @>
            test <@ errors.[7].Message = "TimeInForce must have a valid value" @>

    [<Fact>]
    let ``Validation.validateTx PlaceTradeOrder prices not accepted in unrelated orders`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = placeTradeOrderActionType
                        ActionData =
                            {
                                PlaceTradeOrderTxActionDto.AccountHash = accountHash1.Value
                                BaseAssetHash = assetHash.Value
                                QuoteAssetHash = assetHash2.Value
                                Side = "BUY"
                                Amount = 100m
                                OrderType = "MARKET"
                                LimitPrice = 1m
                                StopPrice = 1m
                                TrailingOffset = 1m
                                TrailingOffsetIsPercentage = false
                                TimeInForce = "IOC"
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok _ -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 3 @>
            test <@ errors.[0].Message = "LimitPrice in non-LIMIT order types should not be set" @>
            test <@ errors.[1].Message = "StopPrice in non-STOP order types should not be set" @>
            test <@ errors.[2].Message = "TrailingOffset in non-TRAILING order types should not be set" @>

    [<Fact>]
    let ``Validation.validateTx PlaceTradeOrder LimitPrice mandatory in LIMIT orders`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = placeTradeOrderActionType
                        ActionData =
                            {
                                PlaceTradeOrderTxActionDto.AccountHash = accountHash1.Value
                                BaseAssetHash = assetHash.Value
                                QuoteAssetHash = assetHash2.Value
                                Side = "BUY"
                                Amount = 100m
                                OrderType = "LIMIT"
                                LimitPrice = 0m
                                StopPrice = 0m
                                TrailingOffset = 0m
                                TrailingOffsetIsPercentage = false
                                TimeInForce = "GTC"
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok _ -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>
            test <@ errors.[0].Message = "LimitPrice in LIMIT order types must be greater than zero" @>

    [<Fact>]
    let ``Validation.validateTx PlaceTradeOrder StopPrice mandatory in STOP orders`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = placeTradeOrderActionType
                        ActionData =
                            {
                                PlaceTradeOrderTxActionDto.AccountHash = accountHash1.Value
                                BaseAssetHash = assetHash.Value
                                QuoteAssetHash = assetHash2.Value
                                Side = "BUY"
                                Amount = 100m
                                OrderType = "STOP_MARKET"
                                LimitPrice = 0m
                                StopPrice = 0m
                                TrailingOffset = 0m
                                TrailingOffsetIsPercentage = false
                                TimeInForce = "IOC"
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok _ -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>
            test <@ errors.[0].Message = "StopPrice in STOP order types must be greater than zero" @>

    [<Fact>]
    let ``Validation.validateTx PlaceTradeOrder TrailingOffset mandatory in TRAILING orders`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = placeTradeOrderActionType
                        ActionData =
                            {
                                PlaceTradeOrderTxActionDto.AccountHash = accountHash1.Value
                                BaseAssetHash = assetHash.Value
                                QuoteAssetHash = assetHash2.Value
                                Side = "BUY"
                                Amount = 100m
                                OrderType = "TRAILING_STOP_MARKET"
                                LimitPrice = 0m
                                StopPrice = 1m
                                TrailingOffset = 0m
                                TrailingOffsetIsPercentage = false
                                TimeInForce = "IOC"
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok _ -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>
            test <@ errors.[0].Message = "TrailingOffset in TRAILING order types must be greater than zero" @>

    [<Fact>]
    let ``Validation.validateTx PlaceTradeOrder numbers should not be greater than max`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = placeTradeOrderActionType
                        ActionData =
                            {
                                PlaceTradeOrderTxActionDto.AccountHash = accountHash1.Value
                                BaseAssetHash = assetHash.Value
                                QuoteAssetHash = assetHash2.Value
                                Side = "BUY"
                                Amount = Utils.maxBlockchainNumeric + 1m
                                OrderType = "TRAILING_STOP_LIMIT"
                                LimitPrice = Utils.maxBlockchainNumeric + 1m
                                StopPrice = Utils.maxBlockchainNumeric + 1m
                                TrailingOffset = Utils.maxBlockchainNumeric + 1m
                                TrailingOffsetIsPercentage = false
                                TimeInForce = "IOC"
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok _ -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 4 @>
            test <@ errors.[0].Message.StartsWith("Amount cannot be greater than") @>
            test <@ errors.[1].Message.StartsWith("LimitPrice cannot be greater than") @>
            test <@ errors.[2].Message.StartsWith("StopPrice cannot be greater than") @>
            test <@ errors.[3].Message.StartsWith("TrailingOffset cannot be greater than") @>

    [<Fact>]
    let ``Validation.validateTx PlaceTradeOrder numbers must be rounded to allowed number of decimals`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = placeTradeOrderActionType
                        ActionData =
                            {
                                PlaceTradeOrderTxActionDto.AccountHash = accountHash1.Value
                                BaseAssetHash = assetHash.Value
                                QuoteAssetHash = assetHash2.Value
                                Side = "BUY"
                                Amount = 0.00000001m
                                OrderType = "TRAILING_STOP_LIMIT"
                                LimitPrice = 0.00000001m
                                StopPrice = 0.00000001m
                                TrailingOffset = 0.00000001m
                                TrailingOffsetIsPercentage = false
                                TimeInForce = "IOC"
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok _ -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 4 @>
            test <@ errors.[0].Message = "Amount must have at most 7 decimals" @>
            test <@ errors.[1].Message = "LimitPrice must have at most 7 decimals" @>
            test <@ errors.[2].Message = "StopPrice must have at most 7 decimals" @>
            test <@ errors.[3].Message = "TrailingOffset must have at most 7 decimals" @>

    [<Fact>]
    let ``Validation.validateTx PlaceTradeOrder TrailingOffset percentage must be rounded to 2 decimals`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = placeTradeOrderActionType
                        ActionData =
                            {
                                PlaceTradeOrderTxActionDto.AccountHash = accountHash1.Value
                                BaseAssetHash = assetHash.Value
                                QuoteAssetHash = assetHash2.Value
                                Side = "BUY"
                                Amount = 100m
                                OrderType = "TRAILING_STOP_LIMIT"
                                LimitPrice = 1m
                                StopPrice = 1m
                                TrailingOffset = 0.001m
                                TrailingOffsetIsPercentage = true
                                TimeInForce = "IOC"
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok _ -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>
            test <@ errors.[0].Message = "TrailingOffset percentage must have at most 2 decimals" @>

    [<Fact>]
    let ``Validation.validateTx PlaceTradeOrder TrailingOffset percentage must be less than 100`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = placeTradeOrderActionType
                        ActionData =
                            {
                                PlaceTradeOrderTxActionDto.AccountHash = accountHash1.Value
                                BaseAssetHash = assetHash.Value
                                QuoteAssetHash = assetHash2.Value
                                Side = "BUY"
                                Amount = 100m
                                OrderType = "TRAILING_STOP_LIMIT"
                                LimitPrice = 1m
                                StopPrice = 1m
                                TrailingOffset = 100m
                                TrailingOffsetIsPercentage = true
                                TimeInForce = "IOC"
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok _ -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>
            test <@ errors.[0].Message = "TrailingOffset percentage must be less than 100%" @>

    [<Fact>]
    let ``Validation.validateTx PlaceTradeOrder TrailingOffsetIsPercentage valid only for TRAILING orders`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = placeTradeOrderActionType
                        ActionData =
                            {
                                PlaceTradeOrderTxActionDto.AccountHash = accountHash1.Value
                                BaseAssetHash = assetHash.Value
                                QuoteAssetHash = assetHash2.Value
                                Side = "BUY"
                                Amount = 100m
                                OrderType = "MARKET"
                                LimitPrice = 0m
                                StopPrice = 0m
                                TrailingOffset = 0m
                                TrailingOffsetIsPercentage = true
                                TimeInForce = "IOC"
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok _ -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>
            test <@ errors.[0].Message = "TrailingOffsetIsPercentage in non-TRAILING order types should not be set" @>

    [<Fact>]
    let ``Validation.validateTx CancelTradeOrder invalid action data`` () =
        let testTx = {
            SenderAddress = chAddress.Value
            Nonce = 10L
            ExpirationTime = 0L
            ActionFee = 1m
            Actions =
                [
                    {
                        ActionType = cancelTradeOrderActionType
                        ActionData =
                            {
                                CancelTradeOrderTxActionDto.TradeOrderHash = ""
                            }
                    }
                ]
        }

        let result =
            Validation.validateTx
                Hashing.isValidHash
                Hashing.isValidBlockchainAddress
                Helpers.maxActionCountPerTx
                chAddress
                txHash
                testTx

        match result with
        | Ok _ -> failwith "Validation should fail in case of this test"
        | Error errors ->
            test <@ errors.Length = 1 @>
            test <@ errors.[0].Message = "TradeOrderHash is not provided" @>
