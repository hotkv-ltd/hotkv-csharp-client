// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

// netstandard2.1 predates C# 9 init-only setters and records; the compiler
// needs this marker type to exist somewhere for either feature to compile.
// net8.0 already provides it in the BCL, so this polyfill only takes part
// in the netstandard2.1 build of this multi-targeted package.
#if NETSTANDARD2_1
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
