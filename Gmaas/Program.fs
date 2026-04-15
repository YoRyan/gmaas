open Gmaas.Config
open Gmaas.Http
open System.IO
open System.Threading.Tasks
open Tomlyn
open Tomlyn.Model

let doAuth (configFile: FileInfo) =
    task {
        use configFile = configFile.Open FileMode.Open
        let config = TomlSerializer.Deserialize<TomlTable> configFile
        return! doAuthFlow config
    }

let doServe (configFile: FileInfo) =
    task {
        use configFile = configFile.Open FileMode.Open
        let config = TomlSerializer.Deserialize<TomlTable> configFile
        let! config = loadServeConfig config

        do! serveHttpAsync config
    }

[<EntryPoint>]
let main arg =
    task {
        let config = System.CommandLine.Option<FileInfo> "--config"
        config.Description <- "Path to TOML configuration file"
        config.Required <- true
        config.Recursive <- true

        let root = System.CommandLine.RootCommand "gmaas: Gmail as a Service"
        root.Add config

        let auth =
            System.CommandLine.Command(
                "auth",
                "Test the connection to Gmail, obtaining tokens from Google if necessary"
            )

        auth.SetAction(fun result -> (doAuth (result.GetRequiredValue config): Task))
        root.Subcommands.Add auth

        let serve = System.CommandLine.Command("serve", "Run the service")
        serve.SetAction(fun result -> (doServe (result.GetRequiredValue config): Task))
        root.Subcommands.Add serve

        return (root.Parse arg).Invoke()
    }
    |> Async.AwaitTask
    |> Async.RunSynchronously
