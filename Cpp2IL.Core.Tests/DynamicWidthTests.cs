namespace Cpp2IL.Core.Tests;

public class DynamicWidthTests
{
    [SetUp]
    public void Setup()
    {
        Cpp2IlApi.ResetInternalState();
        TestGameLoader.LoadSimpleV106Game();
    }
    
    [Test]
    public void AssertTheGameLoadedProperly()
    {
        var context = Cpp2IlApi.CurrentAppContext!;
        
        //Yes, this is really dumb and stupid as a test. But it does actually test that the dynamic-width impl is correct (or we'd throw in setup)
        //and we can't actually check the calculation easily from here, because the index widths are only configured while actively loading the metadata.
        Assert.That(context, Is.Not.Null);
    }
}
