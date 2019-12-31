// Copyright (c) All contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Reflection;
using System.Reflection.Emit;

namespace MessagePack.FSharp.Internal
{
    internal class DynamicAssembly
    {
        private readonly AssemblyBuilder assemblyBuilder;
        private readonly ModuleBuilder moduleBuilder;

        private readonly object gate = new object();

        public DynamicAssembly(string moduleName)
        {
            this.assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(moduleName), AssemblyBuilderAccess.Run);
            this.moduleBuilder = assemblyBuilder.DefineDynamicModule(moduleName + ".dll");
        }

        public TypeBuilder DefineType(string name, TypeAttributes attr)
        {
            lock (this.gate)
            {
                return this.moduleBuilder.DefineType(name, attr);
            }
        }

        public TypeBuilder DefineType(string name, TypeAttributes attr, Type parent)
        {
            lock (this.gate)
            {
                return this.moduleBuilder.DefineType(name, attr, parent);
            }
        }

        public TypeBuilder DefineType(string name, TypeAttributes attr, Type parent, Type[] interfaces)
        {
            lock (this.gate)
            {
                return this.moduleBuilder.DefineType(name, attr, parent, interfaces);
            }
        }
    }
}