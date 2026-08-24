using System.Reflection;

namespace SheetLite.Tests;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class TestAttribute : Attribute;

internal static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition) throw new AssertionException(message ?? "Expected true but was false.");
    }

    public static void False(bool condition, string? message = null) => True(!condition, message ?? "Expected false but was true.");

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertionException($"{message ?? "Values differ"}.\n  Expected: {expected}\n  Actual:   {actual}");
    }

    public static void NotEqual<T>(T expected, T actual, string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertionException(message ?? $"Both values were {actual}.");
    }

    public static void Null(object? value, string? message = null) => True(value is null, message ?? "Expected null.");
    public static void NotNull(object? value, string? message = null) => True(value is not null, message ?? "Expected non-null.");

    public static void Same(object? expected, object? actual, string? message = null)
    {
        if (!ReferenceEquals(expected, actual)) throw new AssertionException(message ?? $"Expected the same instance but instances differ (expected {expected}, got {actual}).");
    }

    public static void NotSame(object? expected, object? actual, string? message = null)
    {
        if (ReferenceEquals(expected, actual)) throw new AssertionException(message ?? "Expected different instances but got the same one.");
    }

    public static void Single<T>(IEnumerable<T> collection, string? message = null)
    {
        int count = collection.Count();
        if (count != 1) throw new AssertionException(message ?? $"Expected exactly 1 item but found {count}.");
    }

    public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null)
    {
        List<T> expectedItems = [.. expected];
        List<T> actualItems = [.. actual];
        bool equal = expectedItems.Count == actualItems.Count && expectedItems.SequenceEqual(actualItems);
        if (!equal)
            throw new AssertionException($"{message ?? "Sequences differ"}.\n  Expected: [{string.Join(", ", expectedItems)}]\n  Actual:   [{string.Join(", ", actualItems)}]");
    }

    public static void Throws<TException>(Action action, string? message = null) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            throw new AssertionException($"{message ?? "Wrong exception"}: expected {typeof(TException).Name} but caught {ex.GetType().Name}: {ex.Message}");
        }
        throw new AssertionException(message ?? $"Expected {typeof(TException).Name} but nothing was thrown.");
    }

    internal sealed class AssertionException(string message) : Exception(message);
}

internal static class TestRunner
{
    public static int Main()
    {
        var testMethods = Assembly.GetExecutingAssembly()
            .GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            .Where(method => method.GetCustomAttribute<TestAttribute>() is not null)
            .OrderBy(method => method.DeclaringType?.Name)
            .ThenBy(method => method.Name)
            .ToList();

        if (testMethods.Count == 0)
        {
            Console.Error.WriteLine("No tests found.");
            return 2;
        }

        int passed = 0;
        var failures = new List<string>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var method in testMethods)
        {
            string name = $"{method.DeclaringType?.Name}.{method.Name}";
            try
            {
                object? instance = method.IsStatic ? null : Activator.CreateInstance(method.DeclaringType!);
                method.Invoke(instance, null);
                passed++;
                Console.WriteLine($"  PASS {name}");
            }
            catch (Exception ex)
            {
                Exception failure = ex is TargetInvocationException invocation ? invocation.InnerException ?? ex : ex;
                failures.Add($"{name}: {failure.Message}");
                Console.WriteLine($"  FAIL {name}");
                Console.WriteLine($"       {failure.Message.ReplaceLineEndings("\n       ")}");
            }
        }
        clock.Stop();

        Console.WriteLine();
        Console.WriteLine($"{passed}/{testMethods.Count} tests passed in {clock.ElapsedMilliseconds} ms.");
        return failures.Count == 0 ? 0 : 1;
    }
}
