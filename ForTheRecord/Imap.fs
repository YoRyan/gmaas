module ForTheRecord.Imap

open System
open System.Threading.Tasks

type IImapInbox =
    abstract member Append: message: ReadOnlySpan<byte> * ?flag: string list * ?date: DateTime -> Task<unit>
