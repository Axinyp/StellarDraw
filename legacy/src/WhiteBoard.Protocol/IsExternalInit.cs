// netstandard2.0 does not include IsExternalInit, required for C# record types.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{ }