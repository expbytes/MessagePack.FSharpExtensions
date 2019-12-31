[<AutoOpen>]
module MessagePack.Tests.Helper

open System
open MessagePack
open MessagePack.FSharp
open MessagePack.Resolvers

let setupMessagePackSerializer =
  let resolvers : IFormatterResolver[] = [| 
      FSharpResolver.Instance; 
      StandardResolver.Instance;
    |]

  let compositeResolver = CompositeResolver.Create resolvers
  let options = MessagePackSerializerOptions.Standard.WithResolver(compositeResolver)

  MessagePackSerializer.DefaultOptions <- options

let convert<'T> (value: 'T) =  
  setupMessagePackSerializer

  value
    |> MessagePackSerializer.Serialize
    |> ReadOnlyMemory<byte>
    |> MessagePackSerializer.Deserialize<'T>

let convertTo<'T, 'U> (value: 'T) =
  setupMessagePackSerializer

  value
    |> MessagePackSerializer.Serialize
    |> ReadOnlyMemory<byte>
    |> MessagePackSerializer.Deserialize<'U>

