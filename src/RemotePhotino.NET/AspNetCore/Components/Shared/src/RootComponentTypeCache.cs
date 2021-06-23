// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace PeakSWC.RemoteBlazorWebView.Windows
{
    // A cache for root component types
    internal class RootComponentTypeCache
    {
        private readonly ConcurrentDictionary<Key, Type?> _typeToKeyLookUp = new();

        public Type? GetRootComponent(string assembly, string type)
        {
            var key = new Key(assembly, type);
            if (_typeToKeyLookUp.TryGetValue(key, out var resolvedType))
            {
                return resolvedType;
            }
            else
            {
                return _typeToKeyLookUp.GetOrAdd(key, ResolveType, AppDomain.CurrentDomain.GetAssemblies());
            }
        }

        private static Type? ResolveType(Key key, Assembly[] assemblies)
        {
            Assembly? assembly = null;
            for (var i = 0; i < assemblies.Length; i++)
            {
                var current = assemblies[i];
                if (current.GetName().Name == key.Assembly)
                {
                    assembly = current;
                    break;
                }
            }

            if (assembly == null)
            {
                return null;
            }

            return assembly.GetType(key.Type, throwOnError: false, ignoreCase: false);
        }

        private readonly struct Key : IEquatable<Key>
        {
            public Key(string assembly, string type) =>
                (Assembly, Type) = (assembly, type);

            public string Assembly { get; }

            public string Type { get; }

            public override bool Equals(object? obj) => obj is Key key && Equals(key);

            public bool Equals(Key other) => string.Equals(Assembly, other.Assembly, StringComparison.Ordinal) &&
                string.Equals(Type, other.Type, StringComparison.Ordinal);

            public override int GetHashCode() => HashCode.Combine(Assembly, Type);
        }
    }
}
