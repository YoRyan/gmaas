open System.IO
open System.Threading.Tasks

open Tomlyn
open Tomlyn.Model

open ForTheRecord.Config
open ForTheRecord.Http

let doTestGmail (configFile: FileInfo) =
    task {
        use configFile = configFile.Open FileMode.Open
        let config = TomlSerializer.Deserialize<TomlTable> configFile
        return! testGmail config
    }

let doServeGmail (configFile: FileInfo) =
    task {
        use configFile = configFile.Open FileMode.Open
        let config = TomlSerializer.Deserialize<TomlTable> configFile
        let! config = loadServeConfig loadGmailConfig config

        do! serveHttpAsync config
    }

[<EntryPoint>]
let main arg =
    task {
        let config = System.CommandLine.Option<FileInfo> "--config"
        config.Description <- "Path to TOML configuration file"
        config.Required <- true
        config.Recursive <- true

        let root =
            System.CommandLine.RootCommand "ForTheRecord: Import notifications into your webmail inbox."

        root.Add config

        let testGmail =
            System.CommandLine.Command(
                "test-gmail",
                "Test the connection to Gmail, obtaining tokens from Google if necessary"
            )

        testGmail.SetAction(fun result -> (doTestGmail (result.GetRequiredValue config): Task))
        root.Subcommands.Add testGmail

        let serveGmail =
            System.CommandLine.Command("serve-gmail", "Run the service using the configured Gmail inbox")

        serveGmail.SetAction(fun result -> (doServeGmail (result.GetRequiredValue config): Task))
        root.Subcommands.Add serveGmail

        return (root.Parse arg).Invoke()
    }
    |> Async.AwaitTask
    |> Async.RunSynchronously
