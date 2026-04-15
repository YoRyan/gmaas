module Tests.Helpers

open Gmaas.Helpers
open System.Collections.Generic
open Xunit

[<Fact>]
let ``Simple header override works`` () =
    let expected =
        seq {
            "From", "bob@example.com"
            "Content-Type", "text/plain"
        }

    let actual =
        (seq {
            "From", "bob@example.com"
            "Content-Type", "text/html"
        })
        |> overrideHeaders [ "Content-Type", "text/plain" ]

    Assert.Equal<IEnumerable<(string * string)>>(expected, actual)

[<Fact>]
let ``Header override is case-insensitive`` () =
    let expected = seq { "coNtent-tYpE", "text/plain" }

    let actual =
        seq { "Content-Type", "text/html" }
        |> overrideHeaders [ "coNtent-tYpE", "text/plain" ]

    Assert.Equal<IEnumerable<(string * string)>>(expected, actual)

[<Fact>]
let ``Header override preserves duplicates in source`` () =
    let expected =
        seq {
            "To", "bob@example.com"
            "To", "alice@example.com"
            "Content-Type", "text/plain"
        }

    let actual =
        seq {
            "To", "bob@example.com"
            "To", "alice@example.com"
        }
        |> overrideHeaders [ "Content-Type", "text/plain" ]

    Assert.Equal<IEnumerable<(string * string)>>(expected, actual)

[<Fact>]
let ``Header override preserves duplicates in overrides`` () =
    let expected =
        seq {
            "To", "bob@example.com"
            "Content-Type", "text/plain"
            "Content-Type", "text/html"
        }

    let actual =
        seq { "To", "bob@example.com" }
        |> overrideHeaders [ "Content-Type", "text/plain"; "Content-Type", "text/html" ]

    Assert.Equal<IEnumerable<(string * string)>>(expected, actual)

[<Fact>]
let ``Full header override chain works`` () =
    let ``default`` =
        seq {
            "From", "me"
            "To", "me"
            "Content-Type", "text/plain"
        }

    let submitted =
        [ "From", "bob@example.com"
          "Subject", "Hello, World!"
          "Content-Type", "text/html"
          "Date", "SomeFakeDate" ]

    let ``override`` = [ "Date", "SomeRealDate" ]

    let expected =
        seq {
            "To", "me"
            "From", "bob@example.com"
            "Subject", "Hello, World!"
            "Content-Type", "text/html"
            "Date", "SomeRealDate"
        }

    let actual =
        ``default`` |> overrideHeaders submitted |> overrideHeaders ``override``

    Assert.Equal<IEnumerable<(string * string)>>(expected, actual)
