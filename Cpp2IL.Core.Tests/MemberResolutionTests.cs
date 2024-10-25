using System.Linq;

namespace Cpp2IL.Core.Tests;

public class MemberResolutionTests
{
    [SetUp]
    public void Setup()
    {
        Cpp2IlApi.ResetInternalState();
        TestGameLoader.LoadSimple2019Game();
    }

    [Test]
    public void TestMethodResolving()
    {
        var appContext = Cpp2IlApi.CurrentAppContext;

        var methodContext = appContext!.AllTypes.SelectMany(t => t.Methods).First(m => m.Definition is not null);

        Assert.That(appContext.ResolveContextForMethod(methodContext.Definition), Is.EqualTo(methodContext));
    }

    [Test]
    public void TestFieldResolving()
    {
        var appContext = Cpp2IlApi.CurrentAppContext;

        var fieldContext = appContext!.AllTypes.SelectMany(t => t.Fields).First(f => f.BackingData?.Field is not null);

        Assert.That(appContext.ResolveContextForField(fieldContext.BackingData!.Field), Is.EqualTo(fieldContext));
    }

    [Test]
    public void TestEventResolving()
    {
        var appContext = Cpp2IlApi.CurrentAppContext;

        var eventContext = appContext!.AllTypes.SelectMany(t => t.Events).First(e => e.Definition is not null);

        Assert.That(appContext.ResolveContextForEvent(eventContext.Definition), Is.EqualTo(eventContext));
    }

    [Test]
    public void TestPropertyResolving()
    {
        var appContext = Cpp2IlApi.CurrentAppContext;

        var propertyContext = appContext!.AllTypes.SelectMany(t => t.Properties).First(p => p.Definition is not null);

        Assert.That(appContext.ResolveContextForProperty(propertyContext.Definition), Is.EqualTo(propertyContext));
    }
}
