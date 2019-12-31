// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.FSharp.Core;
using MessagePack.Formatters;

namespace MessagePack.FSharp.Formatters
{
    public sealed class UnitFormatter : IMessagePackFormatter<Unit>
    {

        public UnitFormatter() { }

        public void Serialize(ref MessagePackWriter writer, Unit value, MessagePackSerializerOptions options)
        {
            writer.WriteNil();
        }

        public Unit Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            reader.ReadNil();

            return null;
        }
    }
}
