using System.Reflection;
using Hookbin.Domain.Entities;

namespace Hookbin.ArchitectureTests.Domain;

/// <summary>
/// Enforces the domain encapsulation contract from commit 9062d68:
/// (1) Domain entities expose no public setters; mutation must go through intent-named methods.
/// (2) Those mutator methods return void, ruling out fluent chaining that hides side effects.
/// (3) Entities are sealed (also covered by RepositoryEntityConventionTests but kept here for self-containment).
/// </summary>
public sealed class EntityEncapsulationTests
{
    private static Type[] DomainEntityTypes() =>
        typeof(WebhookToken).Assembly
            .GetTypes()
            .Where(t =>
                t.Namespace == "Hookbin.Domain.Entities"
                && t.IsClass
                && !t.IsAbstract
                && !t.Name.StartsWith('<'))
            .ToArray();

    [Fact]
    public void DomainEntities_HaveNoPublicSetters()
    {
        var entities = DomainEntityTypes();
        entities.Should().NotBeEmpty("entities should be discovered for the encapsulation check to be meaningful");

        var publicSetters = entities
            .SelectMany(t => t.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .Where(p =>
            {
                var setter = p.SetMethod;
                if (setter is null || !setter.IsPublic) return false;
                // `init` setters are encoded as required custom modifiers on the return parameter
                var isInit = setter.ReturnParameter.GetRequiredCustomModifiers()
                    .Any(m => m == typeof(System.Runtime.CompilerServices.IsExternalInit));
                return !isInit;
            })
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .ToList();

        publicSetters.Should().BeEmpty(
            "domain entities must not expose public setters; use intent-named mutator methods instead. Offenders: {0}",
            string.Join(", ", publicSetters));
    }

    [Fact]
    public void DomainEntities_MutatorMethods_ReturnVoid()
    {
        var entities = DomainEntityTypes();
        var nonVoidMutators = entities
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m =>
                !m.IsSpecialName
                && !m.Name.StartsWith("get_", StringComparison.Ordinal)
                && !m.Name.StartsWith("set_", StringComparison.Ordinal)
                && m.GetCustomAttribute<System.Runtime.CompilerServices.CompilerGeneratedAttribute>() is null
                && m.DeclaringType!.Namespace == "Hookbin.Domain.Entities"
                && m.ReturnType != typeof(void)
                && !typeof(Task).IsAssignableFrom(m.ReturnType)
                && m.Name is not "Equals" and not "GetHashCode" and not "ToString")
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}: {m.ReturnType.Name}")
            .ToList();

        nonVoidMutators.Should().BeEmpty(
            "domain entity mutator methods must return void to prevent fluent-chain ambiguity. Offenders: {0}",
            string.Join(", ", nonVoidMutators));
    }

    [Fact]
    public void DomainEntities_AreSealed()
    {
        var entities = DomainEntityTypes();
        var unsealed = entities.Where(t => !t.IsSealed).Select(t => t.Name).ToList();

        unsealed.Should().BeEmpty("domain entities must be sealed: {0}", string.Join(", ", unsealed));
    }
}
