// Polyfill so C# records and init-only setters compile on netstandard2.0, which
// predates this type. Compiler-only; never referenced at runtime.
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
