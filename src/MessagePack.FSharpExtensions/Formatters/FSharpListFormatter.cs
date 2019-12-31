// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using Microsoft.FSharp.Collections;
using MessagePack.Formatters;

namespace MessagePack.FSharp.Formatters
{
    public sealed class FSharpListFormatter<T> : CollectionFormatterBase<T, T[], IEnumerator<T>, FSharpList<T>>
    {
        protected override void Add(T[] collection, int index, T value, MessagePackSerializerOptions options)
        {
            collection[index] = value;
        }

        protected override FSharpList<T> Complete(T[] intermediateCollection)
        {
            return ListModule.OfArray(intermediateCollection);
        }

        protected override T[] Create(int count, MessagePackSerializerOptions options)
        {
            return new T[count];
        }

        protected override IEnumerator<T> GetSourceEnumerator(FSharpList<T> source)
        {
            return ((IEnumerable<T>)source).GetEnumerator();
        }
    }
}