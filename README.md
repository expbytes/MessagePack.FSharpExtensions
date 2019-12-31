# MessagePack.FSharpExtensions

Forked version of  [MessagePack.FSharpExtensions](https://github.com/pocketberserker/MessagePack.FSharpExtensions) library which is a extension of [MessagePack-CSharp](https://github.com/neuecc/MessagePack-CSharp) for F#.

This version brings support to MessagePack-CSharp 2.0.x version and drops support of net45 and netstandard1.6.

## Usage

```fsharp
open System
open MessagePack
open MessagePack.Resolvers
open MessagePack.FSharp

CompositeResolver.RegisterAndSetAsDefault(
  FSharpResolver.Instance,
  StandardResolver.Instance
)

[<MessagePackObject>]
type UnionSample =
  | Foo of XYZ : int
  | Bar of OPQ : string list

let data = Foo 999

let bin = MessagePackSerializer.Serialize(data)

match (bin |> ReadOnlyMemory<byte> |> ReadMessagePackSerializer.Deserialize<UnionSample>) with
| Foo x ->
  printfn "%d" x
| Bar xs ->
  printfn "%A" xs
```
