namespace Clockwork.Instrumentation.Tests.Infrastructure;

/// <summary>
/// The shared source for the two support assemblies used across the golden corpus: an "API" assembly
/// declaring the controlled members a fixture calls, and a "shims" assembly declaring the static
/// replacements the engine redirects to. Both are referenced by fixtures (the API directly, the shims
/// after rewriting), so keeping them in a third assembly avoids any circular reference.
/// </summary>
internal static class FixtureSources
{
    public const string ApiAssemblyName = "ClockworkFixtures.Api";
    public const string ShimAssemblyName = "ClockworkFixtures.Shims";

    /// <summary>Source for the controlled-API assembly the fixtures call into.</summary>
    public const string Api = """
        namespace ClockworkFixtures.Api
        {
            public static class RealClock
            {
                public static long UtcNowTicks() => 100L;
            }

            public sealed class Service
            {
                public int Value;
                public Service(int value) { Value = value; }
                public int GetValue() => Value;
            }

            public sealed class Widget
            {
                public int X;
                public Widget(int x) { X = x; }
            }

            public static class GenericOps
            {
                public static T Echo<T>(T value) => value;
            }

            public interface IProbe { int Probe(); }

            public struct StructProbe : IProbe
            {
                public int N;
                public int Probe() => N;
            }

            public static class Forbidden
            {
                public static void DangerousWrite(string message) { }
            }

            public sealed class Meterable
            {
                public int Measure() => 5;
            }
        }
        """;

    /// <summary>Source for the shim assembly the engine redirects controlled calls to.</summary>
    public const string Shims = """
        using ClockworkFixtures.Api;

        namespace ClockworkFixtures.Shims
        {
            public static class Recorder
            {
                public static readonly System.Collections.Generic.List<string> Events = new();
                public static void Reset() => Events.Clear();
            }

            public static class ClockShim
            {
                public static long UtcNowTicks()
                {
                    Recorder.Events.Add("UtcNowTicks");
                    return 999L;
                }

                public static int GetValue(Service self)
                {
                    Recorder.Events.Add("GetValue");
                    return 7;
                }

                public static Widget CreateWidget(int x)
                {
                    Recorder.Events.Add("CreateWidget");
                    return new Widget(x + 1000);
                }

                public static T Echo<T>(T value)
                {
                    Recorder.Events.Add("Echo");
                    return value;
                }

                public static int WrapMeasure(int value)
                {
                    Recorder.Events.Add("WrapMeasure");
                    return value + 1;
                }

                public static void Reject(string api)
                {
                    Recorder.Events.Add("Reject:" + api);
                    throw new System.InvalidOperationException("Rejected: " + api);
                }
            }
        }
        """;
}
