using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Threading;

namespace ThreeDEngine.Core.Hosting;

/// <summary>
/// Isolated compatibility bridge for legacy parameterless Scene3D/Scene3DControl construction.
/// New code should use an explicit Engine3DBuilder. The aggregate package or a source-drop host
/// registers the default stack during module initialization; modular package consumers are loaded
/// lazily only when the compatibility constructor is actually used.
/// </summary>
internal static class Engine3DDefaultStack3D
{
    private const string AggregateAssemblyName = "Avalonia3D.Engine";
    private const string FactoryTypeName = "ThreeDEngine.Engine3DApplication3D";
    private const string FactoryMethodName = "CreateDefaultEngine";

    private static Func<Engine3D>? s_registeredFactory;

    /// <summary>
    /// Registers the aggregate/source-drop default stack. Registration is intentionally internal:
    /// application code should use Engine3DBuilder rather than replace process-wide compatibility
    /// behavior. The same factory may be observed more than once during startup, but conflicting
    /// factories are rejected deterministically.
    /// </summary>
    internal static void Register(Func<Engine3D> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        var existing = Interlocked.CompareExchange(ref s_registeredFactory, factory, comparand: null);
        if (existing is null || existing == factory)
        {
            return;
        }

        if (existing.Method == factory.Method && ReferenceEquals(existing.Target, factory.Target))
        {
            return;
        }

        throw new InvalidOperationException(
            "A different default Avalonia3D engine factory is already registered. " +
            "Use an explicit Engine3DBuilder when multiple engine stacks coexist in one process.");
    }

    [DynamicDependency(FactoryMethodName, FactoryTypeName, AggregateAssemblyName)]
    public static Engine3D Create()
    {
        var factory = Volatile.Read(ref s_registeredFactory);
        if (factory is not null)
        {
            return InvokeFactory(factory);
        }

        try
        {
            // Package mode: loading the aggregate assembly runs its module initializer, which
            // registers the factory. Source-drop mode never reaches this path because the same
            // module has already registered the factory before application startup.
            _ = Assembly.Load(new AssemblyName(AggregateAssemblyName));
        }
        catch (FileNotFoundException exception)
        {
            throw CreateMissingAggregateException(exception);
        }
        catch (FileLoadException exception)
        {
            throw CreateMissingAggregateException(exception);
        }
        catch (BadImageFormatException exception)
        {
            throw CreateMissingAggregateException(exception);
        }

        factory = Volatile.Read(ref s_registeredFactory);
        if (factory is null)
        {
            throw new InvalidOperationException(
                "Avalonia3D.Engine was loaded, but it did not register a compatible default engine factory. " +
                "Rebuild all Avalonia3D packages from the same version or compose Engine3DBuilder explicitly.");
        }

        return InvokeFactory(factory);
    }

    private static Engine3D InvokeFactory(Func<Engine3D> factory)
    {
        try
        {
            return factory()
                ?? throw new InvalidOperationException("The default Avalonia3D engine factory returned null.");
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                "The default Avalonia3D engine stack failed to initialize. Prefer an explicit " +
                "Engine3DBuilder and select UseOpenGl/UseWebGl, UseGltfAssets and UseJitter2Physics.",
                exception);
        }
    }

    private static InvalidOperationException CreateMissingAggregateException(Exception exception)
    {
        return new InvalidOperationException(
            "Parameterless engine construction requires either the aggregate Avalonia3D.Engine package " +
            "or the complete 3DEngine source-drop, including Compatibility/Engine3DApplication3D.cs. " +
            "Modular applications must create an Engine3DBuilder explicitly and pass the resulting engine " +
            "to Scene3DControl.",
            exception);
    }
}
