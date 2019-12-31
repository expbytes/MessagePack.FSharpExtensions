// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using MessagePack.Formatters;
using Microsoft.FSharp.Collections;

namespace MessagePack.FSharp.Formatters
{
    public sealed class FSharpSetFormatter<T> : CollectionFormatterBase<T, T[], IEnumerator<T>, FSharpSet<T>>
    {
        protected override void Add(T[] collection, int index, T value, MessagePackSerializerOptions options)
        {
            collection[index] = value;
        }

        protected override FSharpSet<T> Complete(T[] intermediateCollection)
        {
            return SetModule.OfArray(intermediateCollection);
        }

        protected override T[] Create(int count, MessagePackSerializerOptions options)
        {
            return new T[count];
        }

        protected override IEnumerator<T> GetSourceEnumerator(FSharpSet<T> source)
        {
            return ((IEnumerable<T>)source).GetEnumerator();
        }
    }
}
