using System.Collections;
using System.Reflection;
using MemoryPack;
using OutWit.Common.Abstract;

namespace OutWit.Controller.AI.Smt.Tests.Model;

/// <summary>
/// Every wire type of the Model as a value, member by member and found by reflection, so that a
/// member added to a type and missed in its <c>Is</c> or its <c>Clone</c> fails here: for every
/// member of every type of <see cref="SmtModelWireLayout.Types"/>, a changed value makes
/// <c>Is</c> false both ways, <c>Clone</c> copies the value (a list and a model deep), and a
/// MemoryPack round trip keeps it. Every member is filled with a value other than its type's
/// default first, nested models and the items of lists too.
/// </summary>
[TestFixture]
public sealed class SmtWireTypeValueTests
{
    #region Constants

    private static readonly Guid FIRST_ID = new("11111111-2222-3333-4444-555555555555");
    private static readonly Guid SECOND_ID = new("66666666-7777-8888-9999-aaaaaaaaaaaa");

    #endregion

    #region Value Tests

    [Test]
    public void AChangedMemberMakesIsFalseTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var type in SmtModelWireLayout.Types())
            {
                var filled = Filled(type, false);
                Assert.That(filled.Is(Filled(type, false)), Is.True, $"{type.Name}: two instances of the same values");

                foreach (var member in Members(type))
                {
                    var changed = filled.Clone();
                    member.SetValue(changed, Value(member.PropertyType, true));

                    Assert.That(changed.Is(filled), Is.False, $"{type.Name}.{member.Name}: another value, and Is says the two are equal");
                    Assert.That(filled.Is(changed), Is.False, $"{type.Name}.{member.Name}: the other way round");

                    if (!member.PropertyType.IsValueType)
                    {
                        var empty = filled.Clone();
                        member.SetValue(empty, null);
                        Assert.That(empty.Is(filled), Is.False, $"{type.Name}.{member.Name}: null against a value");
                    }
                }
            }
        });
    }

    [Test]
    public void CloneCopiesEveryMemberDeepTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var type in SmtModelWireLayout.Types())
            {
                var filled = Filled(type, false);
                var clone = filled.Clone();

                Assert.That(clone, Is.Not.SameAs(filled), type.Name);
                Assert.That(clone.GetType(), Is.EqualTo(type), $"{type.Name}: a copy of the same type");

                foreach (var member in Members(type))
                {
                    var original = member.GetValue(filled);
                    var copied = member.GetValue(clone);

                    Assert.That(Same(copied, original), Is.True, $"{type.Name}.{member.Name}: Clone does not copy the value");
                    if (original is IList or ModelBase)
                        Assert.That(SharesAnything(copied, original), Is.False, $"{type.Name}.{member.Name}: Clone shares a list or a model with the original");
                }
            }
        });
    }

    [Test]
    public void MemoryPackKeepsEveryMemberTest()
    {
        Assert.Multiple(() =>
        {
            foreach (var type in SmtModelWireLayout.Types())
            {
                var filled = Filled(type, false);
                var back = (ModelBase)MemoryPackSerializer.Deserialize(type, MemoryPackSerializer.Serialize(type, filled))!;

                Assert.That(back.Is(filled), Is.True, type.Name);
                foreach (var member in Members(type))
                    Assert.That(Same(member.GetValue(back), member.GetValue(filled)), Is.True, $"{type.Name}.{member.Name}: the round trip does not keep the value");
            }
        });
    }

    #endregion

    #region Tools

    /// <summary>The members of a wire type that travel, in their order.</summary>
    private static List<PropertyInfo> Members(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<MemoryPackOrderAttribute>() != null)
            .OrderBy(property => property.GetCustomAttribute<MemoryPackOrderAttribute>()!.Order)
            .ToList();
    }

    /// <summary>
    /// An instance of a wire type with every member set to a value other than its type's default;
    /// the other instance differs from the first one in its first member.
    /// </summary>
    private static ModelBase Filled(Type type, bool other)
    {
        var instance = (ModelBase)(Activator.CreateInstance(type) ?? throw new InvalidOperationException($"{type.Name} has no parameterless constructor."));
        var members = Members(type);
        for (var index = 0; index < members.Count; index++)
            members[index].SetValue(instance, Value(members[index].PropertyType, other && index == 0));

        return instance;
    }

    /// <summary>A value of a member's type other than the type's default; the other value differs from the first one.</summary>
    private static object Value(Type type, bool other)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(string))
            return other ? "second" : "first";

        if (underlying == typeof(int))
            return other ? 8 : 7;

        if (underlying == typeof(long))
            return other ? 8L : 7L;

        if (underlying == typeof(double))
            return other ? 0.75 : 0.25;

        if (underlying == typeof(bool))
            return !other;

        if (underlying == typeof(Guid))
            return other ? SECOND_ID : FIRST_ID;

        if (underlying.IsEnum)
        {
            // The last value and the one before it: neither is the default of an enum that has three.
            var values = Enum.GetValues(underlying);
            return values.GetValue(values.Length - (other ? 2 : 1))!;
        }

        if (underlying.IsGenericType && underlying.GetGenericTypeDefinition() == typeof(List<>))
        {
            // Two items; the other list differs from the first one in its second item only, so
            // that a comparison of counts or of references alone would not tell them apart.
            var item = underlying.GetGenericArguments()[0];
            var list = (IList)Activator.CreateInstance(underlying)!;
            list.Add(Value(item, false));
            list.Add(Value(item, other));
            return list;
        }

        if (typeof(ModelBase).IsAssignableFrom(underlying))
            return Filled(underlying, other);

        throw new NotSupportedException($"a member of type {underlying.Name}: teach this test a value of it");
    }

    /// <summary>Whether two values of a member are the same value: models by Is, lists item by item.</summary>
    private static bool Same(object? left, object? right)
    {
        if (left == null || right == null)
            return left == null && right == null;

        if (left is ModelBase model)
            return right is ModelBase other && model.Is(other);

        if (left is IList items)
        {
            if (right is not IList others || items.Count != others.Count)
                return false;

            for (var index = 0; index < items.Count; index++)
            {
                if (!Same(items[index], others[index]))
                    return false;
            }

            return true;
        }

        return left.Equals(right);
    }

    /// <summary>Whether a copy shares a list or a model with its original, at any depth.</summary>
    private static bool SharesAnything(object? copy, object? original)
    {
        if (copy == null || original == null || original is string || original.GetType().IsValueType)
            return false;

        if (ReferenceEquals(copy, original))
            return true;

        if (original is IList items && copy is IList copies)
        {
            for (var index = 0; index < Math.Min(items.Count, copies.Count); index++)
            {
                if (SharesAnything(copies[index], items[index]))
                    return true;
            }

            return false;
        }

        if (original is ModelBase)
            return Members(original.GetType()).Any(member => SharesAnything(member.GetValue(copy), member.GetValue(original)));

        return false;
    }

    #endregion
}
