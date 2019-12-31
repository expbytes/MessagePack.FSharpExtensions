module Program

open System.Collections.Immutable
open MessagePack
open MessagePack.Resolvers
open MessagePack.FSharp
open MessagePack.ImmutableCollection
open Benchmark
open System

[<MessagePackObject>]
type UnionSample =
  | Foo of XYZ : int
  | Bar of OPQ : string list

type StringKeyUnionSample =
  | StringFoo of XYZ : int
  | StringBar of OPQ : string list

module Benchmark =

  let iteration = 10000

  let private impl<'T> serialize (deserialize: byte [] -> 'T) name (target: 'T) =

    let mutable data: byte [] = null

    printfn "%s serialization test" typeof<'T>.Name
    printfn ""

    printf "Serialize:: "

    using (new Measure(name)) (fun _ ->
      for i in [|1..iteration|] do
        data <- serialize target
    )

    printf "Deserialize:: "

    using (new Measure(name)) (fun _ ->      
      for i in [|1..iteration|] do
        deserialize data
        |> ignore
    )

    printfn ""

  let msgpack<'T> name (target: 'T) =
    impl<'T> 
      (fun x -> 
        MessagePackSerializer.Serialize(x)
      )
      (fun x -> 
        x |> ReadOnlyMemory<byte> |> MessagePackSerializer.Deserialize<'T>
      ) 
      name 
      target

[<EntryPoint>]
let main _ =

  let resolvers : IFormatterResolver[] = [| 
    ImmutableCollectionResolver.Instance; 
    FSharpResolver.Instance; 
    StandardResolver.Instance;
  |]

  let compositeResolver = CompositeResolver.Create resolvers
  let options = MessagePackSerializerOptions.Standard.WithResolver(compositeResolver)

  MessagePackSerializer.DefaultOptions <- options
  
  [|1..10000|]
  |> Benchmark.msgpack "MessagePack-CSharp"

  ResizeArray([|1..10000|])
  |> Benchmark.msgpack "MessagePack-CSharp"

  FooClass(XYZ = 99999) :> IUnionSample
  |> Benchmark.msgpack "MessagePack-CSharp"

  ImmutableList<int>.Empty.AddRange([|1..10000|])
  |> Benchmark.msgpack "MessagePack.ImmutableCollection"

  let xs = ImmutableHashSet<int>.Empty
  for i in [|1..10000|] do xs.Add(i) |> ignore
  xs
  |> Benchmark.msgpack "MessagePack.ImmutableCollection"

  let xs = ImmutableDictionary<int, int>.Empty
  for i in [|1..10000|] do xs.Add(i, i) |> ignore
  xs
  |> Benchmark.msgpack "MessagePack.ImmutableCollection"

  let ls = [1..10000]

  ls
  |> Benchmark.msgpack "MessagePack.FSharpExtensions"

  let ss =
    [|1..10000|]
    |> Set.ofArray

  ss
  |> Benchmark.msgpack "MessagePack.FSharpExtensions"

  let ms =
    [|1..10000|]
    |> Array.map (fun x -> (x, x))
    |> Map.ofArray

  ms
  |> Benchmark.msgpack "MessagePack.FSharpExtensions" 
  
  Foo 99999
  |> Benchmark.msgpack "MessagePack.FSharpExtensions"

  StringFoo 99999
  |> Benchmark.msgpack "MessagePack.FSharpExtensions"

  0
