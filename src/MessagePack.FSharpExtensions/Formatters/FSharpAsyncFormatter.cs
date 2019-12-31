// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.FSharp.Control;
using MessagePack.Formatters;

namespace MessagePack.FSharp.Formatters
{
    public sealed class FSharpAsyncFormatter<T> : IMessagePackFormatter<FSharpAsync<T>>
    {

        public FSharpAsyncFormatter() { }

        public void Serialize(ref MessagePackWriter writer, FSharpAsync<T> value, MessagePackSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNil();
            }
            else
            {
                var v = FSharpAsync.RunSynchronously(value, null, null);

                options.Resolver.GetFormatterWithVerify<T>().Serialize(ref writer, v, options);
            }
        }

        public FSharpAsync<T> Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            if (reader.TryReadNil())
            {
                return default;
            }

            var v = options.Resolver.GetFormatterWithVerify<T>().Deserialize(ref reader, options);

            return Microsoft.FSharp.Core.ExtraTopLevelOperators.DefaultAsyncBuilder.Return(v);
        }
    }
}
