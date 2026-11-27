using System.Collections.Concurrent;

namespace NOVA.FIX.Core.Services;

public enum ServiceLifetime
{
    Singleton,
    Transient,
}

/// <summary>
/// Minimal dependency-injection container (no third-party packages).
/// Supports constructor injection, singleton/transient lifetimes and factories.
/// </summary>
public sealed class ServiceContainer
{
    private readonly ConcurrentDictionary<Type, Registration> _registrations = new();
    private readonly ConcurrentDictionary<Type, object> _singletons = new();
    private readonly object _resolveLock = new();

    public ServiceContainer Register<TService, TImplementation>(
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TImplementation : class, TService
    {
        _registrations[typeof(TService)] = new Registration(
            typeof(TImplementation), lifetime, null);
        return this;
    }

    public ServiceContainer Register<TService>(Func<ServiceContainer, TService> factory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TService : class
    {
        _registrations[typeof(TService)] = new Registration(
            typeof(TService), lifetime, c => factory(c));
        return this;
    }

    public ServiceContainer RegisterInstance<TService>(TService instance) where TService : class
    {
        _registrations[typeof(TService)] = new Registration(
            typeof(TService), ServiceLifetime.Singleton, _ => instance);
        _singletons[typeof(TService)] = instance;
        return this;
    }

    public bool IsRegistered(Type serviceType) => _registrations.ContainsKey(serviceType);

    public TService Resolve<TService>() => (TService)Resolve(typeof(TService));

    public object Resolve(Type serviceType)
    {
        if (_singletons.TryGetValue(serviceType, out var existing))
            return existing;

        if (!_registrations.TryGetValue(serviceType, out var reg))
            throw new InvalidOperationException(
                $"Service {serviceType.Name} is not registered.");

        object instance;
        if (reg.Factory is not null)
        {
            instance = reg.Factory(this);
        }
        else
        {
            var ctor = reg.Implementation.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"{reg.Implementation.Name} has no public constructor.");
            var args = ctor.GetParameters()
                .Select(p => Resolve(p.ParameterType))
                .ToArray();
            instance = ctor.Invoke(args);
        }

        if (reg.Lifetime == ServiceLifetime.Singleton)
        {
            instance = _singletons.GetOrAdd(serviceType, instance);
        }
        return instance;
    }

    private sealed record Registration(
        Type Implementation,
        ServiceLifetime Lifetime,
        Func<ServiceContainer, object>? Factory);
}
