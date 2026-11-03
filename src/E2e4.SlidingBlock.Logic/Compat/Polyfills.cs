#if !NET
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

// Members the library uses that the BCL gained after netstandard. Each is internal or an extension, so nothing leaks into the public surface.

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Parameter, Inherited = false)]
    internal sealed class UnscopedRefAttribute : Attribute
    {
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class CallerArgumentExpressionAttribute(string parameterName) : Attribute
    {
        public string ParameterName { get; } = parameterName;
    }
}

#if NETSTANDARD2_0
namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, Inherited = false)]
    internal sealed class NotNullAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class NotNullWhenAttribute(bool returnValue) : Attribute
    {
        public bool ReturnValue { get; } = returnValue;
    }

    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class MaybeNullWhenAttribute(bool returnValue) : Attribute
    {
        public bool ReturnValue { get; } = returnValue;
    }
}
#endif

namespace E2e4.SlidingBlock.Logic
{
    /// <summary>Members of Random, Array and Span that .NET 5 and 6 added.</summary>
    internal static class BclExtensions
    {
        extension(Random random)
        {
            /// <summary>The 63-bit draw of a seeded Random on .NET 6 and later, so the same seed gives the same hash keys on every target.</summary>
            public long NextInt64()
            {
                ulong result;
                do
                {
                    ulong bits = (ulong)(uint)random.Next(1 << 22) | ((ulong)(uint)random.Next(1 << 22) << 22) | ((ulong)(uint)random.Next(1 << 20) << 44);
                    result = bits >> 1;
                }
                while (result == long.MaxValue);
                return (long)result;
            }
        }

        extension(Array)
        {
            public static void Fill<T>(T[] array, T value)
            {
                for (int i = 0; i < array.Length; i++) array[i] = value;
            }
        }

        extension(Span<ushort> span)
        {
            /// <summary>Insertion sort: the spans are a handful of cases long.</summary>
            public void Sort()
            {
                for (int i = 1; i < span.Length; i++)
                {
                    ushort v = span[i];
                    int j = i - 1;
                    for (; j >= 0 && span[j] > v; j--) span[j + 1] = span[j];
                    span[j + 1] = v;
                }
            }
        }
    }

    /// <summary>The argument checks of .NET 6 to 8, so the library reads the same on every target.</summary>
    internal static class ArgumentGuards
    {
        extension(ArgumentNullException)
        {
            public static void ThrowIfNull([NotNull] object? argument, [CallerArgumentExpression(nameof(argument))] string? paramName = null)
            {
                if (argument is null) throw new ArgumentNullException(paramName);
            }
        }

        extension(ArgumentOutOfRangeException)
        {
            public static void ThrowIfNegative(int value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            {
                if (value < 0) throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} must not be negative.");
            }

            public static void ThrowIfNegativeOrZero(long value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            {
                if (value <= 0) throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} must be positive.");
            }

            public static void ThrowIfLessThan(int value, int other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            {
                if (value < other) throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} must be at least {other}.");
            }

            public static void ThrowIfGreaterThan(int value, int other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            {
                if (value > other) throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} must be at most {other}.");
            }

            public static void ThrowIfGreaterThanOrEqual(int value, int other, [CallerArgumentExpression(nameof(value))] string? paramName = null)
            {
                if (value >= other) throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} must be less than {other}.");
            }
        }
    }
}
#endif
